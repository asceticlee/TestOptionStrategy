using TestOptionStrategy.Server.Application.WebApi.DTOs;
using TestOptionStrategy.Server.Common;
using TestOptionStrategy.Server.Domain.ThetaData;

namespace TestOptionStrategy.Server.Application.Services
{
    public class OptionSurfaceService
    {
        private readonly MarketDataService _marketData;
        private readonly GreeksCalculator _greeks = new GreeksCalculator();

        public OptionSurfaceService(MarketDataService marketData)
        {
            _marketData = marketData;
        }

        private class ResolvedLeg
        {
            public OptionType Type;
            public double Strike;
            public DateOnly Expiration;
            public int Contracts;
            public double ImpliedVol;
            public double EntryPrice;
            public DateTime ExpiryUtc;
            public double Bid;
            public double Ask;
            public double Delta;
            public double Theta;
            public double Vega;
            public double Gamma;
            public double UnderlyingPrice;
        }

        private class ResolvedInputs
        {
            public TimeZoneInfo TimeZone = null!;
            public DateTime SnapshotUtc;
            public DateOnly SnapshotDay;
            public double RiskFreeRate;
            public List<ResolvedLeg> Legs = new List<ResolvedLeg>();
            public double SnapshotSpot;
            public DateTime MaxExpiryUtc;
        }

        private async Task<ResolvedInputs> ResolveInputsAsync(SurfaceRequest request)
        {
            if (request.Legs.Count == 0)
            {
                throw new ArgumentException("At least one option leg is required.");
            }

            TimeZoneInfo timeZone = MarketClock.GetUsTimeZone();
            DateTime snapshotUtc = ResolveSnapshotUtc(request, timeZone);
            DateOnly snapshotDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(snapshotUtc, timeZone));

            double riskFreeRateDecimal = await _marketData.GetRiskFreeRateAsync(snapshotDay) / 100.0;

            List<(OptionLegRequest Request, string Right, OptionType OptionType, DateOnly Expiration, DateTime ExpiryUtc)> legMeta = request.Legs
                .Select(legRequest =>
                {
                    string right = MarketDataService.NormalizeRight(legRequest.Right);
                    OptionType optionType = right == "call" ? OptionType.Call : OptionType.Put;
                    DateOnly expiration = DateOnly.Parse(legRequest.Expiration);
                    DateTime expiryUtc = ResolveExpiryUtc(expiration, timeZone);
                    return (legRequest, right, optionType, expiration, expiryUtc);
                })
                .ToList();

            OptionGreeksRow?[] greeksRows = await Task.WhenAll(
                legMeta.Select(meta => _marketData.GetOptionGreeksAtSnapshotAsync(
                    request.Symbol, meta.Expiration, meta.Request.Strike, meta.Right, snapshotUtc))
            );

            List<ResolvedLeg> legs = new List<ResolvedLeg>();
            double snapshotSpot = 0;
            for (int i = 0; i < legMeta.Count; i++)
            {
                (OptionLegRequest legRequest, string right, OptionType optionType, DateOnly expiration, DateTime expiryUtc) = legMeta[i];
                OptionGreeksRow? greeksRow = greeksRows[i];

                if (greeksRow == null)
                {
                    throw new InvalidOperationException(
                        $"No market data for {right} {legRequest.Strike} exp {expiration:yyyy-MM-dd} at {request.SnapshotDate} {request.SnapshotTime} ET. " +
                        "The snapshot may be today before market open, a weekend/holiday, or an illiquid contract. Pick an earlier completed trading day.");
                }

                if (snapshotSpot == 0)
                {
                    snapshotSpot = greeksRow.UnderlyingPrice;
                }

                ResolvedLeg leg = new ResolvedLeg
                {
                    Type = optionType,
                    Strike = legRequest.Strike,
                    Expiration = expiration,
                    Contracts = legRequest.Contracts,
                    ImpliedVol = greeksRow.ImpliedVol,
                    ExpiryUtc = expiryUtc,
                    Bid = greeksRow.Bid,
                    Ask = greeksRow.Ask,
                    Delta = greeksRow.Delta,
                    Theta = greeksRow.Theta,
                    Vega = greeksRow.Vega,
                    UnderlyingPrice = greeksRow.UnderlyingPrice
                };
                double daysToExpiry = (expiryUtc - snapshotUtc).TotalDays;
                OptionPriceAndGreeks bsGreeks = _greeks.GetPriceAndGreeks(
                    optionType, snapshotSpot, legRequest.Strike, daysToExpiry, riskFreeRateDecimal, greeksRow.ImpliedVol);
                leg.Gamma = bsGreeks.Gamma;
                leg.EntryPrice = PriceAt(leg, snapshotSpot, snapshotUtc, riskFreeRateDecimal);
                legs.Add(leg);
            }

            if (snapshotSpot == 0)
            {
                snapshotSpot = await _marketData.GetSpotAtSnapshotAsync(request.Symbol, snapshotUtc);
            }

            ResolvedInputs resolved = new ResolvedInputs
            {
                TimeZone = timeZone,
                SnapshotUtc = snapshotUtc,
                SnapshotDay = snapshotDay,
                RiskFreeRate = riskFreeRateDecimal,
                Legs = legs,
                SnapshotSpot = snapshotSpot,
                MaxExpiryUtc = legs.Max(leg => leg.ExpiryUtc)
            };
            return resolved;
        }

        public async Task<StatsLegResult> ComputeLegGreeksAsync(LegGreeksRequest request)
        {
            SurfaceRequest surfaceRequest = new SurfaceRequest
            {
                Symbol = request.Symbol,
                SnapshotDate = request.SnapshotDate,
                SnapshotTime = request.SnapshotTime,
                SpotShares = 0,
                TimeStepMinutes = 30,
                SpotSamples = 51,
                SpotRangePercent = 5,
                Legs = new List<OptionLegRequest> { request.Leg }
            };
            ResolvedInputs resolved = await ResolveInputsAsync(surfaceRequest);
            ResolvedLeg leg = resolved.Legs[0];
            return new StatsLegResult
            {
                Right = leg.Type == OptionType.Call ? "call" : "put",
                Strike = leg.Strike,
                Expiration = leg.Expiration.ToString("yyyy-MM-dd"),
                Contracts = leg.Contracts,
                ImpliedVol = leg.ImpliedVol,
                EntryPrice = leg.EntryPrice,
                Bid = leg.Bid,
                Ask = leg.Ask,
                Delta = leg.Delta,
                Theta = leg.Theta,
                Vega = leg.Vega,
                Gamma = leg.Gamma,
                UnderlyingPrice = leg.UnderlyingPrice
            };
        }

        public async Task<StatsResponse> ComputeStatsAsync(SurfaceRequest request)
        {
            ResolvedInputs resolved = await ResolveInputsAsync(request);
            List<ResolvedLeg> legs = resolved.Legs;

            double netDebit = 0;
            foreach (ResolvedLeg leg in legs)
            {
                netDebit += leg.Contracts * 100.0 * leg.EntryPrice;
            }

            double lower = resolved.SnapshotSpot * 0.7;
            double upper = resolved.SnapshotSpot * 1.3;
            int samples = 400;
            double maxProfit = double.MinValue;
            double maxLoss = double.MaxValue;
            List<double> breakevens = new List<double>();
            double previousPnl = double.NaN;
            double previousSpot = 0;

            for (int i = 0; i <= samples; i++)
            {
                double spot = lower + (upper - lower) * i / samples;
                double pnl = 0;
                foreach (ResolvedLeg leg in legs)
                {
                    double intrinsic = leg.Type == OptionType.Call ? Math.Max(spot - leg.Strike, 0) : Math.Max(leg.Strike - spot, 0);
                    pnl += leg.Contracts * 100.0 * (intrinsic - leg.EntryPrice);
                }
                if (pnl > maxProfit)
                {
                    maxProfit = pnl;
                }
                if (pnl < maxLoss)
                {
                    maxLoss = pnl;
                }
                if (!double.IsNaN(previousPnl) && previousPnl != pnl && ((previousPnl <= 0 && pnl >= 0) || (previousPnl >= 0 && pnl <= 0)))
                {
                    double t = Math.Abs(previousPnl) / (Math.Abs(previousPnl) + Math.Abs(pnl));
                    breakevens.Add(previousSpot + (spot - previousSpot) * t);
                }
                previousPnl = pnl;
                previousSpot = spot;
            }

            StatsResponse response = new StatsResponse
            {
                SnapshotSpot = resolved.SnapshotSpot,
                RiskFreeRate = resolved.RiskFreeRate,
                NetDebit = netDebit,
                MaxProfit = maxProfit,
                MaxLoss = maxLoss,
                Breakevens = breakevens,
                Legs = legs.Select(leg => new StatsLegResult
                {
                    Right = leg.Type == OptionType.Call ? "call" : "put",
                    Strike = leg.Strike,
                    Expiration = leg.Expiration.ToString("yyyy-MM-dd"),
                    Contracts = leg.Contracts,
                    ImpliedVol = leg.ImpliedVol,
                    EntryPrice = leg.EntryPrice,
                    Bid = leg.Bid,
                    Ask = leg.Ask,
                    Delta = leg.Delta,
                    Theta = leg.Theta,
                    Vega = leg.Vega,
                    Gamma = leg.Gamma,
                    UnderlyingPrice = leg.UnderlyingPrice
                }).ToList()
            };
            return response;
        }

        public async Task<SurfaceResponse> ComputeRealAsync(SurfaceRequest request)
        {
            ResolvedInputs resolved = await ResolveInputsAsync(request);
            TimeZoneInfo timeZone = resolved.TimeZone;
            DateTime snapshotUtc = resolved.SnapshotUtc;
            DateOnly snapshotDay = resolved.SnapshotDay;
            double riskFreeRate = resolved.RiskFreeRate;
            List<ResolvedLeg> legs = resolved.Legs;
            double snapshotSpot = resolved.SnapshotSpot;
            DateTime maxExpiryUtc = resolved.MaxExpiryUtc;

            DateOnly endDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(maxExpiryUtc, timeZone));
            DateOnly today = DateOnly.FromDateTime(MarketClock.UtcNowInUsEastern());
            DateOnly dataEndDay = endDay >= today ? today.AddDays(-1) : endDay;
            if (dataEndDay < snapshotDay)
            {
                throw new InvalidOperationException("No historical option data is available after the trade date for the real surface.");
            }

            string interval = MapTimeStepToInterval(request.TimeStepMinutes);

            Task<List<OptionGreeksRow>>[] fetchTasks = legs
                .Select(leg => _marketData.GetOptionGreeksSeriesAsync(
                    request.Symbol,
                    leg.Expiration,
                    leg.Strike,
                    leg.Type == OptionType.Call ? "call" : "put",
                    snapshotDay,
                    dataEndDay,
                    interval))
                .ToArray();
            List<List<OptionGreeksRow>> legSeries = (await Task.WhenAll(fetchTasks)).ToList();

            for (int i = 0; i < legSeries.Count; i++)
            {
                legSeries[i] = legSeries[i]
                    .Where(r => r.ImpliedVol > 0.0001 && r.Bid > 0 && r.Ask > 0)
                    .OrderBy(r => r.TimestampUtc)
                    .ToList();
                BridgeOvernightIv(legSeries[i]);
            }

            SortedSet<DateTime> timeSet = new SortedSet<DateTime>();
            foreach (List<OptionGreeksRow> rows in legSeries)
            {
                foreach (OptionGreeksRow row in rows)
                {
                    if (row.TimestampUtc >= snapshotUtc && row.TimestampUtc <= maxExpiryUtc)
                    {
                        timeSet.Add(row.TimestampUtc);
                    }
                }
            }
            List<DateTime> timeAxis = timeSet.ToList();
            if (timeAxis.Count == 0)
            {
                throw new InvalidOperationException("No historical option data is available for the real surface over this range.");
            }

            List<double> spotAxis = BuildSpotAxis(snapshotSpot, legs, request.SpotRangePercent, request.SpotSamples);

            List<List<double>> valueSurface = new List<List<double>>();
            List<List<double>> deltaSurface = new List<List<double>>();
            List<List<double>> gammaSurface = new List<List<double>>();
            List<SpotLinePoint> valueLine = new List<SpotLinePoint>();
            List<SpotLinePoint> deltaLine = new List<SpotLinePoint>();
            List<SpotLinePoint> gammaLine = new List<SpotLinePoint>();

            foreach (DateTime timeUtc in timeAxis)
            {
                double[] ivs = new double[legs.Count];
                double actualSpot = 0;
                for (int i = 0; i < legs.Count; i++)
                {
                    OptionGreeksRow? nearest = Nearest(legSeries[i], timeUtc);
                    ivs[i] = nearest != null ? nearest.ImpliedVol : legs[i].ImpliedVol;
                    if (actualSpot == 0 && nearest != null && nearest.UnderlyingPrice > 0)
                    {
                        actualSpot = nearest.UnderlyingPrice;
                    }
                }
                if (actualSpot == 0)
                {
                    actualSpot = snapshotSpot;
                }

                List<double> valueRow = new List<double>();
                List<double> deltaRow = new List<double>();
                List<double> gammaRow = new List<double>();
                foreach (double spot in spotAxis)
                {
                    double value = 0;
                    double delta = 0;
                    double gamma = 0;
                    for (int i = 0; i < legs.Count; i++)
                    {
                        ResolvedLeg leg = legs[i];
                        value += leg.Contracts * 100.0 * (PriceAt(leg, spot, timeUtc, riskFreeRate, ivs[i]) - leg.EntryPrice);
                        delta += leg.Contracts * 100.0 * DeltaAt(leg, spot, timeUtc, riskFreeRate, ivs[i]);
                        gamma += leg.Contracts * 100.0 * GammaAt(leg, spot, timeUtc, riskFreeRate, ivs[i]);
                    }
                    valueRow.Add(value);
                    deltaRow.Add(delta);
                    gammaRow.Add(gamma);
                }
                valueSurface.Add(valueRow);
                deltaSurface.Add(deltaRow);
                gammaSurface.Add(gammaRow);

                string y = FormatTime(timeUtc, timeZone);
                double lineValue = 0;
                double lineDelta = 0;
                double lineGamma = 0;
                for (int i = 0; i < legs.Count; i++)
                {
                    ResolvedLeg leg = legs[i];
                    lineValue += leg.Contracts * 100.0 * (PriceAt(leg, actualSpot, timeUtc, riskFreeRate, ivs[i]) - leg.EntryPrice);
                    lineDelta += leg.Contracts * 100.0 * DeltaAt(leg, actualSpot, timeUtc, riskFreeRate, ivs[i]);
                    lineGamma += leg.Contracts * 100.0 * GammaAt(leg, actualSpot, timeUtc, riskFreeRate, ivs[i]);
                }
                valueLine.Add(new SpotLinePoint { X = actualSpot, Y = y, Z = lineValue });
                deltaLine.Add(new SpotLinePoint { X = actualSpot, Y = y, Z = lineDelta });
                gammaLine.Add(new SpotLinePoint { X = actualSpot, Y = y, Z = lineGamma });
            }

            SurfaceResponse response = new SurfaceResponse
            {
                SpotPrices = spotAxis,
                Timestamps = timeAxis.Select(t => FormatTime(t, timeZone)).ToList(),
                ValueSurface = valueSurface,
                DeltaSurface = deltaSurface,
                GammaSurface = gammaSurface,
                SpotLineValue = valueLine,
                SpotLineDelta = deltaLine,
                SpotLineGamma = gammaLine,
                SnapshotSpot = snapshotSpot,
                RiskFreeRate = riskFreeRate,
                Legs = legs.Select(leg => new OptionLegResult
                {
                    Right = leg.Type == OptionType.Call ? "call" : "put",
                    Strike = leg.Strike,
                    Expiration = leg.Expiration.ToString("yyyy-MM-dd"),
                    Contracts = leg.Contracts,
                    ImpliedVol = leg.ImpliedVol,
                    EntryPrice = leg.EntryPrice
                }).ToList()
            };
            return response;
        }

        public async Task<SurfaceResponse> ComputeAsync(SurfaceRequest request)
        {
            ResolvedInputs resolved = await ResolveInputsAsync(request);
            TimeZoneInfo timeZone = resolved.TimeZone;
            DateTime snapshotUtc = resolved.SnapshotUtc;
            DateOnly snapshotDay = resolved.SnapshotDay;
            double riskFreeRateDecimal = resolved.RiskFreeRate;
            List<ResolvedLeg> legs = resolved.Legs;
            double snapshotSpot = resolved.SnapshotSpot;
            DateTime maxExpiryUtc = resolved.MaxExpiryUtc;

            List<DateTime> timeAxisUtc = BuildTimeAxis(snapshotUtc, maxExpiryUtc, request.TimeStepMinutes, timeZone);
            List<double> spotAxis = BuildSpotAxis(snapshotSpot, legs, request.SpotRangePercent, request.SpotSamples);

            List<List<double>> valueSurface = new List<List<double>>();
            List<List<double>> deltaSurface = new List<List<double>>();
            List<List<double>> gammaSurface = new List<List<double>>();

            foreach (DateTime timeUtc in timeAxisUtc)
            {
                List<double> valueRow = new List<double>();
                List<double> deltaRow = new List<double>();
                List<double> gammaRow = new List<double>();
                foreach (double spot in spotAxis)
                {
                    double value = 0;
                    double delta = 0;
                    double gamma = 0;
                    foreach (ResolvedLeg leg in legs)
                    {
                        value += leg.Contracts * 100.0 * (PriceAt(leg, spot, timeUtc, riskFreeRateDecimal) - leg.EntryPrice);
                        delta += leg.Contracts * 100.0 * DeltaAt(leg, spot, timeUtc, riskFreeRateDecimal);
                        gamma += leg.Contracts * 100.0 * GammaAt(leg, spot, timeUtc, riskFreeRateDecimal);
                    }
                    value += request.SpotShares * (spot - snapshotSpot);
                    valueRow.Add(value);
                    deltaRow.Add(delta);
                    gammaRow.Add(gamma);
                }
                valueSurface.Add(valueRow);
                deltaSurface.Add(deltaRow);
                gammaSurface.Add(gammaRow);
            }

            (List<SpotLinePoint> valueLine, List<SpotLinePoint> deltaLine, List<SpotLinePoint> gammaLine) = await BuildSpotLinesAsync(
                request.Symbol, snapshotDay, maxExpiryUtc, timeAxisUtc, legs, riskFreeRateDecimal, timeZone);

            SurfaceResponse response = new SurfaceResponse
            {
                SpotPrices = spotAxis,
                Timestamps = timeAxisUtc.Select(t => FormatTime(t, timeZone)).ToList(),
                ValueSurface = valueSurface,
                DeltaSurface = deltaSurface,
                GammaSurface = gammaSurface,
                SpotLineValue = valueLine,
                SpotLineDelta = deltaLine,
                SpotLineGamma = gammaLine,
                SnapshotSpot = snapshotSpot,
                RiskFreeRate = riskFreeRateDecimal,
                Legs = legs.Select(leg => new OptionLegResult
                {
                    Right = leg.Type == OptionType.Call ? "call" : "put",
                    Strike = leg.Strike,
                    Expiration = leg.Expiration.ToString("yyyy-MM-dd"),
                    Contracts = leg.Contracts,
                    ImpliedVol = leg.ImpliedVol,
                    EntryPrice = leg.EntryPrice
                }).ToList()
            };
            return response;
        }

        private DateTime ResolveSnapshotUtc(SurfaceRequest request, TimeZoneInfo timeZone)
        {
            DateOnly day = DateOnly.Parse(request.SnapshotDate);
            TimeSpan time = TimeSpan.Parse(request.SnapshotTime);
            DateTime eastern = new DateTime(day.Year, day.Month, day.Day, time.Hours, time.Minutes, time.Seconds);
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(eastern, DateTimeKind.Unspecified), timeZone);
        }

        private DateTime ResolveExpiryUtc(DateOnly expiration, TimeZoneInfo timeZone)
        {
            DateTime eastern = new DateTime(expiration.Year, expiration.Month, expiration.Day, 16, 0, 0);
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(eastern, DateTimeKind.Unspecified), timeZone);
        }

        private List<DateTime> BuildTimeAxis(DateTime snapshotUtc, DateTime maxExpiryUtc, int stepMinutes, TimeZoneInfo timeZone)
        {
            List<DateTime> result = new List<DateTime>();
            DateOnly startDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(snapshotUtc, timeZone));
            DateOnly endDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(maxExpiryUtc, timeZone));
            TimeSpan marketOpen = new TimeSpan(9, 30, 0);
            TimeSpan marketClose = new TimeSpan(16, 0, 0);
            TimeSpan step = TimeSpan.FromMinutes(Math.Max(stepMinutes, 1));

            for (DateOnly day = startDay; day <= endDay; day = day.AddDays(1))
            {
                if (MarketClock.IsWeekend(day))
                {
                    continue;
                }
                for (TimeSpan t = marketOpen; t <= marketClose; t += step)
                {
                    DateTime eastern = new DateTime(day.Year, day.Month, day.Day, t.Hours, t.Minutes, t.Seconds);
                    DateTime utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(eastern, DateTimeKind.Unspecified), timeZone);
                    if (utc >= snapshotUtc && utc <= maxExpiryUtc)
                    {
                        result.Add(utc);
                    }
                }
            }
            return result;
        }

        private List<double> BuildSpotAxis(double snapshotSpot, List<ResolvedLeg> legs, double rangePercent, int samples)
        {
            double fraction = rangePercent / 100.0;
            double lower = snapshotSpot * (1.0 - fraction);
            double upper = snapshotSpot * (1.0 + fraction);
            double minStrike = legs.Min(leg => leg.Strike);
            double maxStrike = legs.Max(leg => leg.Strike);
            lower = Math.Min(lower, minStrike * 0.98);
            upper = Math.Max(upper, maxStrike * 1.02);

            int count = Math.Max(samples, 10);
            List<double> result = new List<double>();
            for (int i = 0; i <= count; i++)
            {
                double value = lower + (upper - lower) * i / count;
                result.Add(value);
            }
            return result;
        }

        private double PriceAt(ResolvedLeg leg, double spot, DateTime timeUtc, double riskFreeRate)
        {
            return PriceAt(leg, spot, timeUtc, riskFreeRate, leg.ImpliedVol);
        }

        private double PriceAt(ResolvedLeg leg, double spot, DateTime timeUtc, double riskFreeRate, double iv)
        {
            double dte = (leg.ExpiryUtc - timeUtc).TotalDays;
            if (dte <= 0)
            {
                return leg.Type == OptionType.Call ? Math.Max(spot - leg.Strike, 0) : Math.Max(leg.Strike - spot, 0);
            }
            return _greeks.GetPriceAndGreeks(leg.Type, spot, leg.Strike, dte, riskFreeRate, iv).Price;
        }

        private double DeltaAt(ResolvedLeg leg, double spot, DateTime timeUtc, double riskFreeRate)
        {
            return DeltaAt(leg, spot, timeUtc, riskFreeRate, leg.ImpliedVol);
        }

        private double DeltaAt(ResolvedLeg leg, double spot, DateTime timeUtc, double riskFreeRate, double iv)
        {
            double dte = (leg.ExpiryUtc - timeUtc).TotalDays;
            if (dte <= 0)
            {
                return leg.Type == OptionType.Call ? (spot > leg.Strike ? 1 : 0) : (spot < leg.Strike ? -1 : 0);
            }
            return _greeks.GetPriceAndGreeks(leg.Type, spot, leg.Strike, dte, riskFreeRate, iv).Delta;
        }

        private double GammaAt(ResolvedLeg leg, double spot, DateTime timeUtc, double riskFreeRate)
        {
            return GammaAt(leg, spot, timeUtc, riskFreeRate, leg.ImpliedVol);
        }

        private double GammaAt(ResolvedLeg leg, double spot, DateTime timeUtc, double riskFreeRate, double iv)
        {
            double dte = (leg.ExpiryUtc - timeUtc).TotalDays;
            if (dte <= 0)
            {
                return 0;
            }
            return _greeks.GetPriceAndGreeks(leg.Type, spot, leg.Strike, dte, riskFreeRate, iv).Gamma;
        }

        private string MapTimeStepToInterval(int timeStepMinutes)
        {
            if (timeStepMinutes <= 15)
            {
                return "15m";
            }
            if (timeStepMinutes <= 30)
            {
                return "30m";
            }
            return "1h";
        }

        private void BridgeOvernightIv(List<OptionGreeksRow> series)
        {
            TimeZoneInfo timeZone = MarketClock.GetUsTimeZone();
            double lastIv = 0;
            DateOnly previousDate = default;
            foreach (OptionGreeksRow row in series)
            {
                DateOnly date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(row.TimestampUtc, timeZone));
                if (previousDate != default && date != previousDate)
                {
                    row.ImpliedVol = lastIv;
                }
                lastIv = row.ImpliedVol;
                previousDate = date;
            }
        }

        private OptionGreeksRow? Nearest(List<OptionGreeksRow> rows, DateTime timeUtc)
        {
            OptionGreeksRow? best = null;
            double bestSeconds = double.MaxValue;
            foreach (OptionGreeksRow row in rows)
            {
                if (row.ImpliedVol <= 0.0001 || row.UnderlyingPrice <= 0 || row.Bid <= 0 || row.Ask <= 0)
                {
                    continue;
                }
                double seconds = Math.Abs((row.TimestampUtc - timeUtc).TotalSeconds);
                if (seconds < bestSeconds)
                {
                    bestSeconds = seconds;
                    best = row;
                }
            }
            return best;
        }

        private async Task<(List<SpotLinePoint> Value, List<SpotLinePoint> Delta, List<SpotLinePoint> Gamma)> BuildSpotLinesAsync(
            string symbol,
            DateOnly snapshotDay,
            DateTime maxExpiryUtc,
            List<DateTime> timeAxisUtc,
            List<ResolvedLeg> legs,
            double riskFreeRate,
            TimeZoneInfo timeZone)
        {
            DateOnly endDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(maxExpiryUtc, timeZone));
            DateOnly today = DateOnly.FromDateTime(MarketClock.UtcNowInUsEastern());
            DateOnly spotEndDay = endDay >= today ? today.AddDays(-1) : endDay;
            if (spotEndDay < snapshotDay)
            {
                return (new List<SpotLinePoint>(), new List<SpotLinePoint>(), new List<SpotLinePoint>());
            }
            List<SpotQuoteRow> quotes = await _marketData.GetSpotQuotesAsync(symbol, snapshotDay, spotEndDay, "5m");
            List<SpotQuoteRow> validQuotes = quotes.Where(q => q.Bid > 0 && q.Ask > 0).OrderBy(q => q.TimestampUtc).ToList();

            List<SpotLinePoint> valueLine = new List<SpotLinePoint>();
            List<SpotLinePoint> deltaLine = new List<SpotLinePoint>();
            List<SpotLinePoint> gammaLine = new List<SpotLinePoint>();
            foreach (DateTime timeUtc in timeAxisUtc)
            {
                SpotQuoteRow? nearest = FindNearest(validQuotes, timeUtc, TimeSpan.FromMinutes(30));
                if (nearest == null)
                {
                    continue;
                }
                double spot = nearest.Mid();
                double value = 0;
                double delta = 0;
                double gamma = 0;
                foreach (ResolvedLeg leg in legs)
                {
                    value += leg.Contracts * 100.0 * (PriceAt(leg, spot, timeUtc, riskFreeRate) - leg.EntryPrice);
                    delta += leg.Contracts * 100.0 * DeltaAt(leg, spot, timeUtc, riskFreeRate);
                    gamma += leg.Contracts * 100.0 * GammaAt(leg, spot, timeUtc, riskFreeRate);
                }
                string y = FormatTime(timeUtc, timeZone);
                valueLine.Add(new SpotLinePoint { X = spot, Y = y, Z = value });
                deltaLine.Add(new SpotLinePoint { X = spot, Y = y, Z = delta });
                gammaLine.Add(new SpotLinePoint { X = spot, Y = y, Z = gamma });
            }
            return (valueLine, deltaLine, gammaLine);
        }

        private SpotQuoteRow? FindNearest(List<SpotQuoteRow> quotes, DateTime timeUtc, TimeSpan maxDistance)
        {
            if (quotes.Count == 0)
            {
                return null;
            }
            SpotQuoteRow? best = null;
            double bestSeconds = double.MaxValue;
            foreach (SpotQuoteRow quote in quotes)
            {
                double seconds = Math.Abs((quote.TimestampUtc - timeUtc).TotalSeconds);
                if (seconds < bestSeconds)
                {
                    bestSeconds = seconds;
                    best = quote;
                }
            }
            if (best == null || bestSeconds > maxDistance.TotalSeconds)
            {
                return null;
            }
            return best;
        }

        private string FormatTime(DateTime timeUtc, TimeZoneInfo timeZone)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(timeUtc, timeZone).ToString("yyyy-MM-dd HH:mm");
        }
    }
}
