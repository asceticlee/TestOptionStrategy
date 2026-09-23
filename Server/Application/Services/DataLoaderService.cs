using Serilog;
using TestOptionStrategy.Server.Common;
using TestOptionStrategy.Server.Domain.Data;
using TestOptionStrategy.Server.Domain.ThetaData;

namespace TestOptionStrategy.Server.Application.Services
{
    public class DataLoaderService
    {
        private const int WeekChunkDays = 7;

        private readonly ThetaDataClient _thetaData;
        private readonly UnderlyingRepository _underlyingRepository;
        private readonly SpotQuoteRepository _spotQuoteRepository;
        private readonly OptionGreeksRepository _optionGreeksRepository;
        private readonly RiskFreeRateRepository _riskFreeRateRepository;
        private readonly GreeksCalculator _greeks = new GreeksCalculator();
        private readonly double _spreadOutlierK = AppSettings.GetDouble("ThetaData:SpreadOutlierK", 3.0);
        private readonly double _spreadOutlierFloor = AppSettings.GetDouble("ThetaData:SpreadOutlierFloor", 0.25);

        public DataLoaderService(
            ThetaDataClient thetaData,
            UnderlyingRepository underlyingRepository,
            SpotQuoteRepository spotQuoteRepository,
            OptionGreeksRepository optionGreeksRepository,
            RiskFreeRateRepository riskFreeRateRepository)
        {
            _thetaData = thetaData;
            _underlyingRepository = underlyingRepository;
            _spotQuoteRepository = spotQuoteRepository;
            _optionGreeksRepository = optionGreeksRepository;
            _riskFreeRateRepository = riskFreeRateRepository;
        }

        public async Task UpdateAsync(string symbol, string interval, int? strikeRange, int leadDays)
        {
            TimeZoneInfo timeZone = MarketClock.GetUsTimeZone();
            DateOnly to = LastCompletedTradingDay();

            DateTime? lastGreeksUtc = await _optionGreeksRepository.GetMaxTsUtcAsync(symbol);
            DateTime? lastSpotUtc = await _spotQuoteRepository.GetMaxTsUtcAsync(symbol);

            DateOnly from;
            if (lastGreeksUtc.HasValue || lastSpotUtc.HasValue)
            {
                DateTime lastUtc = DateTime.MinValue;
                if (lastGreeksUtc.HasValue && lastGreeksUtc.Value > lastUtc)
                {
                    lastUtc = lastGreeksUtc.Value;
                }
                if (lastSpotUtc.HasValue && lastSpotUtc.Value > lastUtc)
                {
                    lastUtc = lastSpotUtc.Value;
                }
                from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(lastUtc, timeZone)).AddDays(-2);
            }
            else
            {
                from = to.AddDays(-7);
            }

            if (from > to)
            {
                Log.Information("Data is already current through {To}; nothing to update.", to);
                return;
            }

            await BackfillAsync(symbol, from, to, interval, strikeRange, leadDays);
        }

        public async Task BackfillAsync(string symbol, DateOnly from, DateOnly to, string interval, int? strikeRange, int leadDays)
        {
            DateOnly safeTo = to > LastCompletedTradingDay() ? LastCompletedTradingDay() : to;
            if (from > safeTo)
            {
                Log.Warning("Nothing to load: from {From} is after to {To}.", from, safeTo);
                return;
            }

            await _underlyingRepository.UpsertAsync("us", symbol);
            List<(DateOnly Day, double Rate)> rates = await LoadRiskFreeRatesAsync(from.AddDays(-10), safeTo);
            await LoadSpotQuotesAsync(symbol, from, safeTo, interval);

            List<DateOnly> expirations = await _thetaData.ListOptionExpirationsAsync(symbol);
            long totalRows = 0;

            for (DateOnly month = new DateOnly(from.Year, from.Month, 1); month <= safeTo; month = month.AddMonths(1))
            {
                DateOnly monthStart = month > from ? month : from;
                DateOnly monthEnd = LastOfMonth(month) < safeTo ? LastOfMonth(month) : safeTo;
                DateOnly windowEnd = monthEnd.AddDays(leadDays);

                List<DateOnly> monthExpirations = expirations
                    .Where(e => e >= monthStart && e <= windowEnd)
                    .OrderBy(e => e)
                    .ToList();

                List<(DateOnly Expiration, DateOnly Start, DateOnly End)> chunks = new List<(DateOnly, DateOnly, DateOnly)>();
                foreach (DateOnly expiration in monthExpirations)
                {
                    DateOnly fetchEnd = expiration < monthEnd ? expiration : monthEnd;
                    for (DateOnly chunkStart = monthStart; chunkStart <= fetchEnd; chunkStart = chunkStart.AddDays(WeekChunkDays))
                    {
                        DateOnly chunkEnd = chunkStart.AddDays(WeekChunkDays - 1) < fetchEnd
                            ? chunkStart.AddDays(WeekChunkDays - 1)
                            : fetchEnd;
                        chunks.Add((expiration, chunkStart, chunkEnd));
                    }
                }

                long monthRows = 0;
                await Parallel.ForEachAsync(
                    chunks,
                    new ParallelOptions { MaxDegreeOfParallelism = 4 },
                    async (chunk, cancellationToken) =>
                    {
                        List<OptionGreeksRow> rows = await _thetaData.GetOptionGreeksFirstOrderChainAsync(
                            symbol, chunk.Expiration, chunk.Start, chunk.End, interval, strikeRange);
                        if (rows.Count == 0)
                        {
                            return;
                        }
                        foreach (OptionGreeksRow row in rows)
                        {
                            row.ImpliedVol = ComputeMidImpliedVol(row, rates);
                        }
                        MarketDataService.ApplySpreadOutlierFilter(rows, _spreadOutlierK, _spreadOutlierFloor);
                        List<OptionGreeksEntity> entities = rows.Select(ToEntity).ToList();
                        await _optionGreeksRepository.UpsertManyAsync(entities);
                        Interlocked.Add(ref monthRows, entities.Count);
                    });

                totalRows += monthRows;
                Log.Information("Backfilled option greeks for {Symbol} month {MonthStart} .. {MonthEnd}: {MonthRows} rows ({ExpirationCount} expirations, {ChunkCount} requests).",
                    symbol, monthStart, monthEnd, monthRows, monthExpirations.Count, chunks.Count);
            }

            Log.Information("Backfill complete: {TotalRows} option greeks rows stored for {Symbol} [{From} .. {To}].",
                totalRows, symbol, from, safeTo);
        }

        private static OptionGreeksEntity ToEntity(OptionGreeksRow row)
        {
            return new OptionGreeksEntity
            {
                Symbol = row.Symbol,
                Expiration = row.Expiration.ToDateTime(TimeOnly.MinValue),
                Strike = (decimal)row.Strike,
                Right = MarketDataService.NormalizeRight(row.Right),
                TsUtc = row.TimestampUtc,
                Bid = (decimal)row.Bid,
                Ask = (decimal)row.Ask,
                Delta = (decimal)row.Delta,
                Theta = (decimal)row.Theta,
                Vega = (decimal)row.Vega,
                Rho = (decimal)row.Rho,
                ImpliedVol = (decimal)row.ImpliedVol,
                UnderlyingPrice = (decimal)row.UnderlyingPrice
            };
        }

        private async Task<List<(DateOnly Day, double Rate)>> LoadRiskFreeRatesAsync(DateOnly from, DateOnly to)
        {
            List<(DateOnly Day, double Rate)> rates = await _thetaData.GetInterestRateEodAsync("SOFR", from, to);
            if (rates.Count == 0)
            {
                return rates;
            }
            List<RiskFreeRateEntity> entities = rates
                .Select(r => new RiskFreeRateEntity { Day = r.Day.ToDateTime(TimeOnly.MinValue), Rate = (decimal)r.Rate })
                .ToList();
            await _riskFreeRateRepository.UpsertManyAsync(entities);
            return rates;
        }

        private double ComputeMidImpliedVol(OptionGreeksRow row, List<(DateOnly Day, double Rate)> rates)
        {
            DateOnly day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(row.TimestampUtc, MarketClock.GetUsTimeZone()));
            double riskFreeRateDecimal = RateForDay(rates, day);
            OptionType optionType = MarketDataService.NormalizeRight(row.Right) == "put" ? OptionType.Put : OptionType.Call;
            double daysToExpiry = (MarketClock.ExpiryUtc(row.Expiration) - row.TimestampUtc).TotalDays;
            return _greeks.CalculateMidImpliedVolatility(
                optionType, row.UnderlyingPrice, row.Strike, daysToExpiry, riskFreeRateDecimal, row.Bid, row.Ask);
        }

        private static double RateForDay(List<(DateOnly Day, double Rate)> rates, DateOnly day)
        {
            double percent = 0;
            foreach ((DateOnly Day, double Rate) item in rates)
            {
                if (item.Day > day)
                {
                    break;
                }
                percent = item.Rate;
            }
            return percent / 100.0;
        }

        private async Task LoadSpotQuotesAsync(string symbol, DateOnly from, DateOnly to, string interval)
        {
            for (DateOnly month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
            {
                DateOnly start = month > from ? month : from;
                DateOnly end = LastOfMonth(month) < to ? LastOfMonth(month) : to;

                List<SpotQuoteRow> quotes = await _thetaData.GetStockQuotesAsync(symbol, start, end, interval);
                if (quotes.Count == 0)
                {
                    continue;
                }
                List<SpotQuoteEntity> entities = quotes
                    .Select(q => new SpotQuoteEntity { Symbol = symbol, TsUtc = q.TimestampUtc, Bid = (decimal)q.Bid, Ask = (decimal)q.Ask })
                    .ToList();
                await _spotQuoteRepository.UpsertManyAsync(entities);
            }
        }

        public static DateOnly LastCompletedTradingDay()
        {
            DateTime now = MarketClock.UtcNowInUsEastern();
            DateOnly candidate = now.TimeOfDay >= new TimeSpan(16, 0, 0)
                ? DateOnly.FromDateTime(now)
                : DateOnly.FromDateTime(now).AddDays(-1);
            while (candidate.DayOfWeek == DayOfWeek.Saturday || candidate.DayOfWeek == DayOfWeek.Sunday)
            {
                candidate = candidate.AddDays(-1);
            }
            return candidate;
        }

        private static DateOnly LastOfMonth(DateOnly month)
        {
            return new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
        }
    }
}
