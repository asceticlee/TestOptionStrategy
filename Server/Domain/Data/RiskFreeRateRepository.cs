using System.Text;
using Dapper;
using Npgsql;

namespace TestOptionStrategy.Server.Domain.Data
{
    public class RiskFreeRateRepository
    {
        private const int BatchSize = 500;

        private readonly Database _database;

        public RiskFreeRateRepository(Database database)
        {
            _database = database;
        }

        public async Task<int> UpsertManyAsync(List<RiskFreeRateEntity> rates)
        {
            int affected = 0;
            using NpgsqlConnection connection = _database.OpenConnection();
            using NpgsqlTransaction transaction = connection.BeginTransaction();
            for (int offset = 0; offset < rates.Count; offset += BatchSize)
            {
                int count = Math.Min(BatchSize, rates.Count - offset);
                StringBuilder sql = new StringBuilder(256 + count * 40);
                sql.AppendLine("insert into risk_free_rate (day, rate) values");
                for (int i = 0; i < count; i++)
                {
                    RiskFreeRateEntity item = rates[offset + i];
                    sql.Append('(')
                       .Append(SqlLiterals.Date(item.Day)).Append(',')
                       .Append(SqlLiterals.Decimal(item.Rate)).Append(')');
                    if (i < count - 1)
                    {
                        sql.Append(',');
                    }
                    sql.AppendLine();
                }
                sql.AppendLine("on conflict (day) do update set rate = excluded.rate;");
                affected += await connection.ExecuteAsync(sql.ToString(), transaction: transaction);
            }
            transaction.Commit();
            return affected;
        }

        public async Task<RiskFreeRateEntity?> GetLatestOnOrBeforeAsync(DateOnly day)
        {
            DateTime dayParam = day.ToDateTime(TimeOnly.MinValue);
            using NpgsqlConnection connection = _database.OpenConnection();
            const string sql = @"
                select id, day, rate
                from risk_free_rate
                where day <= @Day
                order by day desc
                limit 1;";
            RiskFreeRateEntity? row = await connection.QueryFirstOrDefaultAsync<RiskFreeRateEntity>(sql, new { Day = dayParam });
            return row;
        }
    }
}
