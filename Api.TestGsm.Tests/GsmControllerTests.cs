using Xunit;
using Moq;
using Api.TestGsm.Controllers;
using Api.TestGsm.Services;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace Api.TestGsm.Tests
{
    public class GsmControllerTests
    {
        private readonly Mock<IGsmService> _mockService;
        private readonly GsmController _controller;

        public GsmControllerTests()
        {
            _mockService = new Mock<IGsmService>();
            _controller = new GsmController(_mockService.Object);
        }

        [Fact]
        public void Init_ReturnsOk_AndCallsService()
        {
            var result = _controller.Init();
            Assert.IsType<OkResult>(result);
            _mockService.Verify(s => s.TestBenchInitialization(), Times.Once);
        }

        [Fact]
        public void Init4G_ReturnsOk_AndCallsService()
        {
            var result = _controller.Init4G();
            Assert.IsType<OkResult>(result);
            _mockService.Verify(s => s.FourGModuleInitialization(), Times.Once);
        }

        [Fact]
        public void Call_ReturnsBadRequest_WhenPhoneNumberMissing()
        {
            var req = new GsmController.CallRequest("", true);
            var result = _controller.Call(req);
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("phoneNumber required", badRequest.Value);
        }

        [Fact]
        public void Call_ReturnsOk_AndCallsService()
        {
            var req = new GsmController.CallRequest("12345", false);
            var result = _controller.Call(req);
            Assert.IsType<OkResult>(result);
            _mockService.Verify(s => s.LaunchTest("12345", false), Times.Once);
        }

        [Fact]
        public void Hangup_ReturnsOk_AndCallsService()
        {
            var result = _controller.Hangup();
            Assert.IsType<OkResult>(result);
            _mockService.Verify(s => s.HangUpCall(), Times.Once);
        }

        [Fact]
        public void Health_ReturnsOkWithStatus()
        {
            _mockService.Setup(s => s.IsPortOpen()).Returns(true);
            _mockService.Setup(s => s.PingModem()).Returns(false);

            var result = _controller.Health();
            var ok = Assert.IsType<OkObjectResult>(result);
            var dict = ok.Value as IDictionary<string, object>;

            if (dict != null)
            {
                Assert.True((bool)dict["api"]);
                Assert.True((bool)dict["portOfpen"]);
                Assert.False((bool)dict["modemOk"]);
            }
            else
            {
            // fallback for anonymous type
                var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                Assert.True(doc.RootElement.GetProperty("api").GetBoolean());
                Assert.True(doc.RootElement.GetProperty("portOpen").GetBoolean());
                Assert.False(doc.RootElement.GetProperty("modemOk").GetBoolean());
            }
        }
    }
}
