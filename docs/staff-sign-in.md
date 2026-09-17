# Local staff sign-in

This step adds ASP.NET Core Identity accounts, password hashing, roles, cookie-based
sign-in/sign-out, and authenticated catalogue access. No internet identity provider
is used. The `StaffIdentity` migration has been tested in an isolated PostgreSQL
database but has not yet been applied to the user's development database in this step.

## 1. Create the account tables

In a terminal opened in the project root:

```sh
dotnet tool restore
dotnet ef database update --project backend/PharmacyPos.Api -- --environment Development
```

The migration adds Identity's seven user/role/claim/token tables. It does not modify
catalogue data, create production users, or set anyone's password.

## 2. Create your first admin

Run this in an interactive terminal after the migration succeeds:

```sh
dotnet run --project backend/PharmacyPos.Api --no-launch-profile -- --environment Development --create-admin
```

Enter a new staff username and password at the prompts. This password is separate
from the PostgreSQL administrator and `pharmacy_app` database passwords. Use at least
12 characters with uppercase, lowercase, a number, and a symbol. Input is hidden;
never put the password into command arguments, source files, screenshots, or chat.

The command creates Admin, Manager, and Operator roles and your first Admin account.
It exits without starting a server. Once an admin exists, it refuses further bootstrap
creation, and it never promotes an existing username. Role/account writes are one
transaction, with a database lock guarding concurrent first-admin creation. There is
no anonymous registration or web-based bootstrap endpoint. The command uses local
database access and is intended for a trusted server/development administrator.

## 3. Start the app

Restart the backend:

```sh
dotnet run --project backend/PharmacyPos.Api --launch-profile http
```

Keep the frontend running and open http://localhost:5173. Sign in with your new
staff credentials. The header displays your username and role. Sign out ends the
browser session. A lost connection must not be treated as confirmed sign-out.

If tables are missing or services are stopped, the sign-in page may show a connection
error. Apply the migration and restart the backend before entering credentials.

## Server rules and API

| Endpoint | Access and behavior |
|---|---|
| `GET /api/auth/session` | Returns current username/roles or null and a CSRF request token; never cached |
| `POST /api/auth/login` | JSON username/password plus `X-CSRF-TOKEN`; nonpersistent session cookie on success |
| `POST /api/auth/logout` | Requires a signed-in session and valid CSRF token; clears session cookie |
| `GET /api/medicines` | Requires a signed-in Admin, Manager, or Operator; available in Development and Production |

`Staff` is the shared catalogue policy. `AdminOnly` is defined and tested for future
admin actions; those business endpoints must explicitly require it when added.
There are no cost, offer, approval, or staff-management endpoints yet.

- Cookies are HTTP-only and SameSite Strict. Login and logout validate antiforgery
  tokens. Session cookies are not stored in browser local storage.
- A session has a 30-minute sliding timeout. Identity security-stamp validation is
  used on every request; disabled accounts are rejected immediately.
- Five failed passwords lock an existing account for 15 minutes. Login attempts are
  additionally limited to ten per minute per remote IP. Missing users, wrong passwords,
  and locked users receive the same public failure message.
- Outside Development, cookies require HTTPS. The local HTTP exception is for Mac
  development only. The Windows installation uses HTTPS and limits the firewall rule to the private local network.
- Persist and protect ASP.NET Core Data Protection keys for production so cookie
  encryption survives service restarts. The Windows installer configures a service identity, restricted key directory and machine DPAPI protection.
- Admin staff creation, disabling and password recovery have an audit history. No public self-service reset is implemented.

## Verification

`backend/PharmacyPos.AuthChecks` is an executable integration check using Microsoft's
test host and a real PostgreSQL test database. Set `PHARMACY_TEST_CONNECTION` to a
fresh disposable database whose name contains `pharmacy_auth_test`, then run:

```sh
dotnet run --project backend/PharmacyPos.AuthChecks
```

The harness applies migrations and creates disposable test accounts; never point it
at the pharmacy database. The constant password in this test fixture is not a real
account credential. The test host uses ephemeral Data Protection keys and an overridden
database connection rather than the application's private database settings.

Checks cover password hashing, anonymous catalogue rejection, login/logout CSRF,
incorrect passwords, successful operator login, role-policy decisions, sign-out,
five-failure lockout, and the ten-attempt login-rate boundary. Browser verification
also checked operator login, catalogue display, and return to the sign-in form after
logout, using only temporary accounts and a separate temporary PostgreSQL instance.
