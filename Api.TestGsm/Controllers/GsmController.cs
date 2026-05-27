using Microsoft.AspNetCore.Mvc;
using Api.TestGsm.Services;

namespace Api.TestGsm.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GsmController : Controller
    {
        private readonly IGsmService _gsm;
        public GsmController(IGsmService gsm)
        {
            _gsm = gsm;
        }

        [HttpPost("init")]
        public IActionResult Init()
        {
            _gsm.TestBenchInitialization();
            return Ok();
        }

        [HttpPost("init-4g")]
        public IActionResult Init4G()
        {
            try
            {
                _gsm.FourGModuleInitialization();
                return Ok(new { success = true, message = "4G module initialized successfully" });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("time out"))
            {
                return StatusCode(504, new { success = false, error = "Modem timeout", details = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = "Initialization failed", details = ex.Message });
            }
        }

        public record CallRequest(string phoneNumber, bool audioOk);
        [HttpPost("call")]
        public IActionResult Call([FromBody] CallRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.phoneNumber)) return BadRequest("phoneNumber required");
            _gsm.LaunchTest(req.phoneNumber, req.audioOk);
            return Ok();
        }

        [HttpPost("hangup")]
        public IActionResult Hangup()
        {
            _gsm.HangUpCall();
            return Ok();
        }

        [HttpGet("health")] // simple health endpoint for client
        public IActionResult Health()
        {
            var modem = _gsm.PingModem();
            var port = _gsm.IsPortOpen();
            
            return Ok(new { api = true, portOpen = port, modemOk = modem });
           
        }
    }
}
