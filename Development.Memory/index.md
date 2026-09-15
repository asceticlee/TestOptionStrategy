# TestOptionStrategy — Development Memory Index

This directory is the handover brain for this project. Any AI agent (or human) resuming work here
MUST read this file first, then follow the pointers below.

## What this project is

Experiments on trading US options on **SPY**. Data provider is **ThetaData** (v3 REST API via the
local Theta Terminal). Backend is a **C# WebAPI** (`Server/`), frontend is **Next.js** (`Client/`),
database is **PostgreSQL** (`DBSchema/`).

## First task (current focus)

A Next.js page where the user picks a **date + time** and an **options combination**, and the app
plots a **3D P&L surface** over (spot price × time), assuming Greeks/IV are frozen — replicating the
Angular reference in `Temp/OptionPricing3DSurface`.

## Documents

| File | Purpose |
|------|---------|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Overall architecture, tech stack, data flow, directory map |
| [CODING-STANDARDS.md](CODING-STANDARDS.md) | Conventions the owner requires (no `var`, Dapper, Serilog, etc.) |
| [THETADATA-V3.md](THETADATA-V3.md) | Everything learned about the ThetaData v3 REST API and the local terminal |
| [GREEKS-AND-INTEREST-RATE.md](GREEKS-AND-INTEREST-RATE.md) | How Greeks are computed, the interest-rate source, formula notes |
| [DATABASE.md](DATABASE.md) | Schema, connection details, Dapper conventions |

## Reference projects (read-only, not part of this repo)

- `../BidAskLast` — the owner's previous C# + Angular SPY options project. Useful for:
  - `Server/Common/ToolkitExtension/GreeksCalculator.cs` — Greek formulas (server side).
  - `Server/Tiers/Domain/ThetaData/ThetaDataBase.cs` — old **v2** ThetaData client (superseded by v3).
  - `Server/Tiers/Domain/GenericFetcher/USTreasuryRate.cs` — 10Y treasury rate fetcher.
- `Temp/OptionPricing3DSurface` — the owner's Angular 3D surface app (Black-Scholes + Plotly).
  - `src/app/main/option.service.ts` — BS pricing + Greeks (TypeScript, correct formulas).
  - `src/app/main/main.component.ts` — 3D surface generation logic.

## Key facts (verified)

- Theta Terminal v3 launched with: `THETADATA_API_KEY="<api.key content>" java -jar ThetaTerminalv3.jar`
  (see `THETADATA-V3.md`). REST base URL: `http://127.0.0.1:25503/v3`.
- Subscription is **STANDARD** for Stock & Options; **Rate is FREE**. `x-min-subscription: professional`
  endpoints (e.g. `/option/history/greeks/all`, second/third-order greeks) are **NOT** available.
  First-order greeks (`/option/history/greeks/first_order`) and implied vol ARE available.
- SPY spot price was ~762–774 in early Sep 2026.

## Git / repository

- A root `.gitignore` is in place. It excludes: `Temp/`, build artifacts (`**/bin`, `**/obj`,
  `node_modules`, `.next`), logs, OS cruft, and the ThetaData generated/downloaded files
  (`ThetaTerminalv3.jar`, `ThetaData/lib/`, `ThetaData/config.toml`).
- **SECRETS — never commit:** `ThetaData/api.key` (the API key) and `ThetaData/creds.txt` are ignored.
  Anyone cloning the repo must supply their own key via `THETADATA_API_KEY`.
- `ThetaData/openapiv3.yaml` is tracked (source of truth for the v3 API).

## How to run

1. Start the Theta Terminal (see `THETADATA-V3.md`):
   `cd ThetaData && THETADATA_API_KEY="$(cat api.key)" java -jar ThetaTerminalv3.jar`
2. Start the server (port 5210):
   `Deploy/start-server.sh`
3. Start the client (port 3000):
   `Deploy/start-web.sh`   (add `--build` to rebuild the Next.js bundle first)

Stop with `Deploy/stop-server.sh` and `Deploy/stop-web.sh` (idempotent; logs in `Deploy/logs/`,
PID files in `Deploy/*.pid`).

Open `http://localhost:3000`. The header shows the underlying, an expiration strip (month tabs +
date chips), and a **strike ruler** — add legs, then drag each pill on the ruler to move its strike
(above axis = long, below = short; green = call, red = put). The stats row shows net debit, max
loss/profit and breakevens. Click "Plot surface" to render the 3D Value / Delta / Gamma surfaces
with the realized spot path traced in orange.

> When working on a remote machine over VS Code port-forwarding, only `:3000` needs forwarding:
> the Next.js server proxies `/api/*` to the C# server on `:5210` (see `next.config.mjs`).

