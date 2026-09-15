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
