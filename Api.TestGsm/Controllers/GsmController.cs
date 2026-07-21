using Api.TestGsm.Configuration;
using Api.TestGsm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Api.TestGsm.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GsmController : Controller
    {
        private readonly IGsmService _gsm;
        private readonly ApiResponseOptions _responses;

        public GsmController(IGsmService gsm, IOptions<ApiResponseOptions> responses)
        {
            _gsm = gsm;
            _responses = responses.Value;
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
                return Ok(new { success = true, message = RequiredResponse(_responses.Init4GSuccessMessage, "ApiResponses:Init4GSuccessMessage") });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(RequiredResponse(_responses.TimeoutDetectionText, "ApiResponses:TimeoutDetectionText")))
            {
                return StatusCode(504, new { success = false, error = RequiredResponse(_responses.ModemTimeoutError, "ApiResponses:ModemTimeoutError"), details = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, error = RequiredResponse(_responses.InitializationFailedError, "ApiResponses:InitializationFailedError"), details = ex.Message });
            }
        }

        public record CallRequest(string phoneNumber, bool audioOk);

        [HttpPost("call")]
        public IActionResult Call([FromBody] CallRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.phoneNumber))
            {
                return BadRequest(RequiredResponse(_responses.PhoneNumberRequiredMessage, "ApiResponses:PhoneNumberRequiredMessage"));
            }

            _gsm.LaunchTest(req.phoneNumber, req.audioOk);
            return Ok();
        }

        [HttpPost("hangup")]
        public IActionResult Hangup()
        {
            _gsm.HangUpCall();
            return Ok();
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            var modem = _gsm.PingModem();
            var port = _gsm.IsPortOpen();

            return Ok(new { api = true, portOpen = port, modemOk = modem });
        }

        private static string RequiredResponse(string? value, string key)
            => string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException($"Missing configuration value '{key}'.")
                : value;
    }
}
