using System.Text;
using Dapper;
using Npgsql;

namespace TestOptionStrategy.Server.Domain.Data
{
    public class OptionContractRepository
    {
        private const int BatchSize = 500;

        private readonly Database _database;

        public OptionContractRepository(Database database)
        {
            _database = database;
        }

        public async Task<int> UpsertManyAsync(List<OptionContractEntity> contracts)
        {
            int affected = 0;
            using NpgsqlConnection connection = _database.OpenConnection();
            using NpgsqlTransaction transaction = connection.BeginTransaction();
            for (int offset = 0; offset < contracts.Count; offset += BatchSize)
            {
                int count = Math.Min(BatchSize, contracts.Count - offset);
                StringBuilder sql = new StringBuilder(256 + count * 40);
                sql.AppendLine("insert into option_contract (symbol, expiration, strike, opt_right) values");
                for (int i = 0; i < count; i++)
                {
                    OptionContractEntity item = contracts[offset + i];
                    sql.Append('(')
                       .Append(SqlLiterals.String(item.Symbol)).Append(',')
                       .Append(SqlLiterals.Date(item.Expiration)).Append(',')
                       .Append(SqlLiterals.Decimal(item.Strike)).Append(',')
                       .Append(SqlLiterals.String(item.Right)).Append(')');
                    if (i < count - 1)
                    {
                        sql.Append(',');
                    }
                    sql.AppendLine();
                }
                sql.AppendLine("on conflict (symbol, expiration, strike, opt_right) do nothing;");
                affected += await connection.ExecuteAsync(sql.ToString(), transaction: transaction);
            }
            transaction.Commit();
            return affected;
        }

        public async Task<List<OptionContractEntity>> ListAsync(string symbol, DateOnly expiration)
        {
            DateTime expirationParam = expiration.ToDateTime(TimeOnly.MinValue);
            using NpgsqlConnection connection = _database.OpenConnection();
            const string sql = @"
                select id, symbol, expiration, strike, opt_right as right
                from option_contract
                where symbol = @Symbol and expiration = @Expiration
                order by strike, opt_right;";
            IEnumerable<OptionContractEntity> rows = await connection.QueryAsync<OptionContractEntity>(sql, new { Symbol = symbol, Expiration = expirationParam });
            return rows.ToList();
        }
    }
}
