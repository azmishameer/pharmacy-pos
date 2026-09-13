# Pharmacy POS

A local-network pharmacy sales and inventory application, developed in small steps.

## Current milestone

The React welcome page calls the ASP.NET Core `/api/status` endpoint and displays
whether the backend responded. No database, staff login, or sales functionality
exists yet. This is a development starter, not a system ready for pharmacy use.

## Project folders

- `frontend`: React and TypeScript browser interface, built with Vite.
- `backend/PharmacyPos.Api`: C# API targeting .NET 10.

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

Open http://localhost:5173. The page should show **Backend connected**.
Vite forwards `/api` requests to http://localhost:5167 during development.
Keep both terminals running. Press Control+C to stop a server.
After editing backend code, stop and restart the backend to load the changes.

If the page cannot connect, check http://localhost:5167/api/status directly.
It should return `{"status":"ok"}`. A 404 response can mean an older backend
process is still running; restart it from this project.

These HTTP addresses are for development on this computer. Encrypted connections,
automatic service startup, and local backup/restore will be configured before deployment.

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
