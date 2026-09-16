# Staff accounts

Apply the `StaffAccountManagement` migration and restart the backend. Sign in as an
admin and open **Staff accounts** from the medicine catalogue.

Admins can create Operator accounts, search the staff list, reset operator passwords,
and disable or enable operators. Existing accounts stay enabled after migration.
Admin and Manager accounts cannot be modified through this screen. Accounts are not
deleted, so existing sales, refunds and stock records retain their staff attribution.

Each account action records the acting admin, target account, action, time and reason.
Account history shows the latest 100 actions. Passwords are never included in the
account list or audit. Set an initial password of at least 12 characters with uppercase,
lowercase, a number and a symbol, and share it privately with the staff member.
There is currently no forced first-login password change or self-service password reset.

Disabling blocks sign-in and invalidates existing sessions. Password resets also
invalidate existing sessions and clear temporary login lockout, but do not enable a
disabled account. Re-enabling requires a fresh login. Session invalidation is enforced
on the next server request; previously rendered page contents may remain visible.

Requests require admin authorization and CSRF protection. Account versions reject
stale changes from another admin, and request IDs prevent duplicate audit events on
unchanged retries. The endpoints remain development-only, consistent with the rest
of the application preview.

Integration checks cover creation, password rules, case-insensitive duplicate names,
permissions, CSRF, retries, stale changes, session invalidation, disabled sign-in,
re-enabling and audit attribution. They use a separate disposable database.

## Local admin recovery

A server operator with local database access can run:

```sh
dotnet run --project backend/PharmacyPos.Api --launch-profile http -- --reset-admin-password
```

The interactive command lists existing admin usernames, asks which account to
recover, and reads the new password twice without echoing it. Never supply passwords
as command arguments. It resets only an existing admin, clears temporary lockout,
and invalidates old sessions. It does not enable disabled accounts or promote users.
An audit event explicitly identifies local terminal recovery rather than an
authenticated web action. No web recovery endpoint is exposed.
