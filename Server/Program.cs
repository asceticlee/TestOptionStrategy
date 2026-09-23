using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TestOptionStrategy.Server.Common;
using TestOptionStrategy.Server.Application.Services;
using TestOptionStrategy.Server.Domain.Data;
using TestOptionStrategy.Server.Domain.ThetaData;
using TestOptionStrategy.Server.Domain.Treasury;

namespace TestOptionStrategy.Server;

public class Program
{
    public static int Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();

        IConfiguration configuration = builder.Configuration;
        AppSettings.Initialize(configuration);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger);

        builder.Services.AddSingleton<Database>();
        builder.Services.AddSingleton<UnderlyingRepository>();
        builder.Services.AddSingleton<OptionContractRepository>();
        builder.Services.AddSingleton<SpotQuoteRepository>();
        builder.Services.AddSingleton<OptionGreeksRepository>();
        builder.Services.AddSingleton<RiskFreeRateRepository>();
        builder.Services.AddSingleton<ThetaDataClient>();
        builder.Services.AddSingleton<USTreasuryRate>();
        builder.Services.AddSingleton<MarketDataService>();
        builder.Services.AddSingleton<OptionSurfaceService>();
        builder.Services.AddSingleton<DataLoaderService>();

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowedCors", policy =>
            {
                policy.AllowAnyOrigin()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });

        WebApplication app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors("AllowedCors");
        app.UseAuthorization();
        app.MapControllers();

        if (args.Contains("--update") || args.Contains("--backfill"))
        {
            return RunDataLoader(app.Services, args);
        }

        Log.Information("TestOptionStrategy server starting");

        app.Run();
        return 0;
    }

    private static int RunDataLoader(IServiceProvider services, string[] args)
    {
        DataLoaderService loader = services.GetRequiredService<DataLoaderService>();
        string symbol = GetArg(args, "--symbol", "SPY");
        string interval = GetArg(args, "--interval", "5m");
        int strikeRange = GetArgInt(args, "--strike-range", 0);
        int leadDays = GetArgInt(args, "--lead-days", 60);
        int? strikeRangeValue = strikeRange > 0 ? strikeRange : null;

        if (args.Contains("--update"))
        {
            loader.UpdateAsync(symbol, interval, strikeRangeValue, leadDays).GetAwaiter().GetResult();
            return 0;
        }

        string fromText = GetArg(args, "--from", "");
        string toText = GetArg(args, "--to", "");
        if (!DateOnly.TryParse(fromText, out DateOnly from))
        {
            Log.Error("--from must be a date in YYYY-MM-DD format.");
            return 1;
        }
        DateOnly to = DateOnly.TryParse(toText, out DateOnly parsedTo)
            ? parsedTo
            : DataLoaderService.LastCompletedTradingDay();

        loader.BackfillAsync(symbol, from, to, interval, strikeRangeValue, leadDays).GetAwaiter().GetResult();
        return 0;
    }

    private static string GetArg(string[] args, string name, string fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }
        return fallback;
    }

    private static int GetArgInt(string[] args, string name, int fallback)
    {
        string value = GetArg(args, name, "");
        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }
}
