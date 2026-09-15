using System.Text.Json.Serialization;

namespace TestOptionStrategy.Server.Domain.ThetaData
{
    public class OptionExpirationsResponse
    {
        [JsonPropertyName("symbol")]
        public List<string> Symbol { get; set; } = new List<string>();

        [JsonPropertyName("expiration")]
        public List<string> Expiration { get; set; } = new List<string>();
    }

    public class OptionStrikesResponse
    {
        [JsonPropertyName("symbol")]
        public List<string> Symbol { get; set; } = new List<string>();

        [JsonPropertyName("strike")]
        public List<double> Strike { get; set; } = new List<double>();
    }

    public class OptionGreeksFirstOrderResponse
    {
        [JsonPropertyName("symbol")]
        public List<string> Symbol { get; set; } = new List<string>();

        [JsonPropertyName("expiration")]
        public List<string> Expiration { get; set; } = new List<string>();

        [JsonPropertyName("strike")]
        public List<double> Strike { get; set; } = new List<double>();

        [JsonPropertyName("right")]
        public List<string> Right { get; set; } = new List<string>();

        [JsonPropertyName("timestamp")]
        public List<string> Timestamp { get; set; } = new List<string>();

        [JsonPropertyName("bid")]
        public List<double> Bid { get; set; } = new List<double>();

        [JsonPropertyName("ask")]
        public List<double> Ask { get; set; } = new List<double>();

        [JsonPropertyName("delta")]
        public List<double> Delta { get; set; } = new List<double>();

        [JsonPropertyName("theta")]
        public List<double> Theta { get; set; } = new List<double>();

        [JsonPropertyName("vega")]
        public List<double> Vega { get; set; } = new List<double>();

        [JsonPropertyName("rho")]
        public List<double> Rho { get; set; } = new List<double>();

        [JsonPropertyName("epsilon")]
        public List<double> Epsilon { get; set; } = new List<double>();

        [JsonPropertyName("lambda")]
        public List<double> Lambda { get; set; } = new List<double>();

        [JsonPropertyName("implied_vol")]
        public List<double> ImpliedVol { get; set; } = new List<double>();

        [JsonPropertyName("iv_error")]
        public List<double> IvError { get; set; } = new List<double>();

        [JsonPropertyName("underlying_timestamp")]
        public List<string> UnderlyingTimestamp { get; set; } = new List<string>();

        [JsonPropertyName("underlying_price")]
        public List<double> UnderlyingPrice { get; set; } = new List<double>();
    }

    public class StockQuoteResponse
    {
        [JsonPropertyName("timestamp")]
        public List<string> Timestamp { get; set; } = new List<string>();

        [JsonPropertyName("bid")]
        public List<double> Bid { get; set; } = new List<double>();

        [JsonPropertyName("ask")]
        public List<double> Ask { get; set; } = new List<double>();
    }

    public class StockEodResponse
    {
        [JsonPropertyName("created")]
        public List<string> Created { get; set; } = new List<string>();

        [JsonPropertyName("close")]
        public List<double> Close { get; set; } = new List<double>();

        [JsonPropertyName("open")]
        public List<double> Open { get; set; } = new List<double>();

        [JsonPropertyName("high")]
        public List<double> High { get; set; } = new List<double>();

        [JsonPropertyName("low")]
        public List<double> Low { get; set; } = new List<double>();
    }

    public class InterestRateResponse
    {
        [JsonPropertyName("rate")]
        public List<double> Rate { get; set; } = new List<double>();

        [JsonPropertyName("created")]
        public List<string> Created { get; set; } = new List<string>();
    }

    public class CalendarResponse
    {
        [JsonPropertyName("date")]
        public List<string> Date { get; set; } = new List<string>();

        [JsonPropertyName("type")]
        public List<string> Type { get; set; } = new List<string>();
    }

    public class OptionGreeksRow
    {
        public string Symbol = string.Empty;
        public DateOnly Expiration;
        public double Strike;
        public string Right = string.Empty;
        public DateTime TimestampUtc;
        public double Bid;
        public double Ask;
        public double Delta;
        public double Theta;
        public double Vega;
        public double Rho;
        public double ImpliedVol;
        public double UnderlyingPrice;
    }

    public class SpotQuoteRow
    {
        public DateTime TimestampUtc;
        public double Bid;
        public double Ask;

        public double Mid()
        {
            return (Bid + Ask) / 2.0;
        }
    }
}
