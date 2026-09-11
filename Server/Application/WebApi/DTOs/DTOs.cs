namespace TestOptionStrategy.Server.Application.WebApi.DTOs
{
    public class OptionLegRequest
    {
        public string Right { get; set; } = "call";
        public double Strike { get; set; }
        public string Expiration { get; set; } = string.Empty;
        public int Contracts { get; set; }
    }

    public class SurfaceRequest
    {
        public string Symbol { get; set; } = "SPY";
        public string SnapshotDate { get; set; } = string.Empty;
        public string SnapshotTime { get; set; } = string.Empty;
        public int SpotShares { get; set; }
        public int TimeStepMinutes { get; set; } = 30;
        public int SpotSamples { get; set; } = 101;
        public double SpotRangePercent { get; set; } = 5;
        public List<OptionLegRequest> Legs { get; set; } = new List<OptionLegRequest>();
    }

    public class OptionLegResult
    {
        public string Right { get; set; } = string.Empty;
        public double Strike { get; set; }
        public string Expiration { get; set; } = string.Empty;
        public int Contracts { get; set; }
        public double ImpliedVol { get; set; }
        public double EntryPrice { get; set; }
    }

    public class SpotLinePoint
    {
        public double X { get; set; }
        public string Y { get; set; } = string.Empty;
        public double Z { get; set; }
    }

    public class SurfaceResponse
    {
        public List<double> SpotPrices { get; set; } = new List<double>();
        public List<string> Timestamps { get; set; } = new List<string>();
        public List<List<double>> ValueSurface { get; set; } = new List<List<double>>();
        public List<List<double>> DeltaSurface { get; set; } = new List<List<double>>();
        public List<List<double>> GammaSurface { get; set; } = new List<List<double>>();
        public List<SpotLinePoint> SpotLineValue { get; set; } = new List<SpotLinePoint>();
        public List<SpotLinePoint> SpotLineDelta { get; set; } = new List<SpotLinePoint>();
        public List<SpotLinePoint> SpotLineGamma { get; set; } = new List<SpotLinePoint>();
        public double SnapshotSpot { get; set; }
        public double RiskFreeRate { get; set; }
        public List<OptionLegResult> Legs { get; set; } = new List<OptionLegResult>();
    }
}
