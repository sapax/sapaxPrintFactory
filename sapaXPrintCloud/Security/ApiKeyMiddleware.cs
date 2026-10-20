using Microsoft.Extensions.Configuration;
using sapaXPrintCloud.Services;

namespace sapaXPrintCloud.Security
{
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiKeyMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path.Value?.ToLower() ?? "";

            // Allow swagger, health, AND key generation
            if (
                path.Contains("swagger") ||
                path.Contains("health") ||
                path.StartsWith("/api/keys")
            )
            {
                await _next(context);
                return;
            }

            // Agent polling endpoint
            var headerName =
                path.Contains("/api/print-jobs/next") ||
                path.Contains("/api/print-jobs/zpl/next")
                ? "X-AGENT-KEY"
                : "X-API-KEY";

            if (!context.Request.Headers.TryGetValue(headerName, out var rawKey))
            {
                context.Response.StatusCode = 401;
                return;
            }

            if (!ApiKeyRepository.Validate(rawKey!))
            {
                context.Response.StatusCode = 403;
                return;
            }

            await _next(context);
        }
    }
    
}
