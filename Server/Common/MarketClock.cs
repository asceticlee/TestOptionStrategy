namespace TestOptionStrategy.Server.Common
{
    public static class MarketClock
    {
        public const string UsTimeZoneId = "America/New_York";

        private static readonly TimeZoneInfo UsTimeZone = TimeZoneInfo.FindSystemTimeZoneById(UsTimeZoneId);

        public static TimeZoneInfo GetUsTimeZone()
        {
            return UsTimeZone;
        }

        public static DateTime UtcNowInUsEastern()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, UsTimeZone);
        }

        public static int GetMarketTradingDay()
        {
            DateTime eastern = UtcNowInUsEastern();
            return eastern.Year * 10000 + eastern.Month * 100 + eastern.Day;
        }

        public static int DateOnlyToTradingDay(DateOnly date)
        {
            return date.Year * 10000 + date.Month * 100 + date.Day;
        }

        public static DateOnly TradingDayToDateOnly(int tradingDay)
        {
            int year = tradingDay / 10000;
            int month = (tradingDay % 10000) / 100;
            int day = tradingDay % 100;
            return new DateOnly(year, month, day);
        }

        public static DateTime ParseThetaTimestampAsUtc(string timestamp)
        {
            DateTime eastern = DateTime.Parse(timestamp);
            DateTime unspecified = DateTime.SpecifyKind(eastern, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, UsTimeZone);
        }

        public static bool IsWeekend(DateOnly date)
        {
            return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
        }
    }
}
