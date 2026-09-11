using Npgsql;
using TestOptionStrategy.Server.Common;

namespace TestOptionStrategy.Server.Domain.Data
{
    public class Database
    {
        private readonly string _connectionString;

        public Database()
        {
            _connectionString = AppSettings.GetSetting("ConnectionStrings:Default");
        }

        public NpgsqlConnection OpenConnection()
        {
            NpgsqlConnection connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}
