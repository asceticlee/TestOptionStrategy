using Serilog;
using TestOptionStrategy.Server.Common;
using TestOptionStrategy.Server.Application.Services;
using TestOptionStrategy.Server.Domain.Data;
using TestOptionStrategy.Server.Domain.ThetaData;
using TestOptionStrategy.Server.Domain.Treasury;

namespace TestOptionStrategy.Server;

public class Program
{
    public static void Main(string[] args)
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

        Log.Information("TestOptionStrategy server starting");

        app.Run();
    }
}
