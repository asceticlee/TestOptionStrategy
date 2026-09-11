namespace TestOptionStrategy.Server.Common
{
    public class AppSettings
    {
        private static IConfiguration _configuration = null!;

        public static void Initialize(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public static string GetSetting(string key)
        {
            return _configuration[key] ?? string.Empty;
        }
    }
}
