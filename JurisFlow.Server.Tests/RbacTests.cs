using System.Net;
using Xunit;

namespace JurisFlow.Server.Tests;

public class RbacTests : IClassFixture<TestApplicationFactory>
{
    private readonly HttpClient _client;

    public RbacTests(TestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task AdminEndpointsRequireAdminRole()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        request.Headers.Add("X-Test-UserId", "admin-1");
        request.Headers.Add("X-Test-Role", "Admin");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpointsRejectNonAdmin()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/users");
        request.Headers.Add("X-Test-UserId", "user-1");
        request.Headers.Add("X-Test-Role", "Associate");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousRequestsAreUnauthorized()
    {
        var response = await _client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedUsersCanAccessGeneralEndpoints()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/clients");
        request.Headers.Add("X-Test-UserId", "user-2");
        request.Headers.Add("X-Test-Role", "Partner");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/sms")]
    [InlineData("/api/emails")]
    [InlineData("/api/conflicts/history")]
    [InlineData("/api/ai/research")]
    public async Task StaffOnlyEndpointsRejectClientRole(string endpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Add("X-Test-UserId", "client-1");
        request.Headers.Add("X-Test-Role", "Client");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/sms")]
    [InlineData("/api/emails")]
    [InlineData("/api/conflicts/history")]
    [InlineData("/api/ai/research")]
    public async Task StaffOnlyEndpointsRequireAuthentication(string endpoint)
    {
        var response = await _client.GetAsync(endpoint);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
