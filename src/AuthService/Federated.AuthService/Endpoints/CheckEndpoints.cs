using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Federated.AuthService.Data;
using Federated.Common;

namespace Federated.AuthService.Endpoints;

public record CheckRequest(
    [property: JsonPropertyName("client_id")] string? ClientId,
    [property: JsonPropertyName("clientId")] string? ClientIdAlt,
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("audience")] string? Audience,
    [property: JsonPropertyName("apiAudience")] string? ApiAudienceAlt,
    [property: JsonPropertyName("route")] string Route,
    [property: JsonPropertyName("method")] string Method);

public record CheckResponse(
    bool Allowed,
    string Reason,
    string ClientId,
    string RequiredScope,
    IEnumerable<string> ClientScopes);

public static class CheckEndpoints
{
    public static IEndpointRouteBuilder MapCheckEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/check", async (
            CheckRequest req,
            AuthDbContext db,
            HttpContext httpContext,
            ILogger<Program> logger) =>
        {
            string correlationId = httpContext.Items[CommonConstants.CorrelationIdHeader]?.ToString()
                ?? httpContext.Request.Headers[CommonConstants.CorrelationIdHeader].ToString();

            if (string.IsNullOrWhiteSpace(correlationId))
            {
                correlationId = Guid.NewGuid().ToString("N");
            }

            logger.LogInformation("[CorrelationId: {CorrelationId}] Evaluating central authorization check for Route: {Route}, Method: {Method}",
                correlationId, req.Route, req.Method);

            string? effectiveClientId = req.ClientId ?? req.ClientIdAlt;

            // If token provided, decode client_id from token
            if (string.IsNullOrWhiteSpace(effectiveClientId) && !string.IsNullOrWhiteSpace(req.Token))
            {
                try
                {
                    var handler = new JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(req.Token.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim());
                    effectiveClientId = jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value
                                     ?? jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("[CorrelationId: {CorrelationId}] Failed to parse provided token in /check: {Message}", correlationId, ex.Message);
                    return Results.Ok(new CheckResponse(
                        Allowed: false,
                        Reason: "Invalid or malformed Bearer token.",
                        ClientId: "unknown",
                        RequiredScope: "unknown",
                        ClientScopes: Array.Empty<string>()));
                }
            }

            if (string.IsNullOrWhiteSpace(effectiveClientId))
            {
                return Results.Ok(new CheckResponse(
                    Allowed: false,
                    Reason: "No client identification provided (missing client_id or token).",
                    ClientId: "unknown",
                    RequiredScope: "unknown",
                    ClientScopes: Array.Empty<string>()));
            }

            // Lookup client
            var client = await db.Clients
                .Include(c => c.Grants)
                    .ThenInclude(g => g.Permission)
                        .ThenInclude(p => p!.Api)
                .FirstOrDefaultAsync(c => c.ClientId == effectiveClientId);

            if (client == null)
            {
                logger.LogWarning("[CorrelationId: {CorrelationId}] Check denied: client '{ClientId}' not found.", correlationId, effectiveClientId);
                return Results.Ok(new CheckResponse(
                    Allowed: false,
                    Reason: $"Client '{effectiveClientId}' does not exist in central registry.",
                    ClientId: effectiveClientId,
                    RequiredScope: "unknown",
                    ClientScopes: Array.Empty<string>()));
            }

            if (!client.IsActive)
            {
                logger.LogWarning("[CorrelationId: {CorrelationId}] Check denied: client '{ClientId}' is disabled.", correlationId, effectiveClientId);
                return Results.Ok(new CheckResponse(
                    Allowed: false,
                    Reason: $"Client '{effectiveClientId}' is currently disabled.",
                    ClientId: effectiveClientId,
                    RequiredScope: "unknown",
                    ClientScopes: Array.Empty<string>()));
            }

            string targetAudience = req.Audience ?? req.ApiAudienceAlt ?? string.Empty;
            string requiredScope = DetermineRequiredScope(targetAudience, req.Route, req.Method);

            var activeScopes = client.Grants
                .Where(g => g.Permission != null && g.Permission.IsActive && g.Permission.Api != null && g.Permission.Api.IsActive)
                .Select(g => g.Permission!.Scope)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requiredScope == "none")
            {
                return Results.Ok(new CheckResponse(
                    Allowed: true,
                    Reason: "Route does not require authorization (public / health check).",
                    ClientId: effectiveClientId,
                    RequiredScope: "none",
                    ClientScopes: activeScopes));
            }

            bool isGranted = activeScopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase);

            if (!isGranted)
            {
                logger.LogWarning("[CorrelationId: {CorrelationId}] Check denied: client '{ClientId}' lacks scope '{Scope}'. Active scopes: [{ActiveScopes}]",
                    correlationId, effectiveClientId, requiredScope, string.Join(", ", activeScopes));

                return Results.Ok(new CheckResponse(
                    Allowed: false,
                    Reason: $"Client lacks required scope '{requiredScope}'. Active scopes: [{string.Join(", ", activeScopes)}].",
                    ClientId: effectiveClientId,
                    RequiredScope: requiredScope,
                    ClientScopes: activeScopes));
            }

            logger.LogInformation("[CorrelationId: {CorrelationId}] Check allowed: client '{ClientId}' possesses required scope '{Scope}'.",
                correlationId, effectiveClientId, requiredScope);

            return Results.Ok(new CheckResponse(
                Allowed: true,
                Reason: $"Access allowed. Client has active grant for '{requiredScope}'.",
                ClientId: effectiveClientId,
                RequiredScope: requiredScope,
                ClientScopes: activeScopes));
        });

        return app;
    }

    private static string DetermineRequiredScope(string audience, string route, string method)
    {
        string normRoute = route.Trim().ToLowerInvariant();
        string normMethod = method.Trim().ToUpperInvariant();

        if (normRoute == "/health") return "none";

        if (audience.Contains("orders") || normRoute.StartsWith("/orders"))
        {
            if (normMethod == "GET") return "orders.read";
            if (normMethod == "POST" || normMethod == "PUT" || normMethod == "DELETE") return "orders.write";
        }
        else if (audience.Contains("customers") || normRoute.StartsWith("/customers"))
        {
            if (normMethod == "GET") return "customers.read";
        }
        else if (audience.Contains("inventory") || normRoute.StartsWith("/inventory"))
        {
            if (normMethod == "GET") return "inventory.read";
            if (normMethod == "PUT" || normMethod == "POST") return "inventory.write";
        }

        return $"{normRoute.TrimStart('/').Split('/')[0]}.read";
    }
}
