# Manual database backup and restore verification

GitHub stores source code, not pharmacy records. A database backup includes medicines,
stock, sales, refunds, audit history and staff account password hashes. Treat it as private.
The archive is not encrypted. Use encrypted storage and copy completed backups to a
separate device/location; a copy only on the same computer does not protect against disk
loss. Keep the `.dump` and matching `.dump.json` together. Both are ignored by Git in
`backups/`. Development User Secrets, PostgreSQL server roles, application configuration
and receipt-printer settings are not included.

## Create a backup

Run from the repository root while the app can remain running:

```sh
dotnet run --project backend/PharmacyPos.Api --launch-profile http -- --backup-database
```

This uses the configured database credentials without printing them or passing a password
in command-line arguments. PostgreSQL `pg_dump` creates a consistent custom-format
archive. Counts for every public application table are collected from the same exported
snapshot. The manifest records these counts, the PostgreSQL major version, creation time
and SHA-256 checksum. No medicine, customer or password records are printed.

Files go into the repository's `backups/` folder with a unique timestamp/name. Incomplete
output is removed on normal failure. On macOS/Linux the new directory is owner-only and
files are readable/writable only by their owner. Windows uses the directory's inherited
permissions; keep it in a private user directory. No old backups are deleted automatically.
The command has a 30-minute timeout. Avoid database migrations during a backup.

PostgreSQL tools are found in the EDB PostgreSQL 18 installation on macOS/Windows,
or on PATH. Override `Backup__PostgresBin` for another installation and
`Backup__Directory` for another destination. Use a compatible pg_dump version; the
verification server must use the same major PostgreSQL version as the source. If
pg_dump is missing or fails, the command reports failure instead of a completed archive.

## Verify a backup without touching the working database

Python 3 and the PostgreSQL command-line tools are required. Run:

```sh
python3 scripts/verify-backup.py "/absolute/path/to/your-backup.dump"
```

The script privately prompts for the PostgreSQL verification role's password. This is
normally the installer-created `postgres` role, which needs permission to create and
drop temporary databases. It is not the app's Admin login password. Options `--host`,
`--port`, `--user` and `--pg-bin` select a different verification server. Use only a server
you control and enough free disk space for the restored database.

The script checks the manifest and checksum before connecting. It creates only a fresh,
randomly named `pharmacy_restore_check_...` database, restores with no original ownership
or grants, checks the public table list and every row count, and removes the temporary
database even if verification fails. It does not accept a working database as the restore
target and does not use `--clean`. If cleanup fails, it prints only the temporary database
name to remove. `--no-password` is solely for a local isolated test server using trust
authentication. Restore only trusted backups: database archives can contain SQL code.

A successful restore test checks archive integrity, PostgreSQL schema/constraint restoration
and exact table counts. It does not test application sign-in, printer setup or every business
workflow on the restored copy. The checksum detects changes but is not a digital signature.

## Actual disaster recovery

This milestone does not overwrite or switch the working database. For an actual recovery,
stop writes, keep the current database, restore a verified archive into a new database,
configure the intended database role/permissions, then validate the app against that new
database before switching its connection. Reapply secrets/settings separately. Existing
staff passwords are restored as hashes; sessions may require fresh login. Do not run a
restore into the current database as a routine verification step.

The command above remains available for manual backups. Daily automatic backups are
described below. Off-device copying, encryption and a retention schedule are future work.

## Automatic daily backups

Apply `AutomaticDailyBackups`, restart the backend, and open **Database backups** as
admin. Automatic backups are enabled by default at **02:00 Asia/Dhaka**, checked once
per minute by the pharmacy backend. This does not depend on Codex or an open browser.
It does depend on the backend process and computer staying awake and running.

After 2 AM, startup/resume catches up today's backup if no automatic backup has completed
for today. Before 2 AM it waits until 2 AM. It does not create fictitious historical
backups for days the server was off. One current snapshot covers the data available now.
The first start after applying this migration also follows this rule, so it may create a
backup immediately if it is already after 2 AM. Manual backups do not satisfy the daily
schedule or appear in automatic-run history.

Admin pause/resume is persisted in the database and requires a reason and confirmation.
The API enforces Admin-only access and CSRF checks. Each setting change is audited with
the admin, time and reason. Version checks prevent stale changes; repeated identical
requests do not duplicate history. Pause stops new runs, not a backup already in progress.
Manual backup remains available while automatic backups are paused.

The scheduler retries failed backups after 15 minutes while enabled. Interrupted runs
are marked failed when a worker next obtains the database lock; a fresh backup can then
run. An interrupted process can leave an unrecorded archive or partial file; no prior
backup is overwritten or deleted. Database advisory locks prevent concurrent scheduler
workers from starting the same job; a unique success-per-day index is a second safeguard.
The archive snapshot precedes the completion update, so a restored database can contain
its own run marked Running. The scheduler recovers it using the same interrupted-run rule.

The admin screen shows the latest 20 automatic runs and setting changes. Complete means
archive creation succeeded, not that a restore test was performed. Keep performing restore
verification separately. Failures show a sanitized message rather than connection secrets.
`Backup__AutomaticEnabled=false` disables the worker for test/verification environments;
the admin screen distinguishes this server override from an admin pause. The background
worker supports the existing deployment environment; admin API routes remain development-
only until production deployment is implemented.

Backups still remain on this computer unless copied separately. Automatic off-device
copying, encryption and retention cleanup have not been added. Deploying the backend as an
always-running Windows service is future deployment work; closing its development terminal
stops the scheduler along with the application.
