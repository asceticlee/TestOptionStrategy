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
        }

        public async Task<SurfaceResponse> ComputeAsync(SurfaceRequest request)
        {
            if (request.Legs.Count == 0)
            {
                throw new ArgumentException("At least one option leg is required.");
            }

            TimeZoneInfo timeZone = MarketClock.GetUsTimeZone();
            DateTime snapshotUtc = ResolveSnapshotUtc(request, timeZone);
            DateOnly snapshotDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(snapshotUtc, timeZone));

            double riskFreeRateDecimal = await _marketData.GetRiskFreeRateAsync(snapshotDay) / 100.0;

            List<ResolvedLeg> legs = new List<ResolvedLeg>();
            double snapshotSpot = 0;
            foreach (OptionLegRequest legRequest in request.Legs)
            {
                string right = MarketDataService.NormalizeRight(legRequest.Right);
                OptionType optionType = right == "call" ? OptionType.Call : OptionType.Put;
                DateOnly expiration = DateOnly.Parse(legRequest.Expiration);
                DateTime expiryUtc = ResolveExpiryUtc(expiration, timeZone);

                OptionGreeksRow? greeksRow = await _marketData.GetOptionGreeksAtSnapshotAsync(
                    request.Symbol, expiration, legRequest.Strike, right, snapshotUtc);

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
                    ExpiryUtc = expiryUtc
                };
                leg.EntryPrice = PriceAt(leg, snapshotSpot, snapshotUtc, riskFreeRateDecimal);
                legs.Add(leg);
            }

            if (snapshotSpot == 0)
            {
                snapshotSpot = await _marketData.GetSpotAtSnapshotAsync(request.Symbol, snapshotUtc);
            }

            DateTime maxExpiryUtc = legs.Max(leg => leg.ExpiryUtc);

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
            double dte = (leg.ExpiryUtc - timeUtc).TotalDays;
            if (dte <= 0)
            {
                return leg.Type == OptionType.Call ? Math.Max(spot - leg.Strike, 0) : Math.Max(leg.Strike - spot, 0);
            }
            return _greeks.GetPriceAndGreeks(leg.Type, spot, leg.Strike, dte, riskFreeRate, leg.ImpliedVol).Price;
        }

        private double DeltaAt(ResolvedLeg leg, double spot, DateTime timeUtc, double riskFreeRate)
        {
            double dte = (leg.ExpiryUtc - timeUtc).TotalDays;
            if (dte <= 0)
            {
                return leg.Type == OptionType.Call ? (spot > leg.Strike ? 1 : 0) : (spot < leg.Strike ? -1 : 0);
            }
            return _greeks.GetPriceAndGreeks(leg.Type, spot, leg.Strike, dte, riskFreeRate, leg.ImpliedVol).Delta;
        }

        private double GammaAt(ResolvedLeg leg, double spot, DateTime timeUtc, double riskFreeRate)
        {
            double dte = (leg.ExpiryUtc - timeUtc).TotalDays;
            if (dte <= 0)
            {
                return 0;
            }
            return _greeks.GetPriceAndGreeks(leg.Type, spot, leg.Strike, dte, riskFreeRate, leg.ImpliedVol).Gamma;
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
