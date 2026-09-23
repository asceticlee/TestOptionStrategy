# ThetaData v3 — Findings & How-To

## What it is

ThetaData uses a proprietary protocol; a local **Theta Terminal** (Java) hosts a REST API that you
call. v3 REST base URL: `http://127.0.0.1:25503/v3`.

> `ThetaData/api.key` is a **secret** and is gitignored (`*.key`, `ThetaData/creds.txt`). Never commit
> it. The terminal jar, its extracted `ThetaData/lib/`, and the generated `config.toml` are also
> gitignored (downloadable / regenerated).

## Launching the terminal (verified)

The `ThetaData/api.key` contains a single API key (`td1_prod_...`). The terminal authenticates via
the **`THETADATA_API_KEY`** environment variable (NOT a creds file for this key type):

```sh
cd ThetaData
THETADATA_API_KEY="$(cat api.key)" java -jar ThetaTerminalv3.jar
```

Verified output on launch:
```
Subscriptions: Stock: STANDARD Options: STANDARD Index: FREE Rate: FREE
Max concurrent requests: 4
Starting server at: http://0.0.0.0:25503/
```

A `config.toml` is auto-generated on first launch (port 25503, host 0.0.0.0, auth via
`https://nexus-api.thetadata.us/identity/terminal/auth_user`).

> Keep the terminal running while the server collects data. To run in background:
> `nohup env THETADATA_API_KEY="$(cat api.key)" java -jar ThetaTerminalv3.jar > /tmp/thetadata.log 2>&1 &`

## Subscription level (IMPORTANT)

The account is **STANDARD** for Stock and Options. Endpoints with `x-min-subscription: professional`
DO NOT work (return a text error). Verified available:

| Endpoint | Sub | Use |
|----------|-----|-----|
| `GET /stock/list/symbols` | free | list symbols |
| `GET /option/list/expirations?symbol=SPY` | free | expirations |
| `GET /option/list/strikes?symbol=SPY&expiration=YYYYMMDD` | free | strikes |
| `GET /option/list/contracts/{quote\|trade}?symbol=SPY&expiration=...` | value | full chain |
| `GET /option/history/greeks/first_order` | **standard** | delta, theta, vega, rho, **implied_vol**, bid/ask, underlying_price |
| `GET /option/history/greeks/implied_volatility` | standard | implied vol only |
| `GET /option/history/quote` | value | option bid/ask quotes |
| `GET /option/history/trade_quote` | standard | option trades + quotes |
| `GET /option/history/eod` | free | option EOD |
| `GET /stock/history/quote` | value | intraday stock NBBO |
| `GET /stock/history/trade` / `eod` | standard/free | stock trades / EOD |
| `GET /interest_rate/history/eod?symbol=SOFR` | **free** | risk-free rate (SOFR) |
| `GET /calendar/today` | free | trading calendar |

NOT available (professional): `/option/history/greeks/all`, `/option/history/greeks/second_order`,
`/option/history/greeks/third_order`, all `trade_greeks/*`, all `flat_file/*`.

**Consequence:** ThetaData gives us first-order Greeks + implied vol, but **not Gamma** at this tier.
We compute Gamma (and full Greeks) server-side via Black-Scholes using the IV + risk-free rate (see
`GREEKS-AND-INTEREST-RATE.md`).

## Request/response conventions (v3, differ from v2)

- Base path `/v3` (v2 was `/v2`). Port **25503** (v2 was 25510).
- **Response format is columnar JSON**: each field is an array. e.g.
  `{"symbol":["SPY","SPY"],"expiration":["2026-09-18",...]}` — NOT an array of objects.
  Add `&format=json` (default is CSV). `format=html` opens a browser table.
- Dates: `start_date`/`end_date`/`expiration`/`date` accept `YYYY-MM-DD` or `YYYYMMDD`.
- `interval` uses strings: `tick`, `10ms`…`30s`, `1m`, `5m`, `10m`, `15m`, `30m`, `1h`.
  (v2 used milliseconds like `60000`.)
- `right` request param is `call` / `put` / `both`; response values are `CALL` / `PUT`.
- `strike` is in dollars as a float (`600` = $600), NOT strike*1000 like v2.
- Greeks `rate_type` default is `sofr`; you may also pass `rate_value` (percent).
- Timestamps are naive ET, e.g. `2026-09-01T09:30:00.000`.

### Example greeks first_order call (verified)

```
GET /v3/option/history/greeks/first_order?symbol=SPY&expiration=20260918&strike=600&right=call&start_date=20260901&end_date=20260905&interval=10m&format=json
```
Returns columns: `symbol, expiration, strike, right, timestamp, bid, ask, delta, theta, vega, rho,
epsilon, lambda, implied_vol, iv_error, underlying_timestamp, underlying_price`.

**Whole-chain fetch (verified):** omit `strike` and `right` to get **all strikes and both rights for a
single expiration** in one response (e.g. ~198 strikes for SPY). `expiration` must be a specific date
(no `*` on this endpoint). Multi-day requests are limited to **1 month** — the data loader (see
`DATA-LOADING.md`) chunks by week and month to respect this and the ~500k-row pagination ceiling.

**1-month bulk limit applies to the surface too.** `first_order` (and `stock/history/quote`) reject a
single request whose `start_date..end_date` spans more than ~1 month (`Bulk history requests are limited
to no more than 1 month`). The live surface's smile-window fetch (`MarketDataService.
GetOptionGreeksSmileWindowAsync`) and spot-quote fetch (`GetSpotQuotesAsync`) both **chunk** (14 / 20 days
respectively) so a strategy whose expiry is > ~1 month from the snapshot still works — e.g. a 27-Jul
snapshot with a 04-Sep expiry (40 days) previously failed the 3D Real surface with "No historical option
data is available for the real surface over this range."

### Example interest rate (verified)

```
GET /v3/interest_rate/history/eod?symbol=SOFR&start_date=20260901&end_date=20260905&format=json
→ {"rate":[3.66,...],"created":["2026-09-01",...]}
```
`rate` is percent (3.66 = 3.66%).

## The OpenAPI spec

`ThetaData/openapiv3.yaml` is the source of truth (already downloaded). Grep `x-min-subscription` to
check tier availability before coding against an endpoint.

## Concurrency / pagination

Max concurrent requests = 4. Responses > 500K rows are paginated (a `Next-Page` header / url). Not
yet relevant for our surface use case.

## Current-day requests (important)

Requests whose `end_date` is **today** fail with:
`Current day requests must have a start time less than current time`.

Concretely: before market open (09:30 ET) there is no data for today at all, so a snapshot of
"today" returns nothing and the surface call errors with "No market data for ... at <date> <time> ET".
The client therefore defaults its snapshot date to the **last completed trading day** (yesterday,
skipping weekends), and the server returns a clear message telling the user to pick an earlier day.

## Expiration listing vs data availability (backtests)

`GET /option/list/expirations` returns **every** expiration ThetaData knows about (past and future,
2012→2029 for SPY), but data for a given expiration only exists from its **listing date** to its
expiry. SPY's listing schedule is:

- **Monthlies (3rd Friday)** — listed months (up to ~a year+) in advance, so a past trade date has data
  for them (e.g. 16-Oct-2026 was trading on 27-Jul-2026).
- **Weeklies (every Friday)** — listed ~6 weeks (≈42 days) before expiry (verified: the 09-Oct-2026
  weekly first traded **28-Aug-2026**, exactly 6 weeks before 09-Oct). So it is NOT available 10 weeks
  out, but IS available ~3 weeks out (e.g. visible on 14-Sep-2026).
- **Dailies / 0DTE** — listed ~1 day before.

So a weekly like **09-Oct-2026** (a Friday) did NOT exist on a **27-Jul-2026** trade date (~10 weeks
out) — ThetaData returns `No data found`, and the surface reports "No market data". The expiration strip
still lists it (the list is static), which is why it looks selectable but then errors. To backtest a long
expiry on a past date, pick a **monthly** (3rd-Friday) expiry rather than a weekly. The server's
"No market data" message now mentions the not-yet-listed possibility.
