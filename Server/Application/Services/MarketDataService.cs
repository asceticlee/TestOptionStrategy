using Serilog;
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

            await StoreOptionGreeksAsync(symbol, expiration, strike, right, rows);

            OptionGreeksRow? best = rows
                .Where(r => r.ImpliedVol > 0.0001 && r.UnderlyingPrice > 0 && r.Right.Length > 0)
                .OrderBy(r => Math.Abs((r.TimestampUtc - snapshotUtc).TotalSeconds))
                .FirstOrDefault();
            return best;
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
            List<SpotQuoteRow> quotes = await _thetaData.GetStockQuotesAsync(symbol, from, to, interval);
            await StoreSpotQuotesAsync(symbol, quotes);
            return quotes;
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
