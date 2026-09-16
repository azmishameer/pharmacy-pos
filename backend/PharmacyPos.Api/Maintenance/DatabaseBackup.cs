using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
namespace PharmacyPos.Api.Maintenance;

public static class DatabaseBackup
{
    public sealed record TableCount(string Schema, string Table, long Rows);
    public sealed record Manifest(int Version, DateTimeOffset CreatedAt, string Archive, string Sha256, int PostgreSqlMajor, List<TableCount> Tables);
    public static string Tool(string name, IConfiguration config) {
        var bin=config["Backup:PostgresBin"];
        if(!string.IsNullOrWhiteSpace(bin))return Path.Combine(bin,name+(OperatingSystem.IsWindows()?".exe":""));
        if(OperatingSystem.IsMacOS()&&File.Exists("/Library/PostgreSQL/18/bin/"+name))return "/Library/PostgreSQL/18/bin/"+name;
        if(OperatingSystem.IsWindows()) {
            var candidate=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"PostgreSQL","18","bin",name+".exe");
            if(File.Exists(candidate))return candidate;
        }
        return name;
    }
    static ProcessStartInfo StartInfo(string tool,NpgsqlConnectionStringBuilder settings) {
        var p=new ProcessStartInfo(tool){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        // Avoid inherited libpq settings redirecting this backup to another database.
        foreach(var key in p.Environment.Keys.Where(k=>k.StartsWith("PG",StringComparison.OrdinalIgnoreCase)).ToArray())p.Environment.Remove(key);
        p.Environment["PGHOST"]=settings.Host;p.Environment["PGPORT"]=settings.Port.ToString();p.Environment["PGDATABASE"]=settings.Database;
        p.Environment["PGUSER"]=settings.Username;p.Environment["PGPASSWORD"]=settings.Password;p.Environment["PGCONNECT_TIMEOUT"]="15";
        p.Environment["PGSSLMODE"]=settings.SslMode switch{SslMode.VerifyCA=>"verify-ca",SslMode.VerifyFull=>"verify-full",_=>settings.SslMode.ToString().ToLowerInvariant()};
        if(!string.IsNullOrEmpty(settings.RootCertificate))p.Environment["PGSSLROOTCERT"]=settings.RootCertificate;
        return p;
    }
    static FileStream PrivateFile(string path) {
        var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
        if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
        return new FileStream(path,options);
    }
    public static async Task<string> RunAsync(IConfiguration config,string contentRoot,CancellationToken cancellationToken=default) {
        var connection=config.GetConnectionString("Pharmacy");var password=config["Database:Password"];
        if(string.IsNullOrWhiteSpace(connection)||string.IsNullOrEmpty(password))throw new InvalidOperationException("Database settings are missing. Configure development User Secrets first.");
        var settings=new NpgsqlConnectionStringBuilder(connection){Password=password};
        if(string.IsNullOrEmpty(settings.Database)||string.IsNullOrEmpty(settings.Username)||string.IsNullOrEmpty(settings.Host)||settings.Host.Contains(','))
            throw new InvalidOperationException("Backup needs an explicit single database host, database name and username.");
        var directory=Path.GetFullPath(config["Backup:Directory"]??Path.Combine(contentRoot,"../../backups"));
        if(!Directory.Exists(directory)) {
            if(OperatingSystem.IsWindows())Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        }
        var archive=Path.Combine(directory,$"pharmacy-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.dump");
        var partial=archive+".partial";var manifestPath=archive+".json";
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromMinutes(30));var ct=timeout.Token;
        await using var db=new NpgsqlConnection(settings.ConnectionString);await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
        await using var snapshotCommand=new NpgsqlCommand("SELECT pg_export_snapshot()",db,tx);
        var snapshot=(string)(await snapshotCommand.ExecuteScalarAsync(ct))!;
        var tables=new List<(string Schema,string Name)>();
        await using(var list=new NpgsqlCommand("SELECT schemaname, tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename",db,tx))
        await using(var reader=await list.ExecuteReaderAsync(ct))while(await reader.ReadAsync(ct))tables.Add((reader.GetString(0),reader.GetString(1)));
        if(tables.Count==0)throw new InvalidOperationException("The configured database has no application tables; backup was not created.");
        var counts=new List<TableCount>();var quote=new NpgsqlCommandBuilder();
        foreach(var table in tables) {
            await using var count=new NpgsqlCommand($"SELECT count(*) FROM {quote.QuoteIdentifier(table.Schema)}.{quote.QuoteIdentifier(table.Name)}",db,tx);
            counts.Add(new(table.Schema,table.Name,(long)(await count.ExecuteScalarAsync(ct))!));
        }
        var completed=false;
        try {
            var info=StartInfo(Tool("pg_dump",config),settings);
            foreach(var arg in new[]{"--format=custom","--no-owner","--no-acl","--no-password","--snapshot="+snapshot})info.ArgumentList.Add(arg);
            using var process=Process.Start(info)??throw new InvalidOperationException("Could not start pg_dump.");
            using var registration=ct.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
            try {
                var errors=process.StandardError.ReadToEndAsync(ct);
                await using(var file=PrivateFile(partial))await process.StandardOutput.BaseStream.CopyToAsync(file,ct);
                await process.WaitForExitAsync(ct);await errors;
                if(process.ExitCode!=0)throw new InvalidOperationException("pg_dump failed. Check the configured PostgreSQL tools version, database access and free disk space. No completed backup was kept.");
            } finally {
                if(!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            }
            if(new FileInfo(partial).Length==0)throw new InvalidOperationException("pg_dump produced an empty backup.");
            string digest;await using(var file=File.OpenRead(partial))digest=Convert.ToHexString(await SHA256.HashDataAsync(file,ct));
            var manifest=new Manifest(1,DateTimeOffset.UtcNow,Path.GetFileName(archive),digest,db.PostgreSqlVersion.Major,counts);
            await using(var file=PrivateFile(manifestPath))await JsonSerializer.SerializeAsync(file,manifest,new JsonSerializerOptions{WriteIndented=true},ct);
            File.Move(partial,archive);completed=true;
            Console.WriteLine("Backup created: "+archive);
            Console.WriteLine("Verification manifest: "+manifestPath);
            Console.WriteLine("The archive and table counts use the same database snapshot. Keep both files together. A restore test is still required.");
        }finally {
            if(!completed){if(File.Exists(partial))File.Delete(partial);if(File.Exists(manifestPath))File.Delete(manifestPath);}
        }
        await tx.RollbackAsync(ct);
        return archive;
    }
}
