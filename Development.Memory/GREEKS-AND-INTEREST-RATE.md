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

## Interest rate sources

1. **SOFR (preferred, free, daily):**
   `GET /v3/interest_rate/history/eod?symbol=SOFR&start_date=...&end_date=...`
   Returns `rate` (percent). For a snapshot we use the most recent rate ≤ snapshot date.
2. **10Y Treasury (reference impl in `BidAskLast`):** `USTreasuryRate.Get10YearsTreasuryYieldCurveAsync`
   fetches `https://home.treasury.gov/.../daily_treasury_yield_curve&field_tdr_date_value=<year>`
   (Atom XML), parses `NEW_DATE` + `BC_10YEAR`. Kept as a fallback/reference in `Server/Domain/Treasury/`.

We store the daily rate in `risk_free_rate` (see `DATABASE.md`). In Black-Scholes we use the decimal
form (`rate / 100`).
