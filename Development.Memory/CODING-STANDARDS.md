# Coding Standards

Owner-specific conventions. Follow these strictly in `Server/` and `Client/`.

## C# (Server)

1. **Never use `var`.** Always declare the concrete type. `List<int> x = new List<int>();` not
   `var x = ...`. This is a hard requirement from the owner.
2. **Dapper** for data access, **Npgsql** for PostgreSQL. No Entity Framework.
   **Bulk inserts use the owner's multi-row `VALUES` style** (see "Bulk insert convention" below).
3. **Serilog** for logging (Serilog.AspNetCore + console + rolling file). Use `Log.Information(...)`
   / `Log.Error(ex, ...)` static methods, same as `BidAskLast`.
4. Target framework `net8.0` (LTS; SDK 8.0.113 is installed). Keep it, even though newer SDKs exist.
5. **No code comments** unless the owner asks for them. Put explanations in `Development.Memory/` docs
   instead. Write self-documenting code.
6. Naming: PascalCase types/methods, `_camelCase` for private fields. Prefer explicit `readonly` and
   `public`/`private` modifiers (no implicit accessibility).
7. Config via `appsettings.json` read through `Common/AppSettings.cs` (mirrors BidAskLast).
8. JSON: use `System.Text.Json` with explicit `[JsonPropertyName("snake_case")]` attributes on DTOs
   that map to ThetaData responses. API controllers may return camelCase by default.

## NuGet packages (from the owner's other projects)

```xml
<PackageReference Include="Dapper" Version="2.1.66" />
<PackageReference Include="Npgsql" Version="9.0.4" />
<PackageReference Include="Serilog" Version="4.3.0" />
<PackageReference Include="Serilog.AspNetCore" Version="9.0.0" />
<PackageReference Include="Serilog.Settings.Configuration" Version="9.0.0" />
<PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
<PackageReference Include="Serilog.Sinks.RollingFile.Extension" Version="2.0.2" />
```

(Add `Swashbuckle.AspNetCore` for Swagger if desired, mirroring BidAskLast.)

## TypeScript / Next.js (Client)

1. Next.js App Router. Components under `Client/app/`.
2. Charts via **Plotly** (react-plotly.js or `plotly.js-dist-min` loaded as a script + dynamic import).
   The Angular reference used the global `Plotly` from a CDN `<script>`.
3. Keep heavy chart logic in a small number of components; mirror the Angular reference's structure:
   `prepareSpotAxisX`, `prepareTimeAxisY`, `prepareValue/Delta/GammaAxisZ`, `normalizeData`.
4. Use plain `fetch` against the WebAPI (no global state library for v1). The client calls the API
   **same-origin** (`/api/...`) — Next.js `rewrites()` in `next.config.mjs` proxies `/api/*` to the C#
   server (`API_ORIGIN`, default `http://localhost:5210`). This keeps the browser on `:3000` so only
   that port needs VS Code port-forwarding. `NEXT_PUBLIC_API_BASE` can override the origin if needed.
5. No `any` where avoidable; prefer typed interfaces for API DTOs mirroring the server.

## General

- Prefer explicit `public`/`private`. Keep methods small and single-purpose.

## Bulk insert convention (owner's style)

For multi-row inserts, build a single statement with a `StringBuilder`, chunk values into batches
(default 500–1000 rows), and run it via Dapper `ExecuteAsync(sql, transaction: transaction)`:

```csharp
insert into spot_quote (symbol, ts, bid, ask) values
('SPY','2026-09-04 13:30:00.000000+00',1.0,1.1),
('SPY','2026-09-04 13:31:00.000000+00',1.2,1.3)
on conflict (symbol, ts) do nothing;
```

Rules:
- Values are **inlined** (not `@` parameters). Use `Domain/Data/SqlLiterals.cs` helpers to escape:
  `String` (single-quote doubling), `Decimal` (invariant culture), `Date` (`yyyy-MM-dd`),
  `TimestampUtc` (`yyyy-MM-dd HH:mm:ss.ffffff+00` — explicit `+00` so `timestamptz` is unambiguous
  regardless of the DB session timezone).
- A `for` loop with a `BatchSize` const controls chunk size; open one connection + one transaction,
  run each chunk, commit once.

## Important gotchas (verified by failing, then fixed)

1. **Do NOT pass `DateOnly` to Dapper** (its list-expansion path mishandles it). Convert to
   `DateTime` (`dateOnly.ToDateTime(TimeOnly.MinValue)`) at the repository boundary; keep `DateOnly`
   only in the service/domain layer.
2. **Do NOT pass a `List<T>` of entities to Dapper for bulk insert** (its IL path corrupts the SQL
   when the entities use public fields). Bulk inserts use the multi-row `VALUES` style above; reads
   (`QueryAsync`) use anonymous-object parameters, which are fine.
3. Npgsql accepts both `@param` and `:param` placeholders with Dapper; `@param` is used throughout.
4. `right` is a PostgreSQL reserved word — the DB column is named `opt_right`.
- Dates/times: US market timezone is `America/New_York`. ThetaData timestamps are naive ET
  (`2026-09-01T09:30:00.000`) — treat them as `America/New_York` when converting to UTC.
