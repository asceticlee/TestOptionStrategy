# Data Loading (backfill / update)

There was no data-loading command until now: the app only cached ThetaData responses **lazily**
(as you plotted surfaces / viewed stats). This document covers the CLI loader that was added to
bulk-load and refresh the database.

## Commands

Run from the repo root (both build the server first, then run the loader in console mode — the web
server is NOT started):

```sh
# Bring the DB up to date with the latest SPY spot + option greeks + SOFR rate.
Deploy/update-data.sh [options]

# Load historical data for a date range (whole option chain).
Deploy/backfill.sh --from 2023-01-01 [--to 2026-09-21] [options]
```

### Options (both commands)

| Option | Default | Meaning |
|--------|---------|---------|
| `--symbol` | `SPY` | Underlying symbol. |
| `--interval` | `5m` | ThetaData interval string for spot + option greeks (`1m`, `5m`, `15m`, `30m`, `1h`, …). |
| `--strike-range` | `0` (all) | If `> 0`, fetch only `N` strikes above + `N` below ATM (`2N+1` strikes). Cuts volume roughly in half for `N≈50`. |
| `--lead-days` | `60` | How far beyond a month's end to include expirations (see below). |
| `--from` | required for backfill | Start date (inclusive). |
| `--to` | last completed trading day | End date (inclusive). Defaults to the last completed trading day: after 16:00 ET this is the current day, otherwise the previous trading day (weekends skipped). ThetaData cannot serve today before market open. |

`update-data.sh` computes `--from` automatically as the latest timestamp already in
`option_greeks`/`spot_quote` minus 2 days of overlap (idempotent), so it only fetches the gap.
It assumes prior data is contiguous — use `--backfill --from X --to Y` to fill an arbitrary hole.

## What it writes

- `underlying` — upserts the symbol.
- `spot_quote` — intraday NBBO at `--interval`.
- `option_greeks` — first-order greeks (delta/theta/vega/rho, `implied_vol`, bid/ask,
  `underlying_price`) for every strike and both rights, at `--interval`. `implied_vol` is **not**
  ThetaData's value: the loader inverts it from the bid/ask **mid** (`GreeksCalculator.
  CalculateMidImpliedVolatility`) and then applies the **spread-outlier filter**
  (`MarketDataService.ApplySpreadOutlierFilter`, per contract + ET day) — see
  GREEKS-AND-INTEREST-RATE.md.
- `risk_free_rate` — daily SOFR (`/interest_rate/history/eod`, free tier).

`option_contract` (the static chain table) is **not** populated — the app never reads it.

## How it works

- `/option/history/greeks/first_order` is called **without** `strike`/`right`, which returns **all
  strikes and both rights for a single expiration** in one response (verified: ~198 strikes for SPY).
- Iteration is by calendar **month**, and within each month by expiration and then **week-long
  chunks** (7 days). The month loop plus weekly chunks keeps each request under ThetaData's
  1-month multi-day limit and the ~500k-row pagination threshold.
- `--lead-days` (default 60) bounds how far past a month's end expirations are considered "active"
  in that month. It covers dailies/weeklies/monthlies/quarterlies. Long-dated LEAPS listed more
  than `--lead-days` ahead are NOT backfilled for their early life — raise `--lead-days` (at the
  cost of more empty requests) if you need them.
- Inserts are idempotent and **resumable** — re-running continues where it left off. `spot_quote`
  uses `ON CONFLICT … DO NOTHING`; `risk_free_rate` upserts; `option_greeks` uses
  `ON CONFLICT … DO UPDATE SET implied_vol = excluded.implied_vol` so a re-run refreshes the
  archived IV with the (now filtered) value.
- Option-greeks chunks are fetched **4-way parallel** (`Parallel.ForEachAsync`, matching ThetaData's
  max 4 concurrent requests), so a full-chain `5m` day takes on the order of ~15 s.

## Scale / time (important)

A full chain at `5m` is ~**450k rows per trading day** (≈15 active expirations × ~198 strikes ×
2 rights × ~78 bars). Backfilling 2023 → now is on the order of **hundreds of millions of rows**
and many hours of API calls. To keep it tractable:

- Use `--strike-range 50` (≈half the strikes) or `--interval 30m` (≈6× fewer bars) for broad
  coverage, and only use full-chain `5m` for specific short windows.
- Run a partial range first (`--from` / `--to`), then re-run with a wider range — it resumes.

## Requirements

- The Theta Terminal must be running (port `25503`). The web server need not be.
- Database/ThetaData settings come from `Server/appsettings.json` (same as the web app).
