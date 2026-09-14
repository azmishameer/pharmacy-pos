# First catalogue migration

The code now defines five tables: manufacturers, generic ingredients, dosage forms,
medicines, and medicine ingredients/strengths. `InitialCatalogue` is the migration
that creates them. It has been applied, tested, and rolled back in a temporary
PostgreSQL 18 instance; it has not been applied to `pharmacy_pos_dev` in this step.

## Apply to the development database

From the project root, after configuring the existing User Secrets:

```sh
dotnet tool restore
dotnet ef database update --project backend/PharmacyPos.Api -- --environment Development
```

The first command installs the pinned migration tool. The second connects using
the project's development secrets and applies pending migrations. It creates the
five catalogue tables and EF's migration-history table. It does not add example
medicines, stock, or users. Do not pass a password on the command line.

The app does not automatically apply migrations on startup. Keep production migrations
as a deliberate deployment step using separate credentials and a tested backup.

## What this step does and does not provide

- Ingredients have separate strengths, so a combination tablet can hold 5 mg of
  one ingredient and 500 mg of another.
- Classification must be OTC or prescription. Manufacturer and dosage form references
  must exist. Strength must be positive, and blank names are rejected.
- Base stock unit currently supports tablets only. Other forms require additional
  validation and unit design before their entry workflow is released.
- A read-only Development-only `GET /api/medicines` preview now requires a signed-in
  staff account; see [staff setup](staff-sign-in.md). No catalogue write endpoints are
  exposed. Entry workflows, input normalization, at-least-one-ingredient validation, allowed strength
  units, duplicate detection, and audit events must accompany the later entry service.
- Packaging, batches, prices, approvals, costs, and checkout are later migrations.
- The database health endpoint still checks connectivity only; `Healthy` does not
  establish whether migrations have been applied.

## Read-only catalogue preview

Restart the backend after code changes and open http://localhost:5173. The page
lists active medicines with ingredients/strengths, manufacturer, form, and classification.
Search matches brand, ingredient, or manufacturer; responses contain at most 25
medicines per page. Next/Previous controls appear when needed. No costs, stock quantities,
or batch prices are exposed by this endpoint.

`GET /api/medicines?search=Jardimet&page=1` returns `items`, `page`, and `hasMore`.
Page defaults to 1; valid pages are 1–10000 and search is limited to 100 characters.
Blank search lists all active medicines. Literal wildcard characters are escaped.
An empty result returns HTTP 200; database failures return HTTP 503 with a generic
message. This route requires the Staff policy and remains absent outside Development.

The browser shows distinct loading, empty, no-match, and connection-error messages,
with Refresh to retry. It replaces old results when a new search/page begins.
The endpoint, empty/search responses, invalid-page rejection, browser empty/search
states, and backend-disconnection message were checked locally. Populated pagination
still needs fixture-based coverage before this preview is promoted beyond development.

Vite uses http://localhost:5167 by default. Developers may set `PHARMACY_API_URL`
when starting Vite to test a backend on another local port; it is a development
proxy setting, not a browser database credential.

## Verification for developers

```sh
dotnet build backend/PharmacyPos.Api --no-restore --disable-build-servers
dotnet ef migrations has-pending-model-changes --project backend/PharmacyPos.Api -- --environment Development
```

To test database constraints, apply the migration to a dedicated test database, then
execute `backend/tests/catalogue-constraints.sql` with `psql -X -v ON_ERROR_STOP=1`.
The fixture transaction rolls back. Tests check combination strengths, invalid
classification, zero strength, missing references, protected deletion, duplicate
ingredient order, and blank brands. Migration up/down should also be tested only
against a disposable database: rollback drops tables and their data.

No secrets or connection strings are embedded in the generated migration files.
