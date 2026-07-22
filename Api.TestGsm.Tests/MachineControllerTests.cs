using Api.TestGsm.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Api.TestGsm.Tests;

public sealed class MachineControllerTests
{
    [Fact]
    public void Get_ReturnsOnlyMachineName()
    {
        var controller = new MachineController();

        var result = Assert.IsType<OkObjectResult>(controller.Get());
        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));

        Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal(Environment.MachineName, json.RootElement.GetProperty("machineName").GetString());
        
    }
}
