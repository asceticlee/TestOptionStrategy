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

## Deploy (`Deploy/`)

Shell scripts to run/stop the two services (idempotent; PID files in `Deploy/*.pid`, logs in
`Deploy/logs/`):
- `start-server.sh` / `stop-server.sh` — build + run / stop the C# server (port 5210).
- `start-web.sh` / `stop-web.sh` — run / stop the Next.js app (port 3000; pass `--build` to rebuild).

See `index.md` "How to run". (The Theta Terminal is launched separately — see `THETADATA-V3.md`.)

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
- `GET /api/market/quote-summary?symbol&date&time` → `{ spot, previousClose }` for the underlying row
  (day-over-day change).
- `POST /api/surface` → compute the **theoretical** value/delta/gamma surfaces + the three spot lines.
- `POST /api/surface/real` → compute the **real** surfaces: for each leg it fetches the historical
  first-order greeks series over `[tradeDate, last data day]` at the "Time step (min)" interval, then
  for each actual timestamp re-prices the leg over the spot axis using the **observed IV at that
  timestamp** (not the frozen snapshot IV). Returns the same `SurfaceResponse` shape. This reveals the
  real, non-smooth surface driven by how IV actually moved through time.
- `POST /api/surface/stats` → compute strategy stats: `netDebit`, `maxProfit`, `maxLoss`, `breakevens`,
  and per-leg `entryPrice`, `impliedVol`, `bid`, `ask`, `delta`, `theta`, `vega`, `gamma` (gamma is
  computed server-side via Black-Scholes; the rest come from ThetaData first-order greeks).
- `POST /api/surface/leg-greeks` → greeks for a **single** leg (`{symbol, snapshotDate, snapshotTime,
  leg}`) — used by `LegConfigPanel` for one-to-one per-leg greeks refresh.

See `Dtos.cs` for the exact request/response shapes.

## Database

See [DATABASE.md](DATABASE.md).

## Client layout (`Client/`)

```
Client/
  app/
    layout.tsx          # root layout + metadata
    page.tsx            # main page: OptionStrat-style header + ruler + stats + 3 charts
    globals.css         # dark-navy theme + component styles
  components/
    DateStrip.tsx       # horizontal drag-scrollable snapshot-date strip (month header + clickable dates)
    ExpirationStrip.tsx # "EXPIRATION: Nd" + month tabs + per-expiration date chips
    StrikeRuler.tsx     # strike ruler with draggable leg pills (see conventions below)
    StatsStrip.tsx      # 5-cell stats row (net debit/credit, max loss, max profit, pop, breakevens)
    SurfacePanel.tsx    # Plotly 3D surface component (value/delta/gamma), synced cameras
  lib/
    api.ts              # fetch wrappers -> WebAPI
    types.ts            # shared types (Leg, request/response DTOs)
  types/plotly.d.ts     # module declaration for plotly.js-dist-min
```

## UI model (OptionStrat-style header, top → bottom)

The page is a **three-tab view**: **Trade Setup** (all controls below), **3D Theoretical** (the frozen-IV
surface), and **3D Real** (the observed-IV surface).

- **3D Theoretical**: "Plot surface" computes the surface using each leg's **IV frozen at the snapshot**
  and switches to this tab (Re-plot available). Smooth, because IV is assumed constant over time.
- **3D Real**: "Plot Real Surface" fetches the **actual** first-order greeks (IV) series for each leg at
  the "Time step (min)" interval and re-prices each time-slice with the IV **actually observed** at that
  time. This shows the real, non-smooth surface of how the option's value actually evolved (spot +
  IV), rather than the theoretical one. Per-leg greeks fetches are **parallelised** (`Task.WhenAll`);
  a single-leg or 4-leg request completes in ~1 s. The legs are marked via **Black-Scholes at a
  smile-regularized IV** — the IV is fit from the surrounding strikes (quadratic smile in log-moneyness)
  and clamped to the leg's bid/ask — see "3D Real choppiness" / "Smile-regularized IV" in
  GREEKS-AND-INTEREST-RATE.md.

Trade Setup, top → bottom:

1. **Title row** — auto-named strategy (e.g. "Long Call", "Iron Condor") + "?" icon; action pills:
   `Positions (N)`, `Plot surface` (primary blue pill).
2. **Underlying row** — dark ticker chip (editable symbol), large last price, red/green day-over-day
   change (from `quote-summary`), "Snapshot" badge.
3. **Trade Date (DateStrip)** — the drag-scrollable as-of / snapshot-date selector, with a
   **"Trade Date"** label and a **Time (ET)** input alongside it (date + time = the strategy's entry
   moment). This is our "as-of time" concept, which OptionStrat lacks.
4. **StrikeRuler (option legs panel)** — horizontal strike axis with tick marks + labels and a dashed
   spot marker (**symbol + spot price**, e.g. `SPY 764.10`). One pill per leg positioned by strike:
   - **Above the axis = SHORT, below the axis = LONG.**
   - **Colour by right: green = call (`#51B349`), red = put (`#B2242F`).**
   - Pill text is `{strike}{C|P}` (e.g. `750C`).
   - **Drag a pill left/right to change its strike** (snaps to the available strikes for that leg's
     expiration); **drag up/down across the axis to flip its side** (long ↔ short); **double-click a pill
     to swap call ↔ put**. Grabbing/dragging a pill does **not** scroll the page (scrolling to the
     config panel was removed — it disrupted the drag gesture).
   - Each pill has an **×** (right side) to remove that leg directly from the ruler.
   - Each pill has a **small arrow** pointing to its strike on the axis: short pills (above) point down,
     long pills (below) point up.
   - **The axis is a fixed, spot-centred window** `[spot − W, spot + W]` with `W = spot ×
     SpotRangePercent/100`. The scale never changes when dragging a leg — only the pill moves — and the
     underlying price always sits at the centre of the ruler. The window shares the same X range as the
     surface (so widen "Spot range %" to reach strikes further from spot). Pills outside the window are
     clamped to the edge.
   - Pills are placed in **non-overlapping lanes** (greedy horizontal collision-avoidance). The
     separation is **width-aware** — `60px ÷ track width` (measured with a `ResizeObserver`), using
     half-width edges so the minimum gap is exactly one pill width; pills only stack into a new lane
     when they actually touch.
   - Pill text shows the quantity when > 1, e.g. `2× 762C` (single contracts render just `762C`).
5. **Add Leg +** button — sits under the StrikeRuler, before the config panels (renamed from "Add+";
   moved out of the title row). No legs exist until it's clicked.
6. **Per-leg config panels** — one panel per leg (rendered by `LegConfigPanel`, keyed by a stable leg
   `id`), directly under the "Add Leg" button. Each panel is **collapsible** (click its header to
   toggle): expanded shows its own **expiration ruler** (`ExpirationStrip`) + Side / Call-Put / Strike /
   Qty controls + a **greeks row** (Bid, Ask, Delta, Gamma, Theta, Vega, IV); collapsed shows a single
   compact summary row (`Long 762C · 2 · Exp 2026-09-18 · Δ … · IV … · bid/ask`). Each panel fetches its
   **own** greeks via `POST /api/surface/leg-greeks` (200 ms debounce, `…`/`Recalculating…` while
   fetching) — so dragging one leg refreshes only that leg, **one-to-one, without touching the others**.
   Other details:
   - Header badge like `Long 420C · 1` (side + `{strike}{C|P}` + `· {qty}`) so the contract count is
     never confused with the strike.
   - A **new** leg defaults to the available strike **nearest the snapshot spot** (ATM), not the lowest.
7. **StatsStrip** — NET DEBIT/CREDIT, MAX LOSS, MAX PROFIT, CHANCE OF PROFIT (`—`, POP not computed),
   BREAKEVENS. Live-computed via `POST /api/surface/stats` (250 ms debounce) from the current legs.
8. **Analysis params (demoted)** — a slim toolbar for Time step, Spot range (%), Spot samples (Time (ET)
   lives in the Trade Date row).
9. **Charts** — the 3D P&L/delta/gamma surfaces, on a **dark background** matching the theme
   (`paper_bgcolor` `#04041F`, light axis ticks/grid). Shown in the **3D Theoretical** / **3D Real**
   tabs (Re-plot buttons; no separate "Back" button — the tabs handle navigation). The surface **Y axis
   is categorical** (numeric indices with timestamp tick labels), so non-trading hours (nights/weekends)
   are skipped and the surface/orange line don't stretch across market-closed gaps. Time-to-expiry is
   **calendar time** (the standard `(expiry − now).TotalDays / 365`), matching how ThetaData calibrates its
   implied vol, so option prices/stats are correct (a long-far/short-near calendar spread is a debit with
   negative P&L far from the strike). For the **3D Real** surface the observed IV series is filtered to
   valid quotes (bid>0 & ask>0) and **bridged overnight** (the first valid quote of each day carries the
   previous day's close IV), which removes the garbage/empty open-quote IV that caused a spurious chasm.
    The hover tooltip shows the real timestamp (surface uses a **transposed 2D `text`** array +
    `hovertemplate` `%{text}`; the orange line uses 1D `text`). Gotcha: Plotly's 3D hover builds the
    point via `selection.index = [xIndex, yIndex]` (`surface/convert.js handlePick`) and
    `fx/helpers.js appendArrayPointValue` then reads every 2D per-point array (`text`, `customdata`,
    `hovertext`) as `val[xIndex][yIndex]` — i.e. **transposed** relative to `z`, which is laid out
    `z[yIndex][xIndex]`. So the array must be supplied transposed: `text[x][y]`, built as
    `data.x.map(() => yLabels)` (so `text[xIndex][yIndex] = yLabels[yIndex]`). Supplying it like `z`
    gives transposed/wrong times and, past a bound, a literal `%{text}`.
    The Y-axis tick labels are dense (~50, `MM-DD HH:mm`, 8px) so they align with the orange line's
    per-time-step spot path. `scene.aspectmode` is forced to **`"cube"`** — the default `"auto"` flips
    between cube and data-proportional depending on whether `max(range)/min(range)` exceeds 4, so the
    theoretical (z-range 469) rendered cube while the real (z-range 261) rendered narrow-x rectangular.

Scroll behaviour: horizontal scroll containers (`DateStrip`, expiration chips/month tabs, strike ruler,
stats strip) hide their scrollbars (`scrollbar-width: none` + `::-webkit-scrollbar { display: none }`)
while keeping drag/wheel scrolling, so no scrollbar pops in and disrupts the layout.

Theme: dark navy (`#04041F` bg, `#11112A`/`#1A1A33` panels, `#E8EAF2` text, `#4B9DD9` accent,
green `#51B349`, red `#B2242F`) — palette extracted from the provided OptionStrat screenshot via PIL.

Data notes:
- Each option leg is independent (side, call/put, its own expiration, strike, positive qty); signed
  contracts are computed client-side. Supports multi-expiry combinations.
- Strikes are fetched per expiration and cached in `strikesByExpiration` (keyed by `YYYY-MM-DD`).

Client is Next.js **14.2.35** (App Router) + `plotly.js-dist-min` (dynamically imported, client-side).
It calls the WebAPI **same-origin**: `next.config.mjs` rewrites `/api/*` → `http://localhost:5210/api/*`
(`API_ORIGIN` env override). This means the browser only ever talks to `:3000`, so a single VS Code
port-forward of `:3000` is enough (no need to forward `:5210`). `npm audit` flags the whole Next 14.x
line for advisories that are only relevant to public multi-tenant deployments (SSRF/DoS/image-optimizer);
acceptable for a local research tool.

