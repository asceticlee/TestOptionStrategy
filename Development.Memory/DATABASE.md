# Database

## Connection

- Host: `localhost`, port `54333`
- Database: `test_option_strategy`
- User: `test_option_strategy_dbo`, password `Abcd1234`

Created by `DBSchema/init.sql`:
```sql
create user test_option_strategy_dbo with password 'Abcd1234';
alter user test_option_strategy_dbo with createdb;
create database test_option_strategy owner=test_option_strategy_dbo;
```

Tables are defined in `DBSchema/schema.sql`.

## Tables

### `underlying`
| col | type |
|-----|------|
| id | serial PK |
| market | text (`us`) |
| symbol | text (unique, e.g. `SPY`) |

### `option_contract` (static chain)
| col | type |
|-----|------|
| id | bigserial PK |
| symbol | text |
| expiration | date |
| strike | numeric |
| right | text (`call` / `put`) |

unique `(symbol, expiration, strike, right)`.

### `spot_quote` (underlying intraday)
| col | type |
|-----|------|
| id | bigserial PK |
| symbol | text |
| ts | timestamptz |
| bid | numeric |
| ask | numeric |

unique `(symbol, ts)`.

### `option_greeks` (snapshot greeks + IV per leg/timestamp)
| col | type |
|-----|------|
| id | bigserial PK |
| symbol | text |
| expiration | date |
| strike | numeric |
| right | text |
| ts | timestamptz |
| bid | numeric |
| ask | numeric |
| delta | numeric |
| theta | numeric |
| vega | numeric |
| rho | numeric |
| implied_vol | numeric |
| underlying_price | numeric |

unique `(symbol, expiration, strike, right, ts)`.

### `risk_free_rate`
| col | type |
|-----|------|
| id | serial PK |
| day | date (unique) |
| rate | numeric (percent, e.g. 3.66) |

## Dapper conventions

- `Database.cs` builds an `NpgsqlConnection` from `ConnectionStrings:Default`.
- Repositories take the connection and run raw SQL with Dapper `Query`/`Execute`/`QueryFirstOrDefault`.
- **Bulk inserts** use multi-row `VALUES` with inline literals + `ON CONFLICT` (idempotent). See the
  "Bulk insert convention" section in `CODING-STANDARDS.md` and `Domain/Data/SqlLiterals.cs`.
- `numeric` maps to `decimal`; dates map to `DateTime` (convert from `DateOnly` at the repo boundary).
- `timestamptz` values are always stored as UTC (literal form `...+00`).
