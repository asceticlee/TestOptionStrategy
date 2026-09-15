using Microsoft.AspNetCore.Mvc;
using TestOptionStrategy.Server.Application.Services;
using TestOptionStrategy.Server.Application.WebApi.DTOs;
using TestOptionStrategy.Server.Common;

namespace TestOptionStrategy.Server.Application.WebApi.Controllers
{
    [ApiController]
    [Route("api/market")]
    public class MarketDataController : ControllerBase
    {
        private readonly MarketDataService _marketData;

        public MarketDataController(MarketDataService marketData)
        {
            _marketData = marketData;
        }

        [HttpGet("underlyings")]
        public async Task<IActionResult> GetUnderlyings()
        {
            List<string> symbols = await _marketData.GetUnderlyingsAsync();
            return Ok(symbols);
        }

        [HttpGet("expirations")]
        public async Task<IActionResult> GetExpirations([FromQuery] string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest("symbol is required");
            }
            List<DateOnly> expirations = await _marketData.GetExpirationsAsync(symbol);
            return Ok(expirations.Select(d => d.ToString("yyyy-MM-dd")).ToList());
        }

        [HttpGet("strikes")]
        public async Task<IActionResult> GetStrikes([FromQuery] string symbol, [FromQuery] string expiration)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest("symbol is required");
            }
            if (!DateOnly.TryParse(expiration, out DateOnly expirationDate))
            {
                return BadRequest("expiration must be YYYY-MM-DD");
            }
            List<double> strikes = await _marketData.GetStrikesAsync(symbol, expirationDate);
            return Ok(strikes);
        }

        [HttpGet("spot")]
        public async Task<IActionResult> GetSpot([FromQuery] string symbol, [FromQuery] string date, [FromQuery] string time)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest("symbol is required");
            }
            if (!DateOnly.TryParse(date, out DateOnly day) || !TimeSpan.TryParse(time, out TimeSpan timeOfDay))
            {
                return BadRequest("date must be YYYY-MM-DD and time HH:mm");
            }
            TimeZoneInfo timeZone = MarketClock.GetUsTimeZone();
            DateTime eastern = new DateTime(day.Year, day.Month, day.Day, timeOfDay.Hours, timeOfDay.Minutes, timeOfDay.Seconds);
            DateTime snapshotUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(eastern, DateTimeKind.Unspecified), timeZone);
            double spot = await _marketData.GetSpotAtSnapshotAsync(symbol, snapshotUtc);
            return Ok(spot);
        }

        [HttpGet("quote-summary")]
        public async Task<IActionResult> GetQuoteSummary([FromQuery] string symbol, [FromQuery] string date, [FromQuery] string time)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest("symbol is required");
            }
            if (!DateOnly.TryParse(date, out DateOnly day) || !TimeSpan.TryParse(time, out TimeSpan timeOfDay))
            {
                return BadRequest("date must be YYYY-MM-DD and time HH:mm");
            }
            TimeZoneInfo timeZone = MarketClock.GetUsTimeZone();
            DateTime eastern = new DateTime(day.Year, day.Month, day.Day, timeOfDay.Hours, timeOfDay.Minutes, timeOfDay.Seconds);
            DateTime snapshotUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(eastern, DateTimeKind.Unspecified), timeZone);
            (double spot, double previousClose) = await _marketData.GetQuoteSummaryAsync(symbol, snapshotUtc);
            return Ok(new QuoteSummaryDto { Spot = spot, PreviousClose = previousClose });
        }
    }
}
