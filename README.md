# Pharmacy POS

A local-network pharmacy sales and inventory application, developed in small steps.

## Current milestone

The React catalogue page reads active medicine details from the authenticated
`/api/medicines` endpoint, with search and pagination. Empty tables show "No medicines yet".
Local staff sign-in and server-enforced catalogue access are implemented; apply the
new account migration and create your first admin using the instructions below.
Medicine entry now supports manufacturer and dosage-form dropdowns, optional first-batch
dates, and operator submissions for individual admin approval. Admin entries are
automatically approved. See the medicine-entry guide and `CatalogueBatchesAndReview`
migration below. Stock receiving, individual review, explicit MRP verification, expiry handling and
admin disposal with three-month CSV history are implemented in the development preview.
See [stock receiving setup](docs/stock-receiving.md). The [operator sales counter](docs/sales-counter.md) now supports a cart with admin-configured percentage and fixed charges. Apply the `CartCharges`
migration and restart the backend to enable charge settings. Admin discount offers
require the additional `DiscountOffers` migration. [Cash checkout](docs/cash-checkout.md)
adds completed sales, cash/change, stock deduction and saved printable receipts; apply
`CashCheckout` to enable it. [Returns and refunds](docs/returns-and-refunds.md) support complete receipt items,
tracked cash refunds and separate held stock; apply `ReturnsAndRefunds`. Non-cash
payments and catalogue editing remain pending. [Staff account management](docs/staff-accounts.md)
adds admin controls for operator creation, password resets and access status; apply
`StaffAccountManagement` and restart the backend. The [admin sales report](docs/sales-reports.md)
provides date-filtered sales, refunds, cash and staff totals with CSV export; restart
the backend to enable it (no additional migration). [Admin purchase costs](docs/purchase-costs.md)
record delivery totals and retain correction history; apply `PurchaseCostHistory`.
[Charge profit classification](docs/charge-profit-classification.md) adds the admin
exclusion checkbox and review of old charges; apply `ChargeProfitClassification`.
[Admin profit reporting](docs/profit-reports.md) now uses corrected costs, excludes
classified pass-through charges and handles refunds and approved returns. Restart the
backend to enable it; no additional migration is required. The database
connectivity check remains available. The user has applied `InitialCatalogue` to the
development database; setup instructions below cover a fresh checkout.
This is a development starter, not a system ready for pharmacy use.

[Manual database backup and restore verification](docs/backup-and-restore.md) now
creates private archives with checksums and snapshot table counts. Backups are not
committed to Git. Apply `AutomaticDailyBackups` to enable daily 2 AM Bangladesh-time
backups with admin pause/resume and run history. Keep the backend running.

[Stock alerts](docs/stock-alerts.md) show low stock and 90-day near-expiry batches to
admins and operators. Admins set per-medicine thresholds; apply `StockAlertThresholds`.

## Project folders

- `frontend`: React and TypeScript browser interface, built with Vite.
- `backend/PharmacyPos.Api`: C# API targeting .NET 10.

## Project planning

- [Agreed business rules](docs/business-rules.md)
- [Medicine and stock database plan](docs/database-plan.md) — proposed records,
  relationships, approval workflow, and implementation steps.
- [First catalogue setup](docs/catalogue-setup.md) — what is implemented and how
  to apply the first migration. No user database changes were made during its preparation.
- [Local staff sign-in](docs/staff-sign-in.md) — account migration, first-admin setup,
  sign-in, security behavior, and test instructions.
- [Add medicine](docs/medicine-entry.md) — admin catalogue creation and its migration.

## Run locally

Install Node.js 24 LTS and the .NET 10 SDK. Open two terminals in the project folder.
Initial dependency installation requires internet access.

In the first terminal:

```sh
cd frontend
npm ci
npm run dev
```

In the second terminal:

```sh
dotnet run --project backend/PharmacyPos.Api --launch-profile http
```

Open http://localhost:5173. After the account migration and first-admin setup, sign in
to see **Medicine catalogue** and, when empty, **No medicines yet**.
Vite forwards `/api` requests to http://localhost:5167 during development.
Keep both terminals running. Press Control+C to stop a server.
After editing backend code, stop and restart the backend to load the changes.

If the page cannot connect, check http://localhost:5167/api/status directly.
It should return `{"status":"ok"}`. A 404 response can mean an older backend
process is still running; restart it from this project.

These HTTP addresses are for development on this computer. The Windows package provides HTTPS, automatic service startup and separate backup storage; see the deployment guide below.

## Development database connection

The backend uses PostgreSQL through Npgsql. Configure these .NET User Secrets
for `backend/PharmacyPos.Api` on each development computer:

- `ConnectionStrings:Pharmacy`: host, port, database, and username.
- `Database:Password`: the database account password, stored separately.

The current development database is `pharmacy_pos_dev` on localhost port 5432,
with account `pharmacy_app`. User Secrets stay outside the repository but are
not encrypted. Never paste their contents into logs, chat, or commits.

After configuring the secrets, restart the backend and open
http://localhost:5167/api/status/database. `Healthy` (HTTP 200) means a read-only
`SELECT 1` query succeeded. `Unhealthy` (HTTP 503) means configuration is missing
or the connection/query failed. Check that PostgreSQL is running and that the
saved account, database, and password are correct. This endpoint is enabled only
in Development and does not create tables or verify the application schema.
The existing `/api/status` endpoint checks API availability independently.

## Verify changes

From the project folder:

```sh
dotnet build backend/PharmacyPos.Api
cd frontend
npm run build
npm run lint
```

## Planned architecture

Windows browsers will connect to a central application server inside the pharmacy.
Only the backend will access the planned PostgreSQL database. Normal operation
will use the shop network without internet; a server or local-network outage will
prevent sales. Database installation and business rules are still to be finalized.

## Repository hygiene

Keep source code and the npm lockfile in Git. Generated builds and downloaded
packages are ignored. Never commit passwords, pharmacy records, database backups,
or private certificates. GitHub stores code; it is not the pharmacy database or
its backup service.

Receipt branding, 80 mm printing and the enforced 15-day return policy are documented in [receipt printing](docs/receipt-printing.md).

## Client deployment

Build the Windows x64 installer package with `python3 scripts/build-release.py`. See [Windows deployment](docs/windows-deployment.md) for installation, HTTPS, updates, backups and the future move to online hosting. Business endpoints now work in Production mode with the same role and CSRF checks.

Cash, Card, bKash and Nagad recording, split payments, refund destinations and payment-method reporting are covered in [payment methods](docs/payment-methods.md).
