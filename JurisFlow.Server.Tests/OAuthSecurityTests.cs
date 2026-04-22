using System.Net;
using System.Net.Http.Json;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class OAuthSecurityTests : IClassFixture<TestApplicationFactory>
{
    private readonly HttpClient _client;

    public OAuthSecurityTests(TestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ZoomOAuthRequiresAuthorizationCode()
    {
        var request = CreateRequest(HttpMethod.Post, "/api/zoom/oauth", "admin-user", "Admin", new
        {
            code = "",
            state = "not-used"
        });

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Authorization code is required", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GoogleRefreshUsesServerStoredSecret()
    {
        var request = CreateRequest(HttpMethod.Post, "/api/google/oauth/refresh", "admin-user", "Admin", new
        {
            target = "gmail",
            refreshToken = "legacy-browser-refresh-token"
        });

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("requires reconnect", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OAuthStateRejectsUnsupportedTarget()
    {
        var request = CreateRequest(HttpMethod.Post, "/api/oauth/state", "admin-user", "Admin", new
        {
            provider = "google",
            target = "drive-wide-open",
            returnPath = "/#documents"
        });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string userId, string role, object payload)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        request.Content = JsonContent.Create(payload);
        return request;
    }
}
