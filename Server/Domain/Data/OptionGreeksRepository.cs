using System.Text;
using Dapper;
using Npgsql;

namespace TestOptionStrategy.Server.Domain.Data
{
    public class OptionGreeksRepository
    {
        private const int BatchSize = 500;

        private readonly Database _database;

        public OptionGreeksRepository(Database database)
        {
            _database = database;
        }

        public async Task<int> UpsertManyAsync(List<OptionGreeksEntity> rows)
        {
            int affected = 0;
            using NpgsqlConnection connection = _database.OpenConnection();
            using NpgsqlTransaction transaction = connection.BeginTransaction();
            for (int offset = 0; offset < rows.Count; offset += BatchSize)
            {
                int count = Math.Min(BatchSize, rows.Count - offset);
                StringBuilder sql = new StringBuilder(256 + count * 80);
                sql.AppendLine("insert into option_greeks (symbol, expiration, strike, opt_right, ts, bid, ask, delta, theta, vega, rho, implied_vol, underlying_price) values");
                for (int i = 0; i < count; i++)
                {
                    OptionGreeksEntity item = rows[offset + i];
                    sql.Append('(')
                       .Append(SqlLiterals.String(item.Symbol)).Append(',')
                       .Append(SqlLiterals.Date(item.Expiration)).Append(',')
                       .Append(SqlLiterals.Decimal(item.Strike)).Append(',')
                       .Append(SqlLiterals.String(item.Right)).Append(',')
                       .Append(SqlLiterals.TimestampUtc(item.TsUtc)).Append(',')
                       .Append(SqlLiterals.Decimal(item.Bid)).Append(',')
                       .Append(SqlLiterals.Decimal(item.Ask)).Append(',')
                       .Append(SqlLiterals.Decimal(item.Delta)).Append(',')
                       .Append(SqlLiterals.Decimal(item.Theta)).Append(',')
                       .Append(SqlLiterals.Decimal(item.Vega)).Append(',')
                       .Append(SqlLiterals.Decimal(item.Rho)).Append(',')
                       .Append(SqlLiterals.Decimal(item.ImpliedVol)).Append(',')
                       .Append(SqlLiterals.Decimal(item.UnderlyingPrice)).Append(')');
                    if (i < count - 1)
                    {
                        sql.Append(',');
                    }
                    sql.AppendLine();
                }
                sql.AppendLine("on conflict (symbol, expiration, strike, opt_right, ts) do update set implied_vol = excluded.implied_vol;");
                affected += await connection.ExecuteAsync(sql.ToString(), transaction: transaction);
            }
            transaction.Commit();
            return affected;
        }

        public async Task<DateTime?> GetMaxTsUtcAsync(string symbol)
        {
            using NpgsqlConnection connection = _database.OpenConnection();
            const string sql = "select max(ts) from option_greeks where symbol = @Symbol;";
            DateTime? max = await connection.QuerySingleOrDefaultAsync<DateTime?>(sql, new { Symbol = symbol });
            return max;
        }

        public async Task<List<OptionGreeksEntity>> ListAsync(
            string symbol,
            DateOnly expiration,
            decimal strike,
            string right,
            DateTime fromUtc,
            DateTime toUtc)
        {
            DateTime expirationParam = expiration.ToDateTime(TimeOnly.MinValue);
            using NpgsqlConnection connection = _database.OpenConnection();
            const string sql = @"
                select id, symbol, expiration, strike, opt_right as right, ts as tsutc, bid, ask, delta, theta, vega, rho, implied_vol as impliedvol, underlying_price as underlyingprice
                from option_greeks
                where symbol = @Symbol and expiration = @Expiration and strike = @Strike and opt_right = @Right and ts >= @FromUtc and ts <= @ToUtc
                order by ts;";
            IEnumerable<OptionGreeksEntity> rows = await connection.QueryAsync<OptionGreeksEntity>(sql, new
            {
                Symbol = symbol,
                Expiration = expirationParam,
                Strike = strike,
                Right = right,
                FromUtc = fromUtc,
                ToUtc = toUtc
            });
            return rows.ToList();
        }
    }
}
