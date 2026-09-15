using System.Globalization;
using System.Text.Json;
using Serilog;
using TestOptionStrategy.Server.Common;

namespace TestOptionStrategy.Server.Domain.ThetaData
{
    public class ThetaDataClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        public ThetaDataClient()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(120);
            _baseUrl = AppSettings.GetSetting("ThetaData:BaseUrl");
            if (string.IsNullOrEmpty(_baseUrl))
            {
                _baseUrl = "http://127.0.0.1:25503/v3";
            }
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false
            };
        }

        public async Task<List<DateOnly>> ListOptionExpirationsAsync(string symbol)
        {
            string url = $"{_baseUrl}/option/list/expirations?symbol={symbol}&format=json";
            OptionExpirationsResponse? response = await GetJsonAsync<OptionExpirationsResponse>(url);
            if (response == null)
            {
                return new List<DateOnly>();
            }
            List<DateOnly> result = new List<DateOnly>();
            for (int i = 0; i < response.Expiration.Count; i++)
            {
                if (DateOnly.TryParse(response.Expiration[i], out DateOnly date))
                {
                    result.Add(date);
                }
            }
            result.Sort();
            return result;
        }

        public async Task<List<double>> ListOptionStrikesAsync(string symbol, DateOnly expiration)
        {
            string exp = FormatDate(expiration);
            string url = $"{_baseUrl}/option/list/strikes?symbol={symbol}&expiration={exp}&format=json";
            OptionStrikesResponse? response = await GetJsonAsync<OptionStrikesResponse>(url);
            if (response == null)
            {
                return new List<double>();
            }
            List<double> result = new List<double>(response.Strike);
            result.Sort();
            return result;
        }

        public async Task<List<OptionGreeksRow>> GetOptionGreeksFirstOrderAsync(
            string symbol,
            DateOnly expiration,
            double strike,
            string right,
            DateOnly startDate,
            DateOnly endDate,
            string interval)
        {
            string strikeText = strike.ToString("0.0#####", CultureInfo.InvariantCulture);
            string url = $"{_baseUrl}/option/history/greeks/first_order?symbol={symbol}&expiration={FormatDate(expiration)}&strike={strikeText}&right={right}&start_date={FormatDate(startDate)}&end_date={FormatDate(endDate)}&interval={interval}&format=json";
            OptionGreeksFirstOrderResponse? response = await GetJsonAsync<OptionGreeksFirstOrderResponse>(url);
            if (response == null)
            {
                return new List<OptionGreeksRow>();
            }
            List<OptionGreeksRow> result = new List<OptionGreeksRow>();
            int count = response.Timestamp.Count;
            for (int i = 0; i < count; i++)
            {
                result.Add(new OptionGreeksRow
                {
                    Symbol = i < response.Symbol.Count ? response.Symbol[i] : symbol,
                    Expiration = expiration,
                    Strike = i < response.Strike.Count ? response.Strike[i] : strike,
                    Right = i < response.Right.Count ? response.Right[i] : right.ToUpperInvariant(),
                    TimestampUtc = MarketClock.ParseThetaTimestampAsUtc(response.Timestamp[i]),
                    Bid = i < response.Bid.Count ? response.Bid[i] : 0,
                    Ask = i < response.Ask.Count ? response.Ask[i] : 0,
                    Delta = i < response.Delta.Count ? response.Delta[i] : 0,
                    Theta = i < response.Theta.Count ? response.Theta[i] : 0,
                    Vega = i < response.Vega.Count ? response.Vega[i] : 0,
                    Rho = i < response.Rho.Count ? response.Rho[i] : 0,
                    ImpliedVol = i < response.ImpliedVol.Count ? response.ImpliedVol[i] : 0,
                    UnderlyingPrice = i < response.UnderlyingPrice.Count ? response.UnderlyingPrice[i] : 0
                });
            }
            return result;
        }

        public async Task<List<SpotQuoteRow>> GetStockQuotesAsync(
            string symbol,
            DateOnly startDate,
            DateOnly endDate,
            string interval)
        {
            string url = $"{_baseUrl}/stock/history/quote?symbol={symbol}&start_date={FormatDate(startDate)}&end_date={FormatDate(endDate)}&interval={interval}&format=json";
            StockQuoteResponse? response = await GetJsonAsync<StockQuoteResponse>(url);
            if (response == null)
            {
                return new List<SpotQuoteRow>();
            }
            List<SpotQuoteRow> result = new List<SpotQuoteRow>();
            int count = response.Timestamp.Count;
            for (int i = 0; i < count; i++)
            {
                result.Add(new SpotQuoteRow
                {
                    TimestampUtc = MarketClock.ParseThetaTimestampAsUtc(response.Timestamp[i]),
                    Bid = i < response.Bid.Count ? response.Bid[i] : 0,
                    Ask = i < response.Ask.Count ? response.Ask[i] : 0
                });
            }
            return result;
        }

        public async Task<List<(DateOnly Day, double Close)>> GetStockEodAsync(
            string symbol,
            DateOnly startDate,
            DateOnly endDate)
        {
            string url = $"{_baseUrl}/stock/history/eod?symbol={symbol}&start_date={FormatDate(startDate)}&end_date={FormatDate(endDate)}&format=json";
            StockEodResponse? response = await GetJsonAsync<StockEodResponse>(url);
            if (response == null)
            {
                return new List<(DateOnly, double)>();
            }
            List<(DateOnly Day, double Close)> result = new List<(DateOnly Day, double Close)>();
            int count = response.Created.Count;
            for (int i = 0; i < count; i++)
            {
                if (DateOnly.TryParse(response.Created[i], out DateOnly day))
                {
                    double close = i < response.Close.Count ? response.Close[i] : 0;
                    result.Add((day, close));
                }
            }
            result.Sort((a, b) => a.Day.CompareTo(b.Day));
            return result;
        }

        public async Task<List<(DateOnly Day, double Rate)>> GetInterestRateEodAsync(            string symbol,
            DateOnly startDate,
            DateOnly endDate)
        {
            string url = $"{_baseUrl}/interest_rate/history/eod?symbol={symbol}&start_date={FormatDate(startDate)}&end_date={FormatDate(endDate)}&format=json";
            InterestRateResponse? response = await GetJsonAsync<InterestRateResponse>(url);
            if (response == null)
            {
                return new List<(DateOnly, double)>();
            }
            List<(DateOnly, double)> result = new List<(DateOnly, double)>();
            int count = response.Created.Count;
            for (int i = 0; i < count; i++)
            {
                if (DateOnly.TryParse(response.Created[i], out DateOnly day))
                {
                    double rate = i < response.Rate.Count ? response.Rate[i] : 0;
                    result.Add((day, rate));
                }
            }
            return result;
        }

        public async Task<List<(DateOnly Date, string Type)>> GetYearHolidaysAsync(int year)
        {
            string url = $"{_baseUrl}/calendar/year_holidays?year={year}&format=json";
            CalendarResponse? response = await GetJsonAsync<CalendarResponse>(url);
            if (response == null)
            {
                return new List<(DateOnly, string)>();
            }
            List<(DateOnly, string)> result = new List<(DateOnly, string)>();
            int count = response.Date.Count;
            for (int i = 0; i < count; i++)
            {
                if (DateOnly.TryParse(response.Date[i], out DateOnly day))
                {
                    string type = i < response.Type.Count ? response.Type[i] : string.Empty;
                    result.Add((day, type));
                }
            }
            return result;
        }

        private async Task<T?> GetJsonAsync<T>(string url) where T : class
        {
            try
            {
                HttpResponseMessage httpResponse = await _httpClient.GetAsync(url);
                string body = await httpResponse.Content.ReadAsStringAsync();
                if (!httpResponse.IsSuccessStatusCode)
                {
                    Log.Warning("ThetaData request failed ({Status}): {Url} -> {Body}", httpResponse.StatusCode, url, body);
                    return null;
                }
                return JsonSerializer.Deserialize<T>(body, _jsonOptions);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ThetaData request error: {Url}", url);
                return null;
            }
        }

        private string FormatDate(DateOnly date)
        {
            return date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }
    }
}
