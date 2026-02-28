namespace JurisFlow.Server.Middleware
{
    public class SecurityHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;

        public SecurityHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var headers = context.Response.Headers;

            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            headers.TryAdd("Permissions-Policy", "geolocation=(), microphone=(), camera=()");

            var csp = _configuration["Security:ContentSecurityPolicy"];
            if (!string.IsNullOrWhiteSpace(csp))
            {
                headers.TryAdd("Content-Security-Policy", csp);
            }

            await _next(context);
        }
    }
}
