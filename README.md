# Pharmacy POS

A browser-based pharmacy sales and inventory application by **Shameer Azmi**, developed with AI assistance. Designed around Bangladeshi pharmacy workflows using React, TypeScript, ASP.NET Core (.NET 10), Entity Framework Core and PostgreSQL 18.

**Preview software.** Automated regression and browser checks have passed. Physical Windows installation, USB scanner and thermal-printer acceptance testing remain outstanding. The package is not a claim of production or regulatory certification.

## Try the demo

The isolated demo has **Try as admin** and **Try as operator** buttons. No application password is required. It contains fictional medicines, stock and barcode examples. Never enter real pharmacy or personal data.

With Docker and Compose installed, from this repository's root:

```sh
docker compose -f compose.demo.yaml up --build -d
```

Open **http://localhost:8080**. Initial dependency downloads may take several minutes. The demo runs only on this computer by default. [Demo setup, Windows alternative and reset instructions](docs/demo.md).

Normal installations still require staff passwords. Demo access cannot be enabled using a normal installation configuration or an existing unmarked database.

## Download and watch

- [Windows preview releases](https://github.com/azmishameer/pharmacy-pos/releases) — download the named Windows ZIP and checksum, not GitHub's source ZIP when installing on Windows.
- [Installation and update guide](docs/windows-deployment.md) — PostgreSQL must be installed separately. ZIP contains PowerShell scripts, not an MSI.
- [Portfolio assets and GPT Work handoff](portfolio/GPT-WORK-HANDOFF.md).
- [60-second walkthrough](portfolio/README.md) — edited walkthrough of real demo screens with fictional data.

## Main features

- Catalogue with manufacturer, dosage form, ingredients/strength and OTC/prescription classification.
- Batch-aware inventory, pack conversions, MRP verification and individual admin approval of operator stock submissions.
- Barcode lookup for piece, strip and box packs, with explicit batch selection.
- Cart, non-stacking admin offers, configurable charges and receipt rounding.
- Cash, Card, bKash and Nagad **recording**, including split payments. Money is processed externally; this app does not charge a card or contact wallet providers.
- Branded printable 80 mm receipts; 15-day whole-receipt-line returns; held returned stock requiring admin approval for resale.
- Expiry handling, low-stock alerts, disposal history, staff management and CSV exports.
- Admin sales/profit reporting and private purchase-cost history.
- Daily 02:00 Bangladesh-time backups with admin pause/resume.

## Documentation

[Payments](docs/payment-methods.md) · [Barcodes](docs/barcode-scanning.md) · [Receiving](docs/stock-receiving.md) · [Returns](docs/returns-and-refunds.md) · [Receipts](docs/receipt-printing.md) · [Staff](docs/staff-accounts.md) · [Backups](docs/backup-and-restore.md) · [Business rules](docs/business-rules.md)

## Developer setup

Install Node.js 24, .NET SDK 10 and PostgreSQL 18. Create a dedicated PostgreSQL database/user. Configure `ConnectionStrings:Pharmacy` and `Database:Password` with .NET User Secrets for `backend/PharmacyPos.Api`; never commit those values. See [staff sign-in](docs/staff-sign-in.md) and [catalogue setup](docs/catalogue-setup.md).

```sh
dotnet tool restore
dotnet ef database update --project backend/PharmacyPos.Api
dotnet run --project backend/PharmacyPos.Api -- --create-admin
dotnet run --project backend/PharmacyPos.Api --launch-profile http
```

In a second terminal:

```sh
cd frontend
npm ci
npm run dev
```

Open http://localhost:5173. Vite forwards API requests to port 5167. For a Windows package, install Python 3 and run `python3 scripts/build-release.py`. Generated packages, real records, secrets and backups stay out of Git.

## Deployment model

One application server and PostgreSQL serve browsers over the pharmacy LAN. Normal LAN use does not require internet after setup, but the server/network must remain available. This is not offline synchronization or a multi-tenant cloud service. See the Windows guide for future online hosting considerations.

## License

[MIT License](LICENSE). Copyright (c) 2026 Shameer Azmi. Use, modification and commercial redistribution are permitted under those terms; retain the copyright and license notice. Third-party dependencies retain their respective licenses.
