using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using KapeldinerService.Web.Auth;

namespace KapeldinerService.Tests.Api;

public sealed class TestControllerTests : IClassFixture<TestControllerApiFactory>
{
    private readonly HttpClient _client;

    public TestControllerTests(TestControllerApiFactory factory)
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

public sealed class TestControllerApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            ServiceDescriptor? adminBootstrapper = services.FirstOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && Equals(descriptor.ImplementationType, typeof(AdminBootstrapper)));
            if (adminBootstrapper is not null)
                services.Remove(adminBootstrapper);
        });
        builder.UseEnvironment("Development");
    }
}
