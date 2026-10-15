using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace KapeldinerService.Tests.Api;

public sealed class TestControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public TestControllerTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTestEndpointReturnsWorkingMessage()
    {
        HttpResponseMessage response = await _client.GetAsync(new Uri("/api/test", UriKind.Relative));
        var body = await response.Content.ReadFromJsonAsync<TestResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Test controller is working", body?.Message);
    }

    private sealed record TestResponse(string Message);
}
