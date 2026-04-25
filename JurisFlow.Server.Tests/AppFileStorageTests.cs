using System.Net;
using System.Text;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace JurisFlow.Server.Tests;

public class AppFileStorageTests
{
    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenSupabaseInfoEndpointWrapsNotFoundInBadRequest()
    {
        using var env = new FakeWebHostEnvironment();
        var handler = new QueueingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""{"id":"jurisflow-files"}""")
            },
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent("""{"statusCode":"404","error":"not_found","message":"Object not found"}""")
            });

        var storage = CreateStorage(env, handler);

        var exists = await storage.ExistsAsync("uploads/tenant-test-1/document.pdf");

        Assert.False(exists);
        Assert.Collection(
            handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("https://example.supabase.co/storage/v1/bucket/jurisflow-files", request.RequestUri?.ToString());
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("https://example.supabase.co/storage/v1/object/info/jurisflow-files/uploads/tenant-test-1/document.pdf", request.RequestUri?.ToString());
            });
    }

    [Fact]
    public async Task SaveBytesAsync_ContinuesUpload_WhenSupabaseInfoEndpointReturnsWrappedNotFound()
    {
        using var env = new FakeWebHostEnvironment();
        var handler = new QueueingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""{"id":"jurisflow-files"}""")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""{"Key":"uploads/tenant-test-1/document.pdf"}""")
            });

        var storage = CreateStorage(env, handler);

        await storage.SaveBytesAsync("uploads/tenant-test-1/document.pdf", Encoding.UTF8.GetBytes("hello"), "application/pdf");

        Assert.Equal(2, handler.Requests.Count);
        var uploadRequest = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, uploadRequest.Method);
        Assert.Equal("https://example.supabase.co/storage/v1/object/jurisflow-files/uploads/tenant-test-1/document.pdf", uploadRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task ReadBytesAsync_UsesPrivateObjectEndpointForSupabase()
    {
        using var env = new FakeWebHostEnvironment();
        var handler = new QueueingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""{"id":"jurisflow-files"}""")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"))
            });

        var storage = CreateStorage(env, handler);

        var bytes = await storage.ReadBytesAsync("uploads/tenant-test-1/document.pdf");

        Assert.Equal("hello", Encoding.UTF8.GetString(bytes));
        Assert.Collection(
            handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("https://example.supabase.co/storage/v1/bucket/jurisflow-files", request.RequestUri?.ToString());
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("https://example.supabase.co/storage/v1/object/jurisflow-files/uploads/tenant-test-1/document.pdf", request.RequestUri?.ToString());
            });
    }

    private static AppFileStorage CreateStorage(FakeWebHostEnvironment env, QueueingHttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = "supabase",
                ["Storage:Supabase:Url"] = "https://example.supabase.co",
                ["Storage:Supabase:ServiceRoleKey"] = "service-role-key",
                ["Storage:Supabase:Bucket"] = "jurisflow-files"
            })
            .Build();

        return new AppFileStorage(
            configuration,
            env,
            new FakeHttpClientFactory(handler),
            NullLogger<AppFileStorage>.Instance);
    }

    private static StringContent JsonContent(string body)
        => new(body, Encoding.UTF8, "application/json");

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP response is available for the request.");
            }

            return Task.FromResult(_responses.Dequeue());
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            return new HttpRequestMessage(request.Method, request.RequestUri);
        }
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment, IDisposable
    {
        private readonly string _contentRootPath = Path.Combine(Path.GetTempPath(), "jurisflow-appfilestorage-tests", Guid.NewGuid().ToString("N"));

        public FakeWebHostEnvironment()
        {
            Directory.CreateDirectory(_contentRootPath);
            WebRootPath = _contentRootPath;
            WebRootFileProvider = null!;
            ContentRootFileProvider = null!;
        }

        public string ApplicationName { get; set; } = "JurisFlow.Server.Tests";
        public IFileProvider WebRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath
        {
            get => _contentRootPath;
            set => throw new NotSupportedException();
        }

        public IFileProvider ContentRootFileProvider { get; set; }

        public void Dispose()
        {
            if (Directory.Exists(_contentRootPath))
            {
                Directory.Delete(_contentRootPath, recursive: true);
            }
        }
    }
}
