# Password-free demo

This is a separate fictional pharmacy, not an anonymous login to your real installation. The demo offers **Try as admin** and **Try as operator**. No application password is needed. No real money moves. Never enter personal or real pharmacy data.

## Cross-platform demo with Docker

Install Docker with Compose, download/extract the repository source (or clone it), open a terminal in its root, then run:

```
docker compose -f compose.demo.yaml up --build -d
```

Open http://localhost:8080. The first build downloads dependencies and may take several minutes. The database initializes automatically with four fictional medicines and barcode examples `DEMO-BOX-1` and `DEMO-BOX-2`. Near-expiry and expired batches are included. Sample dates are relative to the first startup. Stock and sales changes stay in the demo volume across restarts.

Stop with `docker compose -f compose.demo.yaml down`. To permanently clear ONLY this demo's data, use `docker compose -f compose.demo.yaml down -v`, then start it again. Do not use real installation volumes or credentials. The published port is bound to this computer's loopback interface.

## Windows ZIP alternative

PostgreSQL 18 must be installed. In SQL Shell as your PostgreSQL administrator, create a separate demo owner and database:

```sql
CREATE ROLE pharmacy_demo LOGIN;
\password pharmacy_demo
CREATE DATABASE pharmacy_pos_demo OWNER pharmacy_demo;
```

From the extracted package, run `./Start-Demo.ps1` in PowerShell (use `Set-ExecutionPolicy -Scope Process Bypass` if necessary). It asks for the DEMO DATABASE password only. App visitors select a role without a password. Open http://localhost:8080 and leave its terminal running. This starts no Windows service and does not change your real installation.

## Separation and limitations

Demo startup requires environment `Demo`, database `pharmacy_pos_demo`, username `pharmacy_demo`, and no `PHARMACY_CONFIG` installation file. It refuses an existing database without its demo marker. The password-free route does not exist in Development or Production. Demo cookies have different names. Automatic backup jobs and staff-account mutations are disabled in the demo; real deployments retain normal protections. Do not expose demo ports on the internet or use demo settings with real records.

The Docker launch recipe and Windows launcher are provided for convenience; OS/container integration testing is reported separately from backend/browser tests. Physical Windows and printer/scanner acceptance checks remain necessary for live deployments.
