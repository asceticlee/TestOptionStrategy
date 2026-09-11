using Dapper;
using Npgsql;

namespace TestOptionStrategy.Server.Domain.Data
{
    public class UnderlyingRepository
    {
        private readonly Database _database;

        public UnderlyingRepository(Database database)
        {
            _database = database;
        }

        public async Task<int> UpsertAsync(string market, string symbol)
        {
            using NpgsqlConnection connection = _database.OpenConnection();
            const string sql = @"
                insert into underlying (market, symbol)
                values (@Market, @Symbol)
                on conflict (symbol) do update set market = excluded.market
                returning id;";
            int id = await connection.QuerySingleAsync<int>(sql, new { Market = market, Symbol = symbol });
            return id;
        }

        public async Task<List<string>> ListSymbolsAsync()
        {
            using NpgsqlConnection connection = _database.OpenConnection();
            const string sql = "select symbol from underlying order by symbol;";
            IEnumerable<string> symbols = await connection.QueryAsync<string>(sql);
            return symbols.ToList();
        }
    }
}
