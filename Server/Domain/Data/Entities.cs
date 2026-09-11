namespace TestOptionStrategy.Server.Domain.Data
{
    public class UnderlyingEntity
    {
        public int Id { get; set; }
        public string Market { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
    }

    public class OptionContractEntity
    {
        public long Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public DateTime Expiration { get; set; }
        public decimal Strike { get; set; }
        public string Right { get; set; } = string.Empty;
    }

    public class SpotQuoteEntity
    {
        public long Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public DateTime TsUtc { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
    }

    public class OptionGreeksEntity
    {
        public long Id { get; set; }
        public string Symbol { get; set; } = string.Empty;
        public DateTime Expiration { get; set; }
        public decimal Strike { get; set; }
        public string Right { get; set; } = string.Empty;
        public DateTime TsUtc { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public decimal Delta { get; set; }
        public decimal Theta { get; set; }
        public decimal Vega { get; set; }
        public decimal Rho { get; set; }
        public decimal ImpliedVol { get; set; }
        public decimal UnderlyingPrice { get; set; }
    }

    public class RiskFreeRateEntity
    {
        public int Id { get; set; }
        public DateTime Day { get; set; }
        public decimal Rate { get; set; }
    }
}
