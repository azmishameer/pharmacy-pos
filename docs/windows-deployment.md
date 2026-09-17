# Windows pharmacy server installation

This release serves the React website and ASP.NET Core API together. One Windows x64 PC runs the application and PostgreSQL; cashier PCs only need an up-to-date browser. It operates over the pharmacy's local network without an internet connection after installation. The server must remain powered on and awake. This package is for a private pharmacy network, not direct public internet exposure.

## What is included

- A self-contained Windows x64 application: client machines do not need Node.js, Git, Vite, Visual Studio or the .NET SDK.
- Windows service `PharmacyPos`, automatic startup and restart after a crash, running under its own low-privilege virtual service account.
- HTTPS on port 8443, private-network firewall access limited to the local subnet.
- A private installation file, certificate and session keys in `C:\ProgramData\PharmacyPos`. These are separate from versioned application files under `C:\Program Files\PharmacyPos\releases`.
- Database migrations, first-admin creation, backup, password recovery and update commands.
- Existing daily 02:00 Bangladesh-time backup scheduling, including the admin pause option. Backups go to `C:\ProgramData\PharmacyPos\backups`.

Windows service registration, firewall rules, certificate trust and printer hardware still require a Windows acceptance test. Building this package on a Mac does not test those operating-system features.

## 1. Prepare the server PC

Use a supported Windows 11 x64 PC (or supported Windows Server). Set its network profile to **Private** for the pharmacy LAN, prevent sleep during operation, and reserve its LAN address in the router. Use a stable computer name such as `PHARMACY-SERVER`. Cashier PCs must resolve that name to the server's LAN address (router/local DNS). Do not forward this port on the internet router.

Install PostgreSQL 18 from the official PostgreSQL Windows download page, retaining the command-line tools. Keep PostgreSQL listening locally; cashier PCs connect to the application, not directly to the database. The standard port is 5432.

Open PostgreSQL SQL Shell as the `postgres` administrator and run these lines separately:

```sql
CREATE ROLE pharmacy_app LOGIN;
\password pharmacy_app
CREATE DATABASE pharmacy_pos OWNER pharmacy_app;
```

Choose a new password for this client. Do not reuse your developer database password. `\password` prompts without putting the password in the SQL command history. This account owns only this pharmacy database; do not grant superuser rights.

## 2. Install the release

Copy the release ZIP to the server, compare its SHA-256 with the accompanying `.sha256` file, and extract it. Open **Windows PowerShell → Run as administrator**, then change to the extracted folder containing `Install.ps1`.

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Install.ps1 -ServerName PHARMACY-SERVER
```

Use the actual server computer name. If PostgreSQL is installed elsewhere, supply `-PostgresBin 'D:\PostgreSQL\18\bin'`. The optional `-DatabaseName`, `-DatabaseUser` and `-Port` parameters override the defaults.

The installer verifies package file hashes, asks for the `pharmacy_app` database password, applies migrations, and prompts for the pharmacy's first admin username and password. Use at least 12 characters with uppercase, lowercase, a number and a symbol. It then registers and starts the service.

Open `https://PHARMACY-SERVER:8443` on the server and sign in. Enter this client's receipt settings and create operator accounts. No developer accounts, medicine records or passwords are included in the package.

The HTTPS certificate is specific to this server. Its public `.cer` is safe to copy to cashier PCs; the private `.pfx`, installation JSON, keys and backups must remain protected. The installer restricts the data directory to Windows administrators, SYSTEM and the application service. Windows administrators can access server secrets; POS operators cannot access them through the app.

## 3. Connect a cashier PC

Copy **only** `C:\ProgramData\PharmacyPos\pharmacy-server.cer` and `Trust-Server.ps1` to the cashier PC. Run this as the Windows user who will use the POS:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\Trust-Server.ps1 -CertificatePath .\pharmacy-server.cer
```

Verify the displayed certificate thumbprint with the server administrator before typing `TRUST`. Then open the same hostname URL, `https://PHARMACY-SERVER:8443`. Bookmark it. Repeat certificate trust for each Windows user who needs access.

Do not use a raw IP URL or bypass browser certificate warnings: the name in the URL must match the certificate. If the hostname cannot be found, configure local DNS/router resolution first. If the page does not connect, check that the server is running, both PCs are on the same private LAN, and Windows Firewall permits the installed rule.

## 4. Acceptance test before real use

On the Windows server and at least one cashier PC:

1. Sign in as admin and operator; confirm operator restrictions.
2. Enter a clearly labelled test medicine and delivery; complete a test cash sale and verify stock, totals and receipt.
3. Check returns, original-receipt confirmation and held-stock handling.
4. Print an 80 mm receipt on the real printer, checking logo, wrapping, margins and paper feed.
5. Run a manual backup. Verify restoration into a separate test database using the repository's `scripts/verify-backup.py`; never restore over the live database for a test.
6. Restart Windows and confirm the service, sign-in and saved data still work without development terminals. Check the backup schedule in the admin screen.

Use a separate training installation/database for these transactions. Do not mix trial records into the real pharmacy's opening inventory.

## Maintenance

Run from the release folder in an administrator PowerShell:

```powershell
.\Maintain.ps1 -Action Status
.\Maintain.ps1 -Action Backup
.\Maintain.ps1 -Action ResetAdminPassword
.\Maintain.ps1 -Action Stop
.\Maintain.ps1 -Action Start
```

Keep a separate protected copy of database backups and their manifests on another device. Local backups alone do not protect against disk failure. Also retain installation settings and session keys securely. Windows DPAPI protects session keys on the original machine; a move to a different machine should generate new keys and require fresh logins.

The local HTTPS certificate expires after two years; its expiry is shown by `Status`. Before it expires, an administrator must replace it and distribute the new public certificate to every cashier account. Renaming the server also requires a matching replacement certificate and updated AllowedHosts/configuration. A publicly hosted deployment will use a publicly trusted, automatically renewed certificate instead.

## Updating

Extract the next trusted ZIP on the server. From its folder in administrator PowerShell:

```powershell
.\Update.ps1
```

This stops the service, backs up the existing database, applies new migrations, switches to the new release and starts the service. Configuration, receipt images in the database, accounts, stock and backup history are retained. Allow a maintenance window; cashiers cannot use the app during this update. Retain the old release and the pre-update backup until acceptance checks pass.

If an update fails, the service may remain stopped. Keep both releases and the backup; read the error before taking further action. Do not automatically switch to old binaries after schema changes. Recovery may require the matching pre-update database backup and application version, with downtime and explicit consideration of any later transactions.

## Interrupted first installation

Do not delete an existing `installation.json` or database to repeat setup. Correct the reported issue as a Windows administrator. After configuration has been written, these commands can finish the remaining steps:

```powershell
.\Maintain.ps1 -Action Migrate
.\Maintain.ps1 -Action CreateAdmin
.\Register-Service.ps1
```

Skip `CreateAdmin` if it already succeeded; it intentionally refuses to replace an existing admin. If service registration already succeeded, use `Maintain.ps1 -Action Start`. A failure before installation settings were written requires inspecting the incomplete release/certificate files before retrying; no script deletes them automatically. Logs are available through Windows Event Viewer under Application; errors must be investigated before live use.

## Building and sharing from GitHub

On a development machine, install Node.js 24, .NET SDK 10 and Python 3. Clone your personal repository and run:

```sh
python3 scripts/build-release.py
```

The script installs locked frontend dependencies, builds/lints React, publishes the Windows x64 runtime and backend, and generates a ZIP with a file-hash manifest under `artifacts/`. Internet access is needed when building/downloading dependencies. Build artifacts, certificates and local secrets are ignored by Git. Share the ZIP and its SHA-256 file with the client. A developer with repository access can build the same package; a client receiving only the package does not need GitHub access.

## Moving online later

The browser/API/PostgreSQL design is portable. `PHARMACY_CONFIG` selects an external JSON configuration file; environment variables can override its settings. The same application can be published for a Linux or Windows server. Serve the built frontend from `wwwroot`, use HTTPS, and point the server-side connection at the restored PostgreSQL database.

A future online deployment needs a domain, trusted HTTPS certificate, durable database and backup storage, and a tested migration/cutover. Stop local writes, take and verify a final backup, restore it online, verify counts and application flows, then move clients to the new URL. Do not operate two independent writable copies. Session keys and certificate paths must be configured for the new host. Behind a proxy, explicitly configure trusted forwarded headers; this release does not trust arbitrary forwarded headers.

This release supports one application server. Multiple application instances require shared session keys and coordinated login rate limiting as well as database-backed job coordination. It is not an offline-sync system: once hosted online, cashier PCs need connectivity to that server.

References: [Microsoft Windows service hosting](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-10.0), [ASP.NET Core Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction?view=aspnetcore-10.0), [PostgreSQL Windows downloads](https://www.postgresql.org/download/windows/).
