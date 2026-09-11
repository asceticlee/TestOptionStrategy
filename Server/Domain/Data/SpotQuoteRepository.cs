using System.Text;
using Dapper;
using Npgsql;

namespace TestOptionStrategy.Server.Domain.Data
{
    public class SpotQuoteRepository
    {
        private const int BatchSize = 1000;

        private readonly Database _database;

        public SpotQuoteRepository(Database database)
        {
            _database = database;
        }

        public async Task<int> UpsertManyAsync(List<SpotQuoteEntity> quotes)
        {
            int affected = 0;
            using NpgsqlConnection connection = _database.OpenConnection();
            using NpgsqlTransaction transaction = connection.BeginTransaction();
            for (int offset = 0; offset < quotes.Count; offset += BatchSize)
            {
                int count = Math.Min(BatchSize, quotes.Count - offset);
                StringBuilder sql = new StringBuilder(256 + count * 40);
                sql.AppendLine("insert into spot_quote (symbol, ts, bid, ask) values");
                for (int i = 0; i < count; i++)
                {
                    SpotQuoteEntity item = quotes[offset + i];
                    sql.Append('(')
                       .Append(SqlLiterals.String(item.Symbol)).Append(',')
                       .Append(SqlLiterals.TimestampUtc(item.TsUtc)).Append(',')
                       .Append(SqlLiterals.Decimal(item.Bid)).Append(',')
                       .Append(SqlLiterals.Decimal(item.Ask)).Append(')');
                    if (i < count - 1)
                    {
                        sql.Append(',');
                    }
                    sql.AppendLine();
                }
                sql.AppendLine("on conflict (symbol, ts) do nothing;");
                affected += await connection.ExecuteAsync(sql.ToString(), transaction: transaction);
            }
            transaction.Commit();
            return affected;
        }

        public async Task<List<SpotQuoteEntity>> ListAsync(string symbol, DateTime fromUtc, DateTime toUtc)
        {
            using NpgsqlConnection connection = _database.OpenConnection();
            const string sql = @"
                select id, symbol, ts as tsutc, bid, ask
                from spot_quote
                where symbol = @Symbol and ts >= @FromUtc and ts <= @ToUtc
                order by ts;";
            IEnumerable<SpotQuoteEntity> rows = await connection.QueryAsync<SpotQuoteEntity>(sql, new { Symbol = symbol, FromUtc = fromUtc, ToUtc = toUtc });
            return rows.ToList();
        }
    }
}
