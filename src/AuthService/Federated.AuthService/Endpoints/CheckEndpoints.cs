using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Federated.AuthService.Services;
using Federated.Common;

namespace Federated.AuthService.Endpoints;

public record CheckRequest(
    [property: JsonPropertyName("client_id")] string? ClientId,
    [property: JsonPropertyName("clientId")] string? ClientIdAlt,
    [property: JsonPropertyName("token")] string? Token,
    [property: JsonPropertyName("audience")] string? Audience,
    [property: JsonPropertyName("apiAudience")] string? ApiAudienceAlt,
    [property: JsonPropertyName("route")] string? Route,
    [property: JsonPropertyName("method")] string? Method);

public record CheckResponse(
    bool Allowed,
    string Reason,
    string ClientId,
    string RequiredScope,
    IEnumerable<string> ClientScopes,
    string? MatchedRoute = null);

public static class CheckEndpoints
{
    public static IEndpointRouteBuilder MapCheckEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/check", async (
            CheckRequest req,
            IRouteAccessEvaluator evaluator,
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
            string targetAudience = req.Audience ?? req.ApiAudienceAlt ?? string.Empty;

            // If a token was provided, decode client_id (and audience, if not given explicitly) from it
            if (!string.IsNullOrWhiteSpace(req.Token) &&
                (string.IsNullOrWhiteSpace(effectiveClientId) || string.IsNullOrWhiteSpace(targetAudience)))
            {
                try
                {
                    var handler = new JwtSecurityTokenHandler();
                    var jwt = handler.ReadJwtToken(req.Token.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim());

                    if (string.IsNullOrWhiteSpace(effectiveClientId))
                    {
                        effectiveClientId = jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value
                                         ?? jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                    }

                    if (string.IsNullOrWhiteSpace(targetAudience) && jwt.Audiences.Count() == 1)
                    {
                        targetAudience = jwt.Audiences.First();
                    }
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

            if (string.IsNullOrWhiteSpace(req.Route) || string.IsNullOrWhiteSpace(req.Method))
            {
                return Results.Ok(new CheckResponse(
                    Allowed: false,
                    Reason: "Both 'route' and 'method' are required.",
                    ClientId: effectiveClientId,
                    RequiredScope: "unknown",
                    ClientScopes: Array.Empty<string>()));
            }

            var decision = await evaluator.EvaluateAsync(effectiveClientId, targetAudience, req.Route, req.Method);

            if (decision.Allowed)
            {
                logger.LogInformation("[CorrelationId: {CorrelationId}] Check allowed: client '{ClientId}' on {Method} {Route} via route '{MatchedRoute}' (scope '{Scope}').",
                    correlationId, effectiveClientId, req.Method, req.Route, decision.MatchedRoute, decision.RequiredScope);
            }
            else
            {
                logger.LogWarning("[CorrelationId: {CorrelationId}] Check denied: client '{ClientId}' on {Method} {Route}: {Reason}",
                    correlationId, effectiveClientId, req.Method, req.Route, decision.Reason);
            }

            return Results.Ok(new CheckResponse(
                decision.Allowed,
                decision.Reason,
                decision.ClientId,
                decision.RequiredScope,
                decision.ClientScopes,
                decision.MatchedRoute));
        });

        return app;
    }
}
