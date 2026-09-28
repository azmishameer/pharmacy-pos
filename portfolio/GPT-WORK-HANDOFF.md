# Pharmacy POS — portfolio handoff for GPT Work

Prepared for Shameer Azmi on 28 September 2026.

## Task

Create a portfolio project page for **Pharmacy POS**, using the facts and links below. Include a project overview, technology stack, key workflows, a short demo section, source code link, Windows download and installation guide. Use beginner-friendly wording. Do not invent customer deployments, business results, certifications, live hosting or testing claims.

## Project copy

**Title:** Pharmacy POS — Sales & Inventory Management

**Short description:** A browser-based pharmacy sales and inventory application designed around Bangladeshi pharmacy workflows. It supports batch and expiry tracking, stock approval, pack-based sales, configurable discounts and charges, split payments, returns, barcode lookup and reporting.

**Case-study summary:** Built to connect the sales counter with controlled inventory workflows. Operators can enter deliveries for admin review, sell medicines as individual units, strips or boxes, and record Cash, Card, bKash and Nagad payments. Admins manage pricing verification, offers, charges, purchase costs, staff access and reporting. Returned medicines are held separately until approved for resale, and expired stock is excluded from sales.

**Creator:** Shameer Azmi. If discussing the development process, describe it as an AI-assisted project developed with Codex. Do not invent employment, client names or usage metrics.

**Status:** Working software preview with a downloadable Windows x64 deployment package. Automated regression checks and browser workflow checks have passed. Physical Windows installation, scanner hardware and thermal-printer acceptance tests remain outstanding. Do not label it production-ready or certified for clinical use.

## Technology stack and architecture

- Frontend: React, TypeScript, Vite, CSS.
- Backend: C#, ASP.NET Core / .NET 10.
- Database: PostgreSQL 18, Entity Framework Core, Npgsql.
- Authentication: ASP.NET Core Identity, staff roles, server-side authorization and CSRF protection.
- Windows deployment: self-contained backend, built frontend, PowerShell installation/update scripts, Windows service and HTTPS.
- One application server and PostgreSQL database serve browser-based cashier clients on the pharmacy LAN. Internet is not needed for normal LAN operation after installation; the server and local network must remain available.
- This is not an offline-sync application. A future online deployment requires additional hosting configuration and a migration plan.

## Features to highlight

1. Medicine catalogue with ingredient/strength, manufacturer, dosage form and OTC/prescription classification.
2. Batch-aware receiving, manufacturing/expiry dates and tablet/strip/box/carton quantity conversion.
3. Individual approval of operator stock entries; admin entries approved automatically; MRP verification.
4. Sales cart, admin-configured offers, fixed/percentage charges, rounding and receipts.
5. Cash, Card, bKash and Nagad payment recording, including split payments and transaction references.
6. Full receipt-line returns within 15 days, refund tracking, receipt confirmation and separate held-return stock.
7. Barcode lookup for sales and receiving, piece/strip/box mapping and admin barcode registration/retirement. Standard keyboard/HID scanner with Enter suffix; batch confirmation remains explicit.
8. Expired/near-expiry and low-stock workflows; admin disposal tracking.
9. Staff management, sales/profit reports, admin-only purchase costs and CSV exports.
10. Daily 02:00 Bangladesh-time backups, admin pause/resume, backup history and manual backup tools.
11. Receipt branding and 80 mm receipt layout.

Payment integrations are **manual recording of externally completed transactions**, not automatic bank/wallet processing. Do not claim the app charges cards or automatically transfers/refunds bKash/Nagad funds.

## Source and downloads

Repository: https://github.com/azmishameer/pharmacy-pos

MIT license, copyright 2026 Shameer Azmi: https://github.com/azmishameer/pharmacy-pos/blob/main/LICENSE

Current portfolio release: https://github.com/azmishameer/pharmacy-pos/releases/tag/portfolio-demo-20260928

Windows ZIP: https://github.com/azmishameer/pharmacy-pos/releases/download/portfolio-demo-20260928/pharmacy-pos-win-x64-20260928-164015.zip

Checksum: https://github.com/azmishameer/pharmacy-pos/releases/download/portfolio-demo-20260928/pharmacy-pos-win-x64-20260928-164015.zip.sha256

60-second MP4: https://github.com/azmishameer/pharmacy-pos/releases/download/portfolio-demo-20260928/pharmacy-pos-demo-60s.mp4

Poster image: https://raw.githubusercontent.com/azmishameer/pharmacy-pos/main/portfolio/demo-poster.jpg

Demo setup: https://github.com/azmishameer/pharmacy-pos/blob/main/docs/demo.md

This Windows ZIP includes Start-Demo.ps1, DEMO.md and the MIT license. It is not a one-click MSI. The demo has password-free admin/operator buttons, fictional records and its own database. Windows users still need PostgreSQL and a dedicated demo database; Docker users can start both services from compose.demo.yaml. No publicly hosted interactive demo exists. Do not label localhost as a visitor-accessible live URL.

## Reading links for GPT Work

The README and guides describe the current source and deployment requirements.

Installation, requirements, update, recovery and future hosting guide:
https://github.com/azmishameer/pharmacy-pos/blob/main/docs/windows-deployment.md

Payment methods:
https://github.com/azmishameer/pharmacy-pos/blob/main/docs/payment-methods.md

Barcode scanning:
https://github.com/azmishameer/pharmacy-pos/blob/main/docs/barcode-scanning.md

Receipts:
https://github.com/azmishameer/pharmacy-pos/blob/main/docs/receipt-printing.md

Returns/refunds:
https://github.com/azmishameer/pharmacy-pos/blob/main/docs/returns-and-refunds.md

Backup and restore:
https://github.com/azmishameer/pharmacy-pos/blob/main/docs/backup-and-restore.md

For plain text, change a GitHub /blob/main/ URL to:
https://raw.githubusercontent.com/azmishameer/pharmacy-pos/main/docs/windows-deployment.md

If GitHub access is denied, use this attached document and request access or copies of the relevant guides. Do not fabricate their contents.

## Beginner download/setup summary

1. Obtain the Windows x64 ZIP and accompanying checksum. Verify SHA-256 and extract it.
2. Use a supported Windows x64 server PC on a private local network. Install PostgreSQL 18 separately, including command-line tools.
3. Follow the installation guide to create a dedicated database and database account. Choose fresh credentials.
4. In administrator PowerShell, open the extracted folder and run the documented Install.ps1 command with the actual server computer name.
5. The script requests the database password, applies migrations, creates the first admin, configures HTTPS and starts the service.
6. Open the server's HTTPS address. Configure receipt branding, staff accounts, medicines and stock.
7. Follow Trust-Server.ps1 instructions for additional cashier PCs. The server must stay on.

The package contains no pharmacy records or preconfigured staff credentials. Node.js, Git and the .NET SDK are not needed by package users. Source contributors need Node.js 24, .NET SDK 10 and Python 3 for the release builder, plus PostgreSQL for running the application. The full guide is authoritative; do not replace it with a misleading “download and double-click” claim.

## Demo video

The release includes an exactly 60-second, silent MP4 assembled from real application screenshots with captions. It is an **edited walkthrough**, not a continuous live screen recording. All captured medicines, stock and payments are fictional. It covers demo role entry, catalogue, barcode lookup, cart pricing, split payments, receipts, returns, expiry alerts, sales reports and operator receiving.

Embed it using a standard HTML video element with controls, the poster URL above, and preload="metadata". Host the downloaded MP4 with the portfolio if the site builder cannot stream GitHub release downloads directly. Do not invent audio narration or automatic payment-provider integration.

## Publication and testing notes

The owner approved MIT licensing and public repository publication. No private pharmacy data or developer credentials belong in the demo package. Password-free routes exist only in the isolated Demo environment, not normal installations.

Backend regression checks, demo role/CSRF checks and browser workflows passed. The Windows package was cross-built and all file hashes verified. Docker launch and Windows-specific installation/service/firewall/certificate behavior have not been exercised on this Mac; physical scanner/printer testing remains outstanding. Keep the download labeled preview.

## Suggested portfolio calls to action

- View source — repository URL.
- Download Windows preview — named release page.
- Installation guide — windows-deployment.md.
- Watch demo — the captioned MP4 above; label it an edited walkthrough.

Do not expose developer credentials or include the private development database as sample data. Do not publish local filesystem paths or localhost addresses as visitor links.
