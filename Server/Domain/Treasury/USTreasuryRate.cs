using System.Xml.Linq;
using Serilog;

namespace TestOptionStrategy.Server.Domain.Treasury
{
    public class USTreasuryRate
    {
        private readonly HttpClient _httpClient;

        public USTreasuryRate()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public async Task<List<(int TradingDay, double Yield)>> Get10YearsTreasuryYieldCurveAsync(
            int fromTradingDay,
            int toTradingDay)
        {
            string baseUrl = "https://home.treasury.gov/resource-center/data-chart-center/interest-rates/pages/xmlview?data=daily_treasury_yield_curve&field_tdr_date_value=";
            int fromYear = fromTradingDay / 10000;
            int toYear = toTradingDay / 10000;
            List<(int, double)> yields = new List<(int, double)>();

            for (int year = fromYear; year <= toYear; year++)
            {
                string fullUrl = $"{baseUrl}{year}";
                string xmlData = await FetchXmlAsync(fullUrl);
                if (string.IsNullOrEmpty(xmlData))
                {
                    continue;
                }
                XDocument xmlDoc = XDocument.Parse(xmlData);
                XNamespace atom = "http://www.w3.org/2005/Atom";
                XNamespace m = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
                XNamespace d = "http://schemas.microsoft.com/ado/2007/08/dataservices";
                IEnumerable<XElement> entries = xmlDoc.Descendants(atom + "entry");
                foreach (XElement entry in entries)
                {
                    XElement? content = entry.Element(atom + "content");
                    if (content == null)
                    {
                        continue;
                    }
                    XElement? properties = content.Element(m + "properties");
                    if (properties == null)
                    {
                        continue;
                    }
                    XElement? dateElement = properties.Element(d + "NEW_DATE");
                    XElement? yieldElement = properties.Element(d + "BC_10YEAR");
                    if (dateElement == null || yieldElement == null)
                    {
                        continue;
                    }
                    if (DateTime.TryParse(dateElement.Value, out DateTime date) && double.TryParse(yieldElement.Value, out double yield))
                    {
                        int tradingDay = date.Year * 10000 + date.Month * 100 + date.Day;
                        yields.Add((tradingDay, yield));
                    }
                }
            }

            return yields.Where(y => y.Item1 >= fromTradingDay && y.Item1 <= toTradingDay).ToList();
        }

        private async Task<string> FetchXmlAsync(string url)
        {
            try
            {
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "USTreasuryRate fetch failed: {Url}", url);
                return string.Empty;
            }
        }
    }
}
