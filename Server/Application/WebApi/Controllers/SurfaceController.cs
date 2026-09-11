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
    }
}
