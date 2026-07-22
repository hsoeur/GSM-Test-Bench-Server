using Microsoft.AspNetCore.Mvc;

namespace Api.TestGsm.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class MachineController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok(new
        {
            machineName = Environment.MachineName
        });
    }
}
