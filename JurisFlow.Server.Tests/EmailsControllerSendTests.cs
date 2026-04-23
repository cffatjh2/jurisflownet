using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class EmailsControllerSendTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public EmailsControllerSendTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SendEmail_UsesSelectedGmailMailboxAndPersistsSentMessage()
    {
        var handler = new QueueingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { id = "gmail-sent-1" })
            });

        await using var isolatedFactory = CreateFactory(handler);
        using var scope = isolatedFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        db.EmailAccounts.Add(new EmailAccount
        {
            Id = "acct-gmail-1",
            UserId = "staff-user-send",
            Provider = "Gmail",
            EmailAddress = "staff@example.com",
            DisplayName = "Staff Sender",
            AccessToken = "gmail-access-token",
            TokenExpiresAt = DateTime.UtcNow.AddHours(1),
            IsActive = true,
            SyncEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var client = isolatedFactory.CreateClient();
        var request = CreateRequest(HttpMethod.Post, "/api/emails/send", "staff-user-send", "Associate");
        request.Content = JsonContent.Create(new
        {
            toAddress = "client@example.com",
            subject = "Case update",
            bodyText = "Your filing was submitted.",
            emailAccountId = "acct-gmail-1"
        });

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Email sent", body, StringComparison.OrdinalIgnoreCase);

        var sentEmail = db.EmailMessages.Single(message => message.EmailAccountId == "acct-gmail-1");
        Assert.Equal("Sent", sentEmail.Folder);
        Assert.Equal("staff@example.com", sentEmail.FromAddress);
        Assert.Equal("client@example.com", sentEmail.ToAddresses);
        Assert.Equal("gmail-sent-1", sentEmail.ExternalId);

        Assert.Single(handler.Requests);
        var providerRequest = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, providerRequest.Method);
        Assert.Equal("https://gmail.googleapis.com/gmail/v1/users/me/messages/send", providerRequest.RequestUri?.ToString());
        Assert.Equal("Bearer", providerRequest.Headers.Authorization?.Scheme);
        Assert.Equal("gmail-access-token", providerRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SendEmail_RequiresMailboxSelection_WhenMultipleMailboxesExist()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        db.EmailAccounts.AddRange(
            new EmailAccount
            {
                Id = "acct-1",
                UserId = "staff-user-multi",
                Provider = "Gmail",
                EmailAddress = "first@example.com",
                AccessToken = "token-1",
                TokenExpiresAt = DateTime.UtcNow.AddHours(1),
                IsActive = true,
                SyncEnabled = true
            },
            new EmailAccount
            {
                Id = "acct-2",
                UserId = "staff-user-multi",
                Provider = "Outlook",
                EmailAddress = "second@example.com",
                AccessToken = "token-2",
                TokenExpiresAt = DateTime.UtcNow.AddHours(1),
                IsActive = true,
                SyncEnabled = true
            });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var request = CreateRequest(HttpMethod.Post, "/api/emails/send", "staff-user-multi", "Associate");
        request.Content = JsonContent.Create(new
        {
            toAddress = "client@example.com",
            subject = "Case update",
            bodyText = "Body"
        });

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Select a connected mailbox", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConnectGmail_UsesProviderProfile_WhenEmailIsOmitted()
    {
        var handler = new QueueingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    access_token = "gmail-access-token",
                    refresh_token = "gmail-refresh-token",
                    expires_in = 3600
                })
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    emailAddress = "staff@gmail.example"
                })
            });

        await using var isolatedFactory = CreateFactory(
            handler,
            new Dictionary<string, string?>
            {
                ["Integrations:Google:ClientId"] = "google-client-id",
                ["Integrations:Google:ClientSecret"] = "google-client-secret",
                ["Integrations:Google:RedirectUri"] = "https://app.example/auth/google/callback"
            });

        var client = isolatedFactory.CreateClient();
        var authorizationRequest = CreateRequest(HttpMethod.Get, "/api/emails/accounts/connect/gmail/authorization", "staff-user-connect", "Associate");
        var authorizationResponse = await client.SendAsync(authorizationRequest);
        var authorizationPayload = await authorizationResponse.Content.ReadFromJsonAsync<GmailAuthorizationResponse>();

        Assert.Equal(HttpStatusCode.OK, authorizationResponse.StatusCode);
        Assert.NotNull(authorizationPayload);

        var connectRequest = CreateRequest(HttpMethod.Post, "/api/emails/accounts/connect/gmail", "staff-user-connect", "Associate");
        connectRequest.Content = JsonContent.Create(new
        {
            authorizationCode = "auth-code",
            state = authorizationPayload!.State,
            codeVerifier = authorizationPayload.CodeVerifier,
            redirectUri = authorizationPayload.RedirectUri
        });

        var connectResponse = await client.SendAsync(connectRequest);
        var connectBody = await connectResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, connectResponse.StatusCode);
        Assert.Contains("connected", connectBody, StringComparison.OrdinalIgnoreCase);

        using var scope = isolatedFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var account = db.EmailAccounts.Single(a => a.UserId == "staff-user-connect" && a.Provider == "Gmail");
        Assert.Equal("staff@gmail.example", account.EmailAddress);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://oauth2.googleapis.com/token", handler.Requests[0].RequestUri?.ToString());
        Assert.Equal("https://gmail.googleapis.com/gmail/v1/users/me/profile", handler.Requests[1].RequestUri?.ToString());
    }

    private WebApplicationFactory<Program> CreateFactory(HttpMessageHandler handler, Dictionary<string, string?>? extraConfig = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            if (extraConfig != null)
            {
                builder.ConfigureAppConfiguration((context, config) => config.AddInMemoryCollection(extraConfig));
            }

            builder.ConfigureServices(services =>
            {
                var descriptors = services
                    .Where(descriptor => descriptor.ServiceType == typeof(IHttpClientFactory))
                    .ToList();

                foreach (var descriptor in descriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(handler));
            });
        });
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string userId, string role)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        return request;
    }

    private sealed class GmailAuthorizationResponse
    {
        public string State { get; set; } = string.Empty;
        public string CodeVerifier { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class QueueingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public QueueingHttpMessageHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(await CloneRequestAsync(request, cancellationToken));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP response is available for the request.");
            }

            return _responses.Dequeue();
        }

        private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content != null)
            {
                var content = await request.Content.ReadAsStringAsync(cancellationToken);
                clone.Content = new StringContent(content, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType);
                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }
}
