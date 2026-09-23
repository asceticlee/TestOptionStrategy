using Serilog;
using TestOptionStrategy.Server.Common;
using TestOptionStrategy.Server.Domain.Data;
using TestOptionStrategy.Server.Domain.ThetaData;

namespace TestOptionStrategy.Server.Application.Services
{
    public class MarketDataService
    {
        private readonly ThetaDataClient _thetaData;
        private readonly UnderlyingRepository _underlyingRepository;
        private readonly OptionContractRepository _optionContractRepository;
        private readonly SpotQuoteRepository _spotQuoteRepository;
        private readonly OptionGreeksRepository _optionGreeksRepository;
        private readonly RiskFreeRateRepository _riskFreeRateRepository;
        private readonly GreeksCalculator _greeks = new GreeksCalculator();
        private readonly double _spreadOutlierK = AppSettings.GetDouble("ThetaData:SpreadOutlierK", 3.0);
        private readonly double _spreadOutlierFloor = AppSettings.GetDouble("ThetaData:SpreadOutlierFloor", 0.25);

        public MarketDataService(
            ThetaDataClient thetaData,
            UnderlyingRepository underlyingRepository,
            OptionContractRepository optionContractRepository,
            SpotQuoteRepository spotQuoteRepository,
            OptionGreeksRepository optionGreeksRepository,
            RiskFreeRateRepository riskFreeRateRepository)
        {
            _thetaData = thetaData;
            _underlyingRepository = underlyingRepository;
            _optionContractRepository = optionContractRepository;
            _spotQuoteRepository = spotQuoteRepository;
            _optionGreeksRepository = optionGreeksRepository;
            _riskFreeRateRepository = riskFreeRateRepository;
        }

        public async Task<List<string>> GetUnderlyingsAsync()
        {
            await _underlyingRepository.UpsertAsync("us", "SPY");
            return await _underlyingRepository.ListSymbolsAsync();
        }

        public async Task<List<DateOnly>> GetExpirationsAsync(string symbol)
        {
            return await _thetaData.ListOptionExpirationsAsync(symbol);
        }

        public async Task<List<double>> GetStrikesAsync(string symbol, DateOnly expiration)
        {
            return await _thetaData.ListOptionStrikesAsync(symbol, expiration);
        }

        public async Task<double> GetSpotAtSnapshotAsync(string symbol, DateTime snapshotUtc)
        {
            DateOnly snapshotDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(snapshotUtc, Common.MarketClock.GetUsTimeZone()));
            List<SpotQuoteRow> quotes = await _thetaData.GetStockQuotesAsync(symbol, snapshotDay, snapshotDay, "1m");
            SpotQuoteRow? best = quotes
                .Where(q => q.Bid > 0 && q.Ask > 0)
                .OrderBy(q => Math.Abs((q.TimestampUtc - snapshotUtc).TotalSeconds))
                .FirstOrDefault();
            if (best != null)
            {
                return best.Mid();
            }
            return 0;
        }

        public async Task<(double Spot, double PreviousClose)> GetQuoteSummaryAsync(string symbol, DateTime snapshotUtc)
        {
            double spot = await GetSpotAtSnapshotAsync(symbol, snapshotUtc);
            DateOnly snapshotDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(snapshotUtc, Common.MarketClock.GetUsTimeZone()));
            List<(DateOnly Day, double Close)> eod = await _thetaData.GetStockEodAsync(symbol, snapshotDay.AddDays(-10), snapshotDay.AddDays(-1));
            double previousClose = 0;
            if (eod.Count > 0)
            {
                previousClose = eod[^1].Close;
            }
            return (spot, previousClose);
        }

        public async Task<OptionGreeksRow?> GetOptionGreeksAtSnapshotAsync(
            string symbol,
            DateOnly expiration,
            double strike,
            string right,
            DateTime snapshotUtc)
        {
            DateOnly snapshotDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(snapshotUtc, Common.MarketClock.GetUsTimeZone()));
            List<OptionGreeksRow> rows = await _thetaData.GetOptionGreeksFirstOrderAsync(
                symbol, expiration, strike, right, snapshotDay, snapshotDay, "1m");

            double riskFreeRateDecimal = await GetRiskFreeRateAsync(snapshotDay) / 100.0;
            ApplyMidImpliedVol(rows, riskFreeRateDecimal);

            await StoreOptionGreeksAsync(symbol, expiration, strike, right, rows);

            OptionGreeksRow? best = rows
                .Where(r => r.ImpliedVol > 0.0001 && r.UnderlyingPrice > 0 && r.Right.Length > 0)
                .OrderBy(r => Math.Abs((r.TimestampUtc - snapshotUtc).TotalSeconds))
                .FirstOrDefault();
            return best;
        }

        public async Task<List<OptionGreeksRow>> GetOptionGreeksSeriesAsync(
            string symbol,
            DateOnly expiration,
            double strike,
            string right,
            DateOnly startDate,
            DateOnly endDate,
            string interval)
        {
            List<OptionGreeksRow> rows = await _thetaData.GetOptionGreeksFirstOrderAsync(
                symbol, expiration, strike, right, startDate, endDate, interval);

            double riskFreeRateDecimal = await GetRiskFreeRateAsync(startDate) / 100.0;
            ApplyMidImpliedVol(rows, riskFreeRateDecimal);

            await StoreOptionGreeksAsync(symbol, expiration, strike, right, rows);
            return rows;
        }

        public async Task<List<OptionGreeksRow>> GetOptionGreeksSmileWindowAsync(
            string symbol,
            DateOnly expiration,
            DateOnly startDate,
            DateOnly endDate,
            string interval,
            List<double> strikes)
        {
            double riskFreeRateDecimal = await GetRiskFreeRateAsync(startDate) / 100.0;

            const int chunkDays = 14;
            List<(DateOnly Start, DateOnly End)> chunks = new List<(DateOnly, DateOnly)>();
            for (DateOnly chunkStart = startDate; chunkStart <= endDate; chunkStart = chunkStart.AddDays(chunkDays))
            {
                DateOnly chunkEnd = chunkStart.AddDays(chunkDays - 1) < endDate ? chunkStart.AddDays(chunkDays - 1) : endDate;
                chunks.Add((chunkStart, chunkEnd));
            }

            List<(int StrikeIndex, DateOnly Start, DateOnly End)> work = new List<(int, DateOnly, DateOnly)>();
            for (int s = 0; s < strikes.Count; s++)
            {
                foreach ((DateOnly Start, DateOnly End) chunk in chunks)
                {
                    work.Add((s, chunk.Start, chunk.End));
                }
            }

            List<OptionGreeksRow>[] buckets = new List<OptionGreeksRow>[work.Count];
            await Parallel.ForEachAsync(
                Enumerable.Range(0, work.Count),
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (i, cancellationToken) =>
                {
                    (int StrikeIndex, DateOnly Start, DateOnly End) item = work[i];
                    List<OptionGreeksRow> rows = await _thetaData.GetOptionGreeksFirstOrderAsync(
                        symbol, expiration, strikes[item.StrikeIndex], "both", item.Start, item.End, interval);
                    ApplyMidImpliedVol(rows, riskFreeRateDecimal);
                    buckets[i] = rows;
                });

            List<OptionGreeksRow> result = new List<OptionGreeksRow>();
            foreach (List<OptionGreeksRow> bucket in buckets)
            {
                result.AddRange(bucket);
            }
            return result;
        }

        public async Task<List<(DateOnly Date, string Type)>> GetYearHolidaysAsync(int year)
        {
            return await _thetaData.GetYearHolidaysAsync(year);
        }

        public async Task<double> GetRiskFreeRateAsync(DateOnly day)
        {
            RiskFreeRateEntity? cached = await _riskFreeRateRepository.GetLatestOnOrBeforeAsync(day);
            if (cached != null)
            {
                return (double)cached.Rate;
            }

            DateOnly from = day.AddDays(-10);
            List<(DateOnly Day, double Rate)> rates = await _thetaData.GetInterestRateEodAsync("SOFR", from, day);
            if (rates.Count > 0)
            {
                List<RiskFreeRateEntity> entities = rates
                    .Select(r => new RiskFreeRateEntity { Day = r.Day.ToDateTime(TimeOnly.MinValue), Rate = (decimal)r.Rate })
                    .ToList();
                await _riskFreeRateRepository.UpsertManyAsync(entities);
                (DateOnly Day, double Rate) latest = rates.OrderByDescending(r => r.Day).First();
                return latest.Rate;
            }

            Log.Warning("No risk-free rate available on or before {Day}; defaulting to 0", day);
            return 0;
        }

        public async Task<List<SpotQuoteRow>> GetSpotQuotesAsync(string symbol, DateOnly from, DateOnly to, string interval)
        {
            const int chunkDays = 20;
            List<SpotQuoteRow> result = new List<SpotQuoteRow>();
            for (DateOnly chunkStart = from; chunkStart <= to; chunkStart = chunkStart.AddDays(chunkDays))
            {
                DateOnly chunkEnd = chunkStart.AddDays(chunkDays - 1) < to ? chunkStart.AddDays(chunkDays - 1) : to;
                List<SpotQuoteRow> quotes = await _thetaData.GetStockQuotesAsync(symbol, chunkStart, chunkEnd, interval);
                if (quotes.Count > 0)
                {
                    await StoreSpotQuotesAsync(symbol, quotes);
                    result.AddRange(quotes);
                }
            }
            return result;
        }

        public static string NormalizeRight(string right)
        {
            string lower = right.Trim().ToLowerInvariant();
            if (lower == "c" || lower == "call")
            {
                return "call";
            }
            if (lower == "p" || lower == "put")
            {
                return "put";
            }
            return lower;
        }

        private void ApplyMidImpliedVol(List<OptionGreeksRow> rows, double riskFreeRateDecimal)
        {
            foreach (OptionGreeksRow row in rows)
            {
                OptionType optionType = NormalizeRight(row.Right) == "put" ? OptionType.Put : OptionType.Call;
                double daysToExpiry = (MarketClock.ExpiryUtc(row.Expiration) - row.TimestampUtc).TotalDays;
                row.ImpliedVol = _greeks.CalculateMidImpliedVolatility(
                    optionType, row.UnderlyingPrice, row.Strike, daysToExpiry, riskFreeRateDecimal, row.Bid, row.Ask);
            }
            ApplySpreadOutlierFilter(rows, _spreadOutlierK, _spreadOutlierFloor);
        }

        public static void ApplySpreadOutlierFilter(List<OptionGreeksRow> rows, double k, double floor)
        {
            TimeZoneInfo timeZone = MarketClock.GetUsTimeZone();
            IEnumerable<IGrouping<(DateOnly Expiration, double Strike, string Right, DateOnly Day), OptionGreeksRow>> groups = rows
                .Where(r => r.Bid > 0 && r.Ask > 0)
                .GroupBy(r => (
                    r.Expiration,
                    r.Strike,
                    NormalizeRight(r.Right),
                    DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(r.TimestampUtc, timeZone))));

            foreach (IGrouping<(DateOnly Expiration, double Strike, string Right, DateOnly Day), OptionGreeksRow> group in groups)
            {
                List<double> spreads = group.Select(r => r.Ask - r.Bid).ToList();
                if (spreads.Count < 4)
                {
                    continue;
                }
                spreads.Sort();
                double q1 = Percentile(spreads, 0.25);
                double q3 = Percentile(spreads, 0.75);
                double threshold = Math.Max(q3 + k * (q3 - q1), floor);
                foreach (OptionGreeksRow row in group)
                {
                    if ((row.Ask - row.Bid) > threshold)
                    {
                        row.ImpliedVol = 0;
                    }
                }
            }
        }

        private static double Percentile(List<double> sorted, double fraction)
        {
            double position = fraction * (sorted.Count - 1);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
            {
                return sorted[lower];
            }
            double weight = position - lower;
            return sorted[lower] * (1.0 - weight) + sorted[upper] * weight;
        }

        private async Task StoreOptionGreeksAsync(string symbol, DateOnly expiration, double strike, string right, List<OptionGreeksRow> rows)
        {
            if (rows.Count == 0)
            {
                return;
            }
            string normalizedRight = NormalizeRight(right);
            List<OptionGreeksEntity> entities = rows.Select(r => new OptionGreeksEntity
            {
                Symbol = symbol,
                Expiration = expiration.ToDateTime(TimeOnly.MinValue),
                Strike = (decimal)strike,
                Right = normalizedRight,
                TsUtc = r.TimestampUtc,
                Bid = (decimal)r.Bid,
                Ask = (decimal)r.Ask,
                Delta = (decimal)r.Delta,
                Theta = (decimal)r.Theta,
                Vega = (decimal)r.Vega,
                Rho = (decimal)r.Rho,
                ImpliedVol = (decimal)r.ImpliedVol,
                UnderlyingPrice = (decimal)r.UnderlyingPrice
            }).ToList();
            await _optionGreeksRepository.UpsertManyAsync(entities);
        }

        private async Task StoreSpotQuotesAsync(string symbol, List<SpotQuoteRow> quotes)
        {
            if (quotes.Count == 0)
            {
                return;
            }
            List<SpotQuoteEntity> entities = quotes.Select(q => new SpotQuoteEntity
            {
                Symbol = symbol,
                TsUtc = q.TimestampUtc,
                Bid = (decimal)q.Bid,
                Ask = (decimal)q.Ask
            }).ToList();
            await _spotQuoteRepository.UpsertManyAsync(entities);
        }
    }
}
