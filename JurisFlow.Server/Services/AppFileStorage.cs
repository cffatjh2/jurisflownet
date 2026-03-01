using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JurisFlow.Server.Services
{
    public interface IAppFileStorage
    {
        string Provider { get; }
        string NormalizeRelativePath(string relativePath);
        Task SaveBytesAsync(string relativePath, byte[] content, string? contentType = null, CancellationToken cancellationToken = default);
        Task<byte[]> ReadBytesAsync(string relativePath, CancellationToken cancellationToken = default);
        Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default);
        Task DeleteIfExistsAsync(string relativePath, CancellationToken cancellationToken = default);
        Task CopyAsync(string sourceRelativePath, string destinationRelativePath, string? contentType = null, CancellationToken cancellationToken = default);
    }

    public sealed class AppFileStorage : IAppFileStorage
    {
        private const string LocalProvider = "local";
        private const string SupabaseProvider = "supabase";

        private readonly string _provider;
        private readonly string _contentRootPath;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AppFileStorage> _logger;
        private readonly string? _supabaseUrl;
        private readonly string? _supabaseServiceRoleKey;
        private readonly string? _supabaseBucket;
        private readonly SemaphoreSlim _bucketLock = new(1, 1);
        private bool _bucketEnsured;

        public AppFileStorage(
            IConfiguration configuration,
            IWebHostEnvironment env,
            IHttpClientFactory httpClientFactory,
            ILogger<AppFileStorage> logger)
        {
            _provider = (configuration["Storage:Provider"] ?? LocalProvider).Trim().ToLowerInvariant();
            if (!string.Equals(_provider, LocalProvider, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_provider, SupabaseProvider, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Storage:Provider must be either 'local' or 'supabase'.");
            }

            _contentRootPath = Path.GetFullPath(env.ContentRootPath);
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            if (string.Equals(_provider, SupabaseProvider, StringComparison.OrdinalIgnoreCase))
            {
                _supabaseUrl = (configuration["Storage:Supabase:Url"] ?? configuration["Supabase:Url"])?.Trim().TrimEnd('/');
                _supabaseServiceRoleKey = (configuration["Storage:Supabase:ServiceRoleKey"] ?? configuration["Supabase:ServiceRoleKey"])?.Trim();
                _supabaseBucket = (configuration["Storage:Supabase:Bucket"] ?? "jurisflow-files").Trim();

                if (string.IsNullOrWhiteSpace(_supabaseUrl))
                {
                    throw new InvalidOperationException("Storage:Supabase:Url is required when Storage:Provider is 'supabase'.");
                }

                if (string.IsNullOrWhiteSpace(_supabaseServiceRoleKey))
                {
                    throw new InvalidOperationException("Storage:Supabase:ServiceRoleKey is required when Storage:Provider is 'supabase'.");
                }

                if (string.IsNullOrWhiteSpace(_supabaseBucket))
                {
                    throw new InvalidOperationException("Storage:Supabase:Bucket is required when Storage:Provider is 'supabase'.");
                }
            }
        }

        public string Provider => _provider;

        public string NormalizeRelativePath(string relativePath)
        {
            var normalized = (relativePath ?? string.Empty)
                .Trim()
                .Replace('\\', '/')
                .TrimStart('/');

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvalidOperationException("File path is required.");
            }

            foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == "." || segment == "..")
                {
                    throw new InvalidOperationException("Invalid file path.");
                }
            }

            return normalized;
        }

        public async Task SaveBytesAsync(string relativePath, byte[] content, string? contentType = null, CancellationToken cancellationToken = default)
        {
            var normalizedPath = NormalizeRelativePath(relativePath);
            if (string.Equals(_provider, LocalProvider, StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = ResolveLocalFullPath(normalizedPath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
                return;
            }

            await EnsureSupabaseBucketAsync(cancellationToken);

            var client = CreateSupabaseClient();
            var exists = await ExistsInSupabaseAsync(normalizedPath, cancellationToken);
            var method = exists ? HttpMethod.Put : HttpMethod.Post;

            using var request = CreateSupabaseRequest(method, BuildObjectRoute("object", normalizedPath));
            request.Content = BuildBinaryContent(content, contentType);

            using var response = await client.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, $"store object '{normalizedPath}' in Supabase");
        }

        public async Task<byte[]> ReadBytesAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            var normalizedPath = NormalizeRelativePath(relativePath);
            if (string.Equals(_provider, LocalProvider, StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = ResolveLocalFullPath(normalizedPath);
                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException("File not found.", normalizedPath);
                }

                return await File.ReadAllBytesAsync(fullPath, cancellationToken);
            }

            await EnsureSupabaseBucketAsync(cancellationToken);

            var client = CreateSupabaseClient();
            using var request = CreateSupabaseRequest(HttpMethod.Get, BuildObjectRoute("object/authenticated", normalizedPath));
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException("File not found.", normalizedPath);
            }

            await EnsureSuccessAsync(response, $"read object '{normalizedPath}' from Supabase");
            return await response.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        public async Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            var normalizedPath = NormalizeRelativePath(relativePath);
            if (string.Equals(_provider, LocalProvider, StringComparison.OrdinalIgnoreCase))
            {
                return File.Exists(ResolveLocalFullPath(normalizedPath));
            }

            await EnsureSupabaseBucketAsync(cancellationToken);
            return await ExistsInSupabaseAsync(normalizedPath, cancellationToken);
        }

        public async Task DeleteIfExistsAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            var normalizedPath = NormalizeRelativePath(relativePath);
            if (string.Equals(_provider, LocalProvider, StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = ResolveLocalFullPath(normalizedPath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                return;
            }

            await EnsureSupabaseBucketAsync(cancellationToken);

            var client = CreateSupabaseClient();
            using var request = CreateSupabaseRequest(HttpMethod.Delete, BuildObjectRoute("object", normalizedPath));
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            await EnsureSuccessAsync(response, $"delete object '{normalizedPath}' from Supabase");
        }

        public async Task CopyAsync(string sourceRelativePath, string destinationRelativePath, string? contentType = null, CancellationToken cancellationToken = default)
        {
            var bytes = await ReadBytesAsync(sourceRelativePath, cancellationToken);
            await SaveBytesAsync(destinationRelativePath, bytes, contentType, cancellationToken);
        }

        private string ResolveLocalFullPath(string relativePath)
        {
            var localPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_contentRootPath, localPath));
            var rootWithSeparator = _contentRootPath.EndsWith(Path.DirectorySeparatorChar)
                ? _contentRootPath
                : _contentRootPath + Path.DirectorySeparatorChar;

            if (!string.Equals(fullPath, _contentRootPath, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Resolved file path is outside the application content root.");
            }

            return fullPath;
        }

        private async Task<bool> ExistsInSupabaseAsync(string normalizedPath, CancellationToken cancellationToken)
        {
            var client = CreateSupabaseClient();
            using var request = CreateSupabaseRequest(HttpMethod.Get, BuildObjectRoute("object/info", normalizedPath));
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            await EnsureSuccessAsync(response, $"check object '{normalizedPath}' in Supabase");
            return true;
        }

        private async Task EnsureSupabaseBucketAsync(CancellationToken cancellationToken)
        {
            if (_bucketEnsured || string.Equals(_provider, LocalProvider, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await _bucketLock.WaitAsync(cancellationToken);
            try
            {
                if (_bucketEnsured)
                {
                    return;
                }

                var client = CreateSupabaseClient();
                using var getRequest = CreateSupabaseRequest(HttpMethod.Get, $"storage/v1/bucket/{Uri.EscapeDataString(_supabaseBucket!)}");
                using var getResponse = await client.SendAsync(getRequest, cancellationToken);

                if (getResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    using var createRequest = CreateSupabaseRequest(HttpMethod.Post, "storage/v1/bucket");
                    createRequest.Content = new StringContent(
                        JsonSerializer.Serialize(new { id = _supabaseBucket, name = _supabaseBucket, @public = false }),
                        Encoding.UTF8,
                        "application/json");

                    using var createResponse = await client.SendAsync(createRequest, cancellationToken);
                    await EnsureSuccessAsync(createResponse, $"create Supabase storage bucket '{_supabaseBucket}'");
                    _logger.LogInformation("Created Supabase storage bucket {BucketName}.", _supabaseBucket);
                }
                else
                {
                    await EnsureSuccessAsync(getResponse, $"load Supabase storage bucket '{_supabaseBucket}'");
                }

                _bucketEnsured = true;
            }
            finally
            {
                _bucketLock.Release();
            }
        }

        private HttpClient CreateSupabaseClient()
        {
            if (string.IsNullOrWhiteSpace(_supabaseUrl))
            {
                throw new InvalidOperationException("Supabase storage is not configured.");
            }

            var client = _httpClientFactory.CreateClient(nameof(AppFileStorage));
            client.BaseAddress = new Uri(_supabaseUrl.EndsWith('/') ? _supabaseUrl : _supabaseUrl + "/");
            return client;
        }

        private HttpRequestMessage CreateSupabaseRequest(HttpMethod method, string relativeUri)
        {
            var request = new HttpRequestMessage(method, relativeUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _supabaseServiceRoleKey);
            request.Headers.TryAddWithoutValidation("apikey", _supabaseServiceRoleKey);
            return request;
        }

        private string BuildObjectRoute(string routePrefix, string normalizedPath)
        {
            var escapedSegments = string.Join(
                "/",
                normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));

            return $"storage/v1/{routePrefix}/{Uri.EscapeDataString(_supabaseBucket!)}/{escapedSegments}";
        }

        private static ByteArrayContent BuildBinaryContent(byte[] content, string? contentType)
        {
            var body = new ByteArrayContent(content);
            if (!string.IsNullOrWhiteSpace(contentType) &&
                MediaTypeHeaderValue.TryParse(contentType, out var parsedContentType))
            {
                body.Headers.ContentType = parsedContentType;
            }
            else
            {
                body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            }

            return body;
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var body = response.Content == null
                ? string.Empty
                : await response.Content.ReadAsStringAsync();
            var message = string.IsNullOrWhiteSpace(body)
                ? $"{operation} failed with status {(int)response.StatusCode} ({response.ReasonPhrase})."
                : $"{operation} failed with status {(int)response.StatusCode} ({response.ReasonPhrase}): {body}";

            throw new InvalidOperationException(message);
        }
    }
}
