# Architecture

## Goal

Trade/analyze US options on SPY. First milestone: a Next.js page that plots a 3D P&L surface for a
chosen (date, time, options combination), assuming Greeks frozen (constant IV + constant risk-free
rate), mirroring the Angular app in `Temp/OptionPricing3DSurface`.

## Components

```
┌──────────────┐   REST (JSON)   ┌────────────────┐   Dapper/Npgsql   ┌────────────┐
│ Next.js app  │ ───────────────▶│ C# WebAPI      │ ────────────────▶│ PostgreSQL │
│  (Client/)   │                 │  (Server/)     │                   │ (54333)    │
└──────────────┘                 └───────┬────────┘                   └────────────┘
                                         │ REST (JSON)
                                         ▼
                                 ┌────────────────┐
                                 │ Theta Terminal │  local server, port 25503 (v3)
                                 │ (ThetaData/)   │  launched with THETADATA_API_KEY
                                 └────────────────┘
```

- **Client (Next.js + Plotly)** calls the WebAPI. It never talks to ThetaData directly.
- **Server (C# WebAPI, net8.0)** talks to ThetaData (v3 REST) and Postgres. It computes the surface
  (Black-Scholes) so the client only renders.
- **Postgres** stores the option chain, spot quotes, option greeks, and the risk-free rate, so data
  can be collected once and reused.

## Data flow for the 3D surface

1. Client picks `symbol` (SPY), `snapshot` (date+time, NY timezone), and a set of option **legs**
   (right, strike, expiration, contracts).
2. Server, for each leg, fetches **implied vol** at the snapshot from
   `GET /option/history/greeks/first_order` (STANDARD subscription), and the **risk-free rate**
   (SOFR) from `GET /interest_rate/history/eod` (FREE).
3. Server builds a grid: X = spot prices (around snapshot spot ± N%), Y = time axis (snapshot → max
   leg expiry, market-hours steps). For each (spot, time) it prices each leg with Black-Scholes
   (frozen sigma = snapshot IV, frozen r = snapshot rate) and sums PnL / Delta / Gamma.
4. Server also returns the historical spot-price path **three times** — `spotLineValue`, `spotLineDelta`,
   `spotLineGamma` — so the orange line is computed with the matching Z for each surface (PnL for the
   value surface, delta for the delta surface, gamma for the gamma surface).
5. Client renders three Plotly 3D surfaces (Value, Delta, Gamma), each with its own spot line, with
   synced cameras.

### What the "Time (ET)" input means

`date` + `time` together define the **snapshot / as-of moment** (in `America/New_York`). At that
moment the server samples each leg's **implied vol** (the greeks row closest in time) and the **spot
price**, and freezes them for the whole surface. It is also where the surface's Y (time) axis **begins**
and where the orange realized-spot line starts. Everything to the right/future of the snapshot is the
"Greeks-frozen" projection; nothing is computed for times before the snapshot.

Consequently the snapshot must be a past trading moment that has market data. "Today" before market
open has no data (see `THETADATA-V3.md`), so the client defaults the date to the last completed
trading day.

## Server project layout (`Server/`)

```
Server/
  TestOptionStrategy.Server.csproj
  Program.cs
  appsettings.json
  Common/
    AppSettings.cs            # IConfiguration accessor (same pattern as BidAskLast)
    MarketClock.cs            # trading day / ms-of-day / NY timezone helpers
    GreeksCalculator.cs       # Black-Scholes price + all Greeks (port of reference)
  Domain/
    ThetaData/
      ThetaDataClient.cs      # v3 REST client (columnar JSON)
      ThetaDataModels.cs      # response DTOs (snake_case -> JsonPropertyName)
    Treasury/
      USTreasuryRate.cs       # 10Y treasury yield fetcher (reference impl)
    Data/
      Database.cs             # Npgsql connection factory (connection string from config)
      UnderlyingRepository.cs
      OptionContractRepository.cs
      SpotQuoteRepository.cs
      OptionGreeksRepository.cs
      RiskFreeRateRepository.cs
  Application/
    Services/
      MarketDataService.cs    # orchestrates ThetaData fetch + DB read/cache
      OptionSurfaceService.cs # builds the 3D surface grids
    WebApi/
      Controllers/
        MarketDataController.cs
        SurfaceController.cs
      DTOs/
        DTOs.cs
```

## API surface (v1)

- `GET /api/market/underlyings` → list of underlyings (from DB/ThetaData).
- `GET /api/market/expirations?symbol=SPY` → expirations.
- `GET /api/market/strikes?symbol=SPY&expiration=YYYY-MM-DD` → strikes.
- `GET /api/market/spot?symbol=SPY&date=YYYY-MM-DD&time=HH:mm` → spot (mid) at the snapshot moment
  (0 when no data, e.g. pre-market today). Used by the client's live "Spot @ snapshot" readout.
- `POST /api/surface` → compute value/delta/gamma surfaces + the three spot lines.

See `Dtos.cs` for the exact request/response shapes.

## Database

See [DATABASE.md](DATABASE.md).

## Client layout (`Client/`)

```
Client/
  app/
    layout.tsx          # root layout + metadata
    page.tsx            # main page: controls (date/time/expiration/legs) + 3 charts
    globals.css         # styles
  components/
    SurfacePanel.tsx    # Plotly 3D surface component (value/delta/gamma), synced cameras
  lib/
    api.ts              # fetch wrappers -> WebAPI
    types.ts            # request/response types mirroring server DTOs
  types/plotly.d.ts     # module declaration for plotly.js-dist-min
```

Notes on the UI (`page.tsx`):
- Each option leg is independent and carries **Long/Short**, **Call/Put**, its own **expiration**, a
  **strike**, and a positive **quantity**. The signed contract count (`+qty` long, `-qty` short) is
  computed client-side before `POST /api/surface`. This supports multi-expiry combinations
  (e.g. calendar spreads).
- Strikes are fetched **per expiration** and cached in `strikesByExpiration` (keyed by `YYYY-MM-DD`);
  a leg's strike list refreshes when its expiration changes.
- A live **"Spot @ snapshot"** readout fetches `GET /api/market/spot` (debounced 400 ms) whenever the
  date/time/symbol changes, so the user sees the underlying price at the chosen moment during setup.

Client is Next.js **14.2.35** (App Router) + `plotly.js-dist-min` (dynamically imported, client-side).
It calls the WebAPI **same-origin**: `next.config.mjs` rewrites `/api/*` → `http://localhost:5210/api/*`
(`API_ORIGIN` env override). This means the browser only ever talks to `:3000`, so a single VS Code
port-forward of `:3000` is enough (no need to forward `:5210`). `npm audit` flags the whole Next 14.x
line for advisories that are only relevant to public multi-tenant deployments (SSRF/DoS/image-optimizer);
acceptable for a local research tool.

