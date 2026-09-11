using System.Globalization;

namespace TestOptionStrategy.Server.Domain.Data
{
    public static class SqlLiterals
    {
        public static string String(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        public static string Decimal(decimal value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        public static string Date(DateTime value)
        {
            return "'" + value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'";
        }

        public static string TimestampUtc(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return "'" + utc.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "+00'";
        }
    }
}
