using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Federated.Common;

/// <summary>Explicit settings for <see cref="CentralCheckMiddleware"/> (override configuration).</summary>
public class CentralCheckOptions
{
    /// <summary>The audience (registered API) this service represents in the central route registry.</summary>
    public string? Audience { get; set; }
}

public class CentralCheckMiddleware
{
    private readonly RequestDelegate _next;
    private readonly HttpClient _httpClient;
    private readonly string _checkUrl;
    private readonly string _audience;
    private readonly ILogger<CentralCheckMiddleware> _logger;

    public CentralCheckMiddleware(
        RequestDelegate next,
        IConfiguration config,
        ILogger<CentralCheckMiddleware> logger,
        CentralCheckOptions options,
        IHttpClientFactory? httpClientFactory = null)
    {
        _next = next;
        _logger = logger;
        _httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();

        string authority = config["Authentication:Authority"] ?? "http://localhost:5001";
        _checkUrl = $"{authority.TrimEnd('/')}/check";
        _audience = !string.IsNullOrWhiteSpace(options.Audience)
            ? options.Audience
            : config["Authentication:Audience"] ?? string.Empty;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string path = context.Request.Path.Value ?? "";
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        string authHeader = context.Request.Headers.Authorization.ToString();
        string clientIdHeader = context.Request.Headers["X-Client-Id"].ToString();

        if (string.IsNullOrWhiteSpace(authHeader) && string.IsNullOrWhiteSpace(clientIdHeader))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "unauthorized",
                message = "Missing Authorization Bearer token."
            });
            return;
        }

        string correlationId = context.Items[CommonConstants.CorrelationIdHeader]?.ToString()
            ?? context.Request.Headers[CommonConstants.CorrelationIdHeader].ToString();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        // Forward authorization check to central AuthService
        var checkPayload = new
        {
            clientId = string.IsNullOrWhiteSpace(clientIdHeader) ? null : clientIdHeader,
            token = authHeader,
            audience = _audience,
            route = path,
            method = context.Request.Method
        };

        var requestMsg = new HttpRequestMessage(HttpMethod.Post, _checkUrl)
        {
            Content = JsonContent.Create(checkPayload)
        };
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            requestMsg.Headers.Add(CommonConstants.CorrelationIdHeader, correlationId);
        }

        HttpResponseMessage checkResp;
        try
        {
            checkResp = await _httpClient.SendAsync(requestMsg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to central AuthService at {CheckUrl}", _checkUrl);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "auth_service_unavailable",
                message = "Central authorization service could not be reached."
            });
            return;
        }

        var result = await checkResp.Content.ReadFromJsonAsync<CheckResponseDto>();
        if (result == null || !result.Allowed)
        {
            _logger.LogWarning("[CorrelationId: {CorrelationId}] Central check DENIED for {Method} {Path}: {Reason}",
                correlationId, context.Request.Method, path, result?.Reason ?? "Unknown");

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "forbidden",
                reason = result?.Reason ?? "Access denied by central authorization policy.",
                requiredScope = result?.RequiredScope
            });
            return;
        }

        _logger.LogInformation("[CorrelationId: {CorrelationId}] Central check ALLOWED for client {ClientId}: {Reason}",
            correlationId, result.ClientId, result.Reason);

        // Bind user claims principal for downstream handler
        var claims = new List<Claim>
        {
            new("client_id", result.ClientId),
            new(ClaimTypes.NameIdentifier, result.ClientId)
        };
        if (result.ClientScopes != null)
        {
            claims.Add(new Claim("scope", string.Join(" ", result.ClientScopes)));
        }
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "CentralCheck"));

        await _next(context);
    }
}

public record CheckResponseDto(
    bool Allowed,
    string Reason,
    string ClientId,
    string RequiredScope,
        IEnumerable<string> ClientScopes,
    string? MatchedRoute = null);

public static class CentralCheckMiddlewareExtensions
{
        /// <param name="audience">
    /// Audience of the API being protected, as registered in the AuthService route registry.
    /// Falls back to the "Authentication:Audience" configuration value when omitted.
    /// </param>
    public static IApplicationBuilder UseCentralCheck(this IApplicationBuilder app, string? audience = null)
    {
        return app.UseMiddleware<CentralCheckMiddleware>(new CentralCheckOptions { Audience = audience });
    }
}
