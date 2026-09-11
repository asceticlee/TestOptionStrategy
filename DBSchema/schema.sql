create table if not exists underlying (
    id serial primary key,
    market text not null default 'us',
    symbol text not null,
    constraint uq_underlying_symbol unique (symbol)
);

create table if not exists option_contract (
    id bigserial primary key,
    symbol text not null,
    expiration date not null,
    strike numeric not null,
    opt_right text not null,
    constraint uq_option_contract unique (symbol, expiration, strike, opt_right)
);

create index if not exists ix_option_contract_symbol_expiration on option_contract (symbol, expiration);

create table if not exists spot_quote (
    id bigserial primary key,
    symbol text not null,
    ts timestamptz not null,
    bid numeric,
    ask numeric,
    constraint uq_spot_quote unique (symbol, ts)
);

create index if not exists ix_spot_quote_symbol_ts on spot_quote (symbol, ts);

create table if not exists option_greeks (
    id bigserial primary key,
    symbol text not null,
    expiration date not null,
    strike numeric not null,
    opt_right text not null,
    ts timestamptz not null,
    bid numeric,
    ask numeric,
    delta numeric,
    theta numeric,
    vega numeric,
    rho numeric,
    implied_vol numeric,
    underlying_price numeric,
    constraint uq_option_greeks unique (symbol, expiration, strike, opt_right, ts)
);

create index if not exists ix_option_greeks_symbol_expiration_ts on option_greeks (symbol, expiration, ts);

create table if not exists risk_free_rate (
    id serial primary key,
    day date not null,
    rate numeric not null,
    constraint uq_risk_free_rate_day unique (day)
);
