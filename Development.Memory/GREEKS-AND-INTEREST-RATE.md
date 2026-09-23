# Greeks & Interest Rate

## What ThetaData gives us

At STANDARD tier, `/option/history/greeks/first_order` returns `delta, theta, vega, rho, epsilon,
lambda` plus `implied_vol`, `underlying_price`, `bid`, `ask`. **Gamma is NOT included** (needs
`professional` for second-order). So we calculate Gamma (and can recompute all Greeks) ourselves.

## Approach

For each option leg we need, at a snapshot `(S, T, K, right)`:
- `sigma` = implied vol from ThetaData (or computed from option mid via Newton/Brent).
- `r` = risk-free rate (SOFR from `/interest_rate/history/eod`, or 10Y treasury from `USTreasuryRate.cs`).
- Price + all Greeks via **Black-Scholes** (see `GREEKS-AND-INTEREST-RATE.md` formulas).

## Black-Scholes formulas (standard, ported from `Temp/OptionPricing3DSurface`)

With `T` in years (`daysToExpiry / 365`):

```
d1 = ( ln(S/K) + (r + sigma^2/2)*T ) / (sigma * sqrt(T))
d2 = d1 - sigma*sqrt(T)

call price = S*N(d1) - K*e^{-rT}*N(d2)
put  price = K*e^{-rT}*N(-d2) - S*N(-d1)

delta_call = N(d1)          delta_put = N(d1) - 1  (= -N(-d1))
gamma      = e^{-d1^2/2} / (S*sigma*sqrt(2*pi*T))     (same for call & put)
vega       = S*sqrt(T)*e^{-d1^2/2} / sqrt(2*pi)
theta_call = -S*sigma*e^{-d1^2/2} / (2*sqrt(2*pi*T)) - r*K*e^{-rT}*N(d2)
theta_put  = -S*sigma*e^{-d1^2/2} / (2*sqrt(2*pi*T)) + r*K*e^{-rT}*N(-d2)
rho_call   = K*T*e^{-rT}*N(d2)       rho_put = -K*T*e^{-rT}*N(-d2)
```

These match the TypeScript reference (`option.service.ts`), which is correct.

### Gamma from vega (identity used in BidAskLast)

`gamma = vega / (S^2 * sigma * T)`  (valid when both use the same units; T in years).

### Notes on units
- The TypeScript reference expresses `theta` in **per-year** terms. BidAskLast's
  `GreeksCalculator.CalculateTheta` divides by 365 (per-day) and also adds an extra
  `-r*S*e^{-rT}*N(d1)` term (non-standard). For the 3D surface we project PnL via **price**, not
  theta, so this doesn't affect the surface. Documented here to avoid confusion.
- `daysToExpiry` for the surface uses actual calendar days between snapshot and expiry
  (matches the reference). ThetaData uses a "0DTE floor" version for its own Greeks (`version` param,
  default `latest`, min 1 hour TTE) — our local BS uses true TTE.

### Data quality / degenerate inputs (important)
- ThetaData `first_order` greeks rows can have `implied_vol = 0` (and `underlying_price = 0`) when
  there is no valid quote (e.g. at market open). These rows are **not** usable in Black-Scholes:
  `sigma = 0` makes `d1`/`d2` → ±∞ and the Greeks → NaN/Infinity, which then **fails JSON
  serialisation** (`System.ArgumentException: positive/negative infinity`). Two guards were added:
  1. When building a real/historical IV series, only rows with `implied_vol > 0.0001` **and**
     `underlying_price > 0` (and `bid > 0 && ask > 0`) are considered (fallback to the snapshot IV
     otherwise). The 3D Real surface also **bridges the IV overnight** (first valid quote of a day carries
     the previous close's IV) to avoid the empty-open-quote jump.
  2. `GreeksCalculator.GetPriceAndGreeks` returns the intrinsic value (delta ±1/0, zero other Greeks)
     for `sigma <= 0` / NaN / ∞ or non-positive `S`/`K`, instead of producing Infinity.

### 3D Real choppiness — now marked at the mid, not the last trade (resolved)
The 3D Real surface re-prices each leg with **Black-Scholes at an IV we invert from the mid**
(`GreeksCalculator.CalculateMidImpliedVolatility`), not ThetaData's `implied_vol` (which is derived from
the last **trade** and bounces bid↔ask). `MarketDataService.ApplyMidImpliedVol` overwrites `row.ImpliedVol`
with the mid-implied IV immediately after every ThetaData fetch, so the snapshot path, the real-surface
path, and the stored rows all carry mid-IV. The data loader (`DataLoaderService`) does the same when
backfilling. `CalculateMidImpliedVolatility` returns 0 when the mid is degenerate (bid/ask ≤ 0, deep-ITM
with vega ≈ 0), and those rows are filtered by the existing guards.

Result: `BS(mid-IV) == mid` exactly (verified via `/api/surface/stats` — `entryPrice == (bid+ask)/2` to
~1e-12), so the chart tracks the quoted mid instead of the last trade.

Remaining choppiness is **not** the mid-vs-trade issue any more — it is genuine market noise from the
wide bid/ask on illiquid deep-ITM strikes. The example butterfly (742C / 2×747C / 752C) has spreads
~$1.17 / ~$1.92 / ~$0.08 (butterfly quoted spread ≈ $5.09, ±$2.5), and the −2× middle leg amplifies that.
Even the *mid* bounces because market-makers update these quotes sporadically; the butterfly's time-value
(the intrinsic cancels) carries ~±$2 of noise. If further smoothing is wanted, apply a rolling mean to the
mid/IV series — but that trades lag for smoothness.

The delta/theta/vega/rho columns (leg panel + `option_greeks` table) still come from ThetaData and are
computed at ThetaData's own IV; only `implied_vol` is mid-based. The surface's value/delta/gamma are
recomputed server-side from the mid-IV, so the chart is self-consistent.

#### Worked example — one bad ask blows up a butterfly (14-Aug-2026)
The butterfly 742C / 2×747C / 752C (exp 21-Aug-2026) moved −49.50 → −166 in one 30-min bar (14:30 →
15:30) while SPY spot only moved 775.93 → 776.15. Per-leg decomposition (bid/ask from ThetaData, mid-IV
computed by us):

| time | leg | bid | ask | spread | mid | mid-IV |
|------|-----|-----|-----|--------|-----|--------|
| 14:30 | 742C | 34.52 | 34.68 | 0.16 | 34.60 | 16.4% |
| 14:30 | 747C | 29.57 | 29.88 | 0.31 | 29.725 | 15.9% |
| 14:30 | 752C | 24.72 | 24.78 | 0.06 | 24.75 | 13.8% |
| 15:30 | 742C | 34.68 | 34.88 | 0.20 | 34.78 | 15.8% |
| 15:30 | 747C | 29.78 | **31.20** | **1.42** | **30.49** | **20.3%** |
| 15:30 | 752C | 24.91 | 24.96 | 0.05 | 24.935 | 13.7% |

The 747C **ask** spiked 29.88 → 31.20 while its bid barely moved (29.57 → 29.78); neighbouring bars
confirm it is an outlier (15:00 ask 30.19, 16:00 ask 30.18). The mid jumped +0.765, and because the
butterfly is short **2×** 747C that became −2×0.765 = −1.53, offset by the wings (+0.18 / +0.19) →
−1.165 on the butterfly, i.e. −$116 PnL. The 0.22 spot move is irrelevant: the butterfly's intrinsic
cancels, so its value is pure time-value convexity and is dominated by the middle leg's quote quality.

Lesson: mid-IV removes the last-trade bounce but is still hostage to a single wide/spurious ask on an
illiquid off-round middle strike, which the −2× middle leg amplifies. If this churns the chart too much,
add an outlier filter (drop or median-smooth rows whose spread is ≫ the day's typical spread for that
strike) before inverting the IV.

#### Implemented: spread-outlier filter
Removes the spurious-ask spikes by filtering on the **bid/ask spread** before inverting the mid-IV, in
`MarketDataService.ApplySpreadOutlierFilter` — called from both `MarketDataService.ApplyMidImpliedVol`
(live snapshot + real series) and `DataLoaderService` (backfill).

Per contract **and per ET trading day** (group rows by expiration + strike + right + ET date):
1. Collect `spread = ask - bid` for rows with `bid>0 && ask>0`.
2. Skip the day if it has < ~4 valid rows.
3. `Q1`, `Q3`, `IQR = Q3 - Q1`; fence `threshold = max(Q3 + k·IQR, floor)`.
4. `k` and `floor` are config in `appsettings.json` → `ThetaData:SpreadOutlierK` (default `3.0`) and
   `ThetaData:SpreadOutlierFloor` (default `0.25`), read via `AppSettings.GetDouble`.
5. Rows with `spread > threshold` → `ImpliedVol = 0`; the existing `ImpliedVol > 0.0001` guards drop them
   and the surface snaps to the neighbour. Raw bid/ask are still stored (non-destructive).

**Key lesson — the baseline must be per-day, not per-fetch.** Grouping the whole fetched series (a full
month for the real surface) fails: an illiquid strike like 747C is "sticky wide" across the month
(median 0.50, Q3 1.34, IQR 1.19), so even the 1.42 ask-spike sits under `Q3 + 3·IQR ≈ 4.9` and is kept.
Grouping by ET day (14-Aug alone: Q1 0.45 / Q3 0.69 / IQR 0.24 → fence 1.41) correctly flags the 1.42 and
3.72 spikes. Verified: 14-Aug 15:30 butterfly value smoothed −166 → −62, and the 747C 15:30 row is stored
with `implied_vol = 0`.

Supporting change: `OptionGreeksRepository.UpsertManyAsync` now uses
`ON CONFLICT … DO UPDATE SET implied_vol = excluded.implied_vol` (was `DO NOTHING`), so re-running the
backfill refreshes the archived IV with the filtered value instead of silently keeping stale rows.

#### Residual choppiness: persistently-wide illiquid strikes (not fixable by the filter)
The spread filter only removes **transient** spikes. It cannot help when a strike is **persistently**
wide — and that is exactly what the 742/747 off-round ($1-increment) legs are. Verified on the
21-Aug-2026 butterfly (742C / 2×747C / 752C):

- 742C and 747C carry a **$1.2–3.0 spread all day**, while 752C stays at ~$0.08. Their quotes update
  **asynchronously** — 747C's spread flips wide (~$1.9) ↔ tight (~$0.4) every few minutes — so the mid
  bounces inside a $1.5–3 band and the mid-IV wobbles **±2–3 vol points** across the day (747C: 0.13 ↔
  0.18). The per-day IQR fence correctly does NOT flag this: the width is the baseline, not an outlier.
- Because the butterfly is **2× short the 747C**, that ~$1 mid wiggle ≈ $2 on the butterfly ≈ $200 PnL
  (the observed −158 → +179 swings on 5–6 Aug). The 10:30→12:00 jump was 747C IV 16.8%→13.3% — a
  3.5-vol drop that sits right at the edge of what a $1.4–1.7 spread can resolve, i.e. noise, not a real
  move.

Conclusion: the mid is a poor mark for a *persistently*-wide strike; this is genuine illiquidity, not a
data bug. Candidate improvements (in effort order): (1) rolling-median the mid (±30 min) before inverting
IV; (2) mark at the last **trade** instead of the mid for wide-spread strikes; (3) interpolate IV from a
fitted smile over the liquid $5-round strikes.

#### Smile-regularized IV (implemented)
The mid-derived IV of an illiquid off-round strike can be a **smile notch**, not just noisy. At
6-Aug-2026 12:00 (S=768.15) the 21-Aug-2026 chain read:

| leg | bid | ask | IV(bid) | IV(ask) | IV(mid) |
|-----|-----|-----|---------|---------|---------|
| 742C | 28.74 | 30.55 | 15.42% | 20.29% | 18.02% |
| 747C | 23.01 | 24.45 | 10.94% | 15.27% | 13.32% |
| 752C | 19.96 | 20.05 | 14.14% | 14.34% | 14.24% |

747C's mid-IV (13.32%) sits below **both** neighbours. Linear interpolation 742→752 gives 747C ≈ 16.13%,
and `BS(16.13%) = 24.795` is **$0.35 above the ask** — i.e. the whole 747C quote was stale, not just the
mid.

**Implemented fix (3D Real + snapshot entry):** per timestamp, fit a smooth IV smile and use it to mark
each leg, clamped to the leg's executable bid/ask:

1. Fetch a **bounded smile window** of ~15 strikes around the legs (each `right=both` single-strike
   `first_order` call, 4-way parallel) — NOT the whole chain (which the Theta terminal serves in ~90s).
   `MarketDataService.GetOptionGreeksSmileWindowAsync`.
2. Build smile points from **OTM** options (put for K≤spot, call for K>spot), weighted `1/spread`, fit a
   quadratic in log-moneyness `ln(K/S)` (`Common/SmileFitter.cs`, weighted least squares).
3. Per leg: `iv_smile = curve(K)`; then `iv_mark = clamp(iv_smile, [iv_bid, iv_ask])` where iv_bid/iv_ask
   are the leg's own bid/ask inverted to IV (`DeduceLegMarkIv`). This is equivalent to the user's rule
   "clamp the deduced price to the nearest bid/ask bound".

Result (butterfly 742C / 2×747C / 752C, 27-Jul snapshot, 30m): the −158 → +179 swings collapse to
−12 → −40, and the 14-Aug 15:30 spike (−166) is gone (−39.4 → −39.7). Entry IVs become a smooth monotone
smile (742 15.4%, 747 14.5%, 752 13.8%).

Performance: ~15 s for the real surface (dominated by the terminal's ~3.3 s/request latency × ~15 strikes
÷ 4 parallel), ~2.5 s for stats/snapshot. `delta/theta/vega/rho` in the leg panel still come from
ThetaData; the surface's value/delta/gamma are recomputed from the smile IV.

#### Degenerate-smile fallback (wide-quote events)
During a market-wide **wide-quote event** every option's spread blows out for a bar (e.g. 29-Jul-2026
14:00, 3-Aug-2026 10:00): the per-day IQR spread filter zeroes **all** the smile-window strikes, leaving
`points == 0`, so the quadratic fit yields `ivSmile = 0` and `DeduceLegMarkIv` returned 0 → the leg was
priced at **intrinsic** (BS σ=0), producing huge spikes (a 10-wide bear call spread 742C/752C showed
+591 and −1328 while neighbours were ~±100). Fix: `ComputeRealAsync` now keeps a per-leg `lastIv` and
**carries forward** the previous valid IV when `DeduceLegMarkIv` returns ≤ 0, instead of using 0. The
spikes collapse to the neighbours (69 / −110).

Related case — a **single leg's** quote is filtered (not the whole smile): when only one leg's own row is
zeroed (wide spread) the smile still has points, so `DeduceLegMarkIv` returned the **unclamped** smile IV
for that leg while the other legs stayed **clamped** to their quotes — the inconsistent IVs produced a
discontinuity (17-Sep-2026 16:00, butterfly 757/762/767: value spiked to +99 vs neighbours ≈ −2). Fix:
when the leg's own row is missing (`own == null`) `DeduceLegMarkIv` now returns 0, so the same
carry-forward kicks in (the leg uses the previous valid IV) and the surface stays continuous.

### Lesson: do NOT use trading-time DTE
An experiment switched `daysToExpiry` to "trading time" (sum market hours, ÷6.5) to hide the overnight
theta "chasm". It was **reverted**: it broke pricing (a long-far/short-near calendar spread flipped from
a debit to a credit, because the far leg's weekend was dropped from its time-to-expiry) and is
inconsistent with ThetaData's calendar-time-calibrated IV. Theta decay over closed periods is real and
must stay; the only chasm that was genuinely spurious was the empty-open-quote IV (handled above).

### Calendar-spread carry asymmetry (correct, not a bug)
A same-strike calendar spread (long far / short near) shows **asymmetric** P&L far from the strike:
deep OTM ≈ `−net_debit`, deep ITM ≈ `−net_debit + carry`. The intrinsic cancels (same strike), leaving the
risk-free discount `K·(1−e^(−rT))`, whose far-vs-near difference (`K·(1−e^(−rT_far)) − K·(1−e^(−rT_near))`)
makes the ITM side less negative. For `r≈3.65%`, `K≈766`, `ΔT≈4d` this is ≈ +$31/contract. This is the
interest "carry" of the calendar spread and is intentional — do not "fix" it to be symmetric.

## Interest rate sources

1. **SOFR (preferred, free, daily):**
   `GET /v3/interest_rate/history/eod?symbol=SOFR&start_date=...&end_date=...`
   Returns `rate` (percent). For a snapshot we use the most recent rate ≤ snapshot date.
2. **10Y Treasury (reference impl in `BidAskLast`):** `USTreasuryRate.Get10YearsTreasuryYieldCurveAsync`
   fetches `https://home.treasury.gov/.../daily_treasury_yield_curve&field_tdr_date_value=<year>`
   (Atom XML), parses `NEW_DATE` + `BC_10YEAR`. Kept as a fallback/reference in `Server/Domain/Treasury/`.

We store the daily rate in `risk_free_rate` (see `DATABASE.md`). In Black-Scholes we use the decimal
form (`rate / 100`).
