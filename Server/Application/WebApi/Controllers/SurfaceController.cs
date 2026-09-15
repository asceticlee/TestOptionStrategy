using Microsoft.AspNetCore.Mvc;
using TestOptionStrategy.Server.Application.Services;
using TestOptionStrategy.Server.Application.WebApi.DTOs;

namespace TestOptionStrategy.Server.Application.WebApi.Controllers
{
    [ApiController]
    [Route("api/surface")]
    public class SurfaceController : ControllerBase
    {
        private readonly OptionSurfaceService _surfaceService;

        public SurfaceController(OptionSurfaceService surfaceService)
        {
            _surfaceService = surfaceService;
        }

        [HttpPost]
        public async Task<IActionResult> Compute([FromBody] SurfaceRequest request)
        {
            try
            {
                SurfaceResponse response = await _surfaceService.ComputeAsync(request);
                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("stats")]
        public async Task<IActionResult> ComputeStats([FromBody] SurfaceRequest request)
        {
            try
            {
                StatsResponse response = await _surfaceService.ComputeStatsAsync(request);
                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("leg-greeks")]
        public async Task<IActionResult> ComputeLegGreeks([FromBody] LegGreeksRequest request)
        {
            try
            {
                StatsLegResult response = await _surfaceService.ComputeLegGreeksAsync(request);
                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("real")]
        public async Task<IActionResult> ComputeReal([FromBody] SurfaceRequest request)
        {
            try
            {
                SurfaceResponse response = await _surfaceService.ComputeRealAsync(request);
                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
