using System;
using System.Collections.Generic;
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
            ICheckTokenValidator tokenValidator,
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

            // The caller identity comes only from a verified token. client_id / X-Client-Id values are never trusted.
            if (string.IsNullOrWhiteSpace(req.Token))
            {
                return Results.Ok(new CheckResponse(
                    Allowed: false,
                    Reason: "A Bearer token is required.",
                    ClientId: "unknown",
                    RequiredScope: "unknown",
                    ClientScopes: Array.Empty<string>()));
            }

            var tokenResult = tokenValidator.Validate(req.Token, req.Audience ?? req.ApiAudienceAlt);
            if (!tokenResult.IsValid)
            {
                logger.LogWarning("[CorrelationId: {CorrelationId}] Rejected token in /check: {Error}", correlationId, tokenResult.Error);
                return Results.Ok(new CheckResponse(
                    Allowed: false,
                    Reason: tokenResult.Error ?? "Invalid token.",
                    ClientId: "unknown",
                    RequiredScope: "unknown",
                    ClientScopes: Array.Empty<string>()));
            }

            string effectiveClientId = tokenResult.ClientId!;
            string targetAudience = tokenResult.Audience ?? string.Empty;

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
