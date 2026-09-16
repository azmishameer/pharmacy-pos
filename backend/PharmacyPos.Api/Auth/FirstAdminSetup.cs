using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmacyPos.Api.Data;

namespace PharmacyPos.Api.Auth;

public static class FirstAdminSetup
{
    public static async Task RunAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Run first-admin setup in an interactive terminal.");
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        Console.Write("Choose the first admin username: ");
        var username = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("A username is required.");
        Console.WriteLine("Use at least 12 characters, including uppercase, lowercase, a number, and a symbol.");
        var password = ReadPassword("Password: ");
        if (password != ReadPassword("Confirm password: ")) throw new InvalidOperationException("Passwords did not match.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        // Serialize first-admin creation; never promote an existing account by username.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425910)");
        if ((await users.GetUsersInRoleAsync("Admin")).Count > 0)
            throw new InvalidOperationException("An admin already exists. First-admin setup is closed.");
        foreach (var role in StaffAuthentication.Roles)
            if (!await roles.RoleExistsAsync(role)) Ensure(await roles.CreateAsync(new IdentityRole(role)));
        var user = new IdentityUser(username) { LockoutEnabled = true };
        Ensure(await users.CreateAsync(user, password));
        Ensure(await users.AddToRoleAsync(user, "Admin"));
        await transaction.CommitAsync();
        Console.WriteLine("First admin created. You can now sign in using this username and password.");
    }

    public static async Task ResetAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Run admin recovery in an interactive terminal.");
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmacyDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var admins = await users.GetUsersInRoleAsync("Admin");
        if (admins.Count == 0) throw new InvalidOperationException("No admin account exists.");
        Console.WriteLine("Existing admin usernames: " + string.Join(", ", admins.Select(u => u.UserName)));
        Console.Write("Admin username to recover: ");
        var name = Console.ReadLine()?.Trim();
        var user = string.IsNullOrEmpty(name) ? null : await users.FindByNameAsync(name);
        if (user == null || !await users.IsInRoleAsync(user, "Admin"))
            throw new InvalidOperationException("That is not an existing admin account.");
        var password = ReadPassword("New password: ");
        if (password != ReadPassword("Confirm new password: "))
            throw new InvalidOperationException("Passwords did not match.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(718425910)");
        await db.Entry(user).ReloadAsync();
        if (!await users.IsInRoleAsync(user, "Admin")) throw new InvalidOperationException("Account is no longer an admin.");
        var account = await db.StaffAccounts.SingleOrDefaultAsync(a => a.UserId == user.Id);
        if (account?.Disabled == true) throw new InvalidOperationException("Account is disabled. Password recovery does not enable access.");
        Ensure(await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), password));
        Ensure(await users.ResetAccessFailedCountAsync(user));
        Ensure(await users.SetLockoutEndDateAsync(user, null));
        Ensure(await users.UpdateSecurityStampAsync(user));
        if (account == null) { account = new StaffAccount { UserId = user.Id }; db.StaffAccounts.Add(account); }
        account.Version = Guid.NewGuid();
        db.StaffAccountEvents.Add(new StaffAccountEvent {
            Id = Guid.NewGuid(), TargetId = user.Id, ActorId = user.Id,
            Action = "LocalAdminRecovery", Reason = "Local terminal recovery; executed by server operator, not an authenticated web session.",
            At = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        Console.WriteLine("Admin password reset. Temporary lockout cleared and previous sessions invalidated.");
    }

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(x => x.Description)));
    }

    private static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); }
            else if (!char.IsControl(key.KeyChar) && chars.Count < 1024) chars.Add(key.KeyChar);
        }
        Console.WriteLine();
        return new string(chars.ToArray());
    }
}
