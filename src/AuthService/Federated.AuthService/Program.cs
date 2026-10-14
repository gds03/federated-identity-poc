using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Federated.Common;
using Federated.AuthService.Data;
using Federated.AuthService.Endpoints;
using Federated.AuthService.Models;
using Federated.AuthService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") 
                      ?? "Data Source=FederatedAuth.db"));

builder.Services.AddSingleton<ISecretHasher, SecretHasher>();
builder.Services.AddSingleton<IRsaKeyService, RsaKeyService>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IRouteAccessEvaluator, RouteAccessEvaluator>();

// CORS for local React Admin UI
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders(CommonConstants.CorrelationIdHeader);
    });
});

// Authentication & Authorization for Admin Endpoints
builder.Services.AddAuthentication("AdminBearer")
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AdminAuthHandler>("AdminBearer", null);

builder.Services.AddAuthorization(options =>
{
    // Read Policy: allows any authenticated user (viewers and admins)
    options.AddPolicy(AdminPolicies.Read, policy =>
    {
        policy.RequireAuthenticatedUser();
    });

    // Write Policy: strictly enforces 'access-admin' role/group or 'admin-access' scope
    options.AddPolicy(AdminPolicies.Write, policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(ctx =>
        {
            var user = ctx.User;
            return user.Claims.Any(c =>
                (c.Type is "roles" or "groups" or "role" or "https://federated.internal/roles"
                 && c.Value.Equals("access-admin", StringComparison.OrdinalIgnoreCase)) ||
                (c.Type == "scope" && c.Value.Split(' ').Contains("admin-access", StringComparer.OrdinalIgnoreCase)));
        });
    });
});

var app = builder.Build();

// Ensure DB is seeded
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<ISecretHasher>();
    await DbSeeder.SeedAsync(db, hasher);
}

// Middleware
app.UseCors();
app.UseTransactionId();
app.UseAuthentication();
app.UseAuthorization();

string issuer = builder.Configuration["AuthService:Issuer"] ?? "http://localhost:5001";

// 1. Health check
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "AuthService" }));

// 2. OpenID Discovery Document (RFC 8414)
app.MapGet("/.well-known/openid-configuration", () =>
{
    return Results.Ok(new
    {
        issuer,
        token_endpoint = $"{issuer}/oauth/token",
        jwks_uri = $"{issuer}/.well-known/jwks.json",
        response_types_supported = new[] { "token" },
        grant_types_supported = new[] { "client_credentials" },
        token_endpoint_auth_methods_supported = new[] { "client_secret_post" }
    });
});

// 3. JWKS Endpoint (RFC 7517)
app.MapGet("/.well-known/jwks.json", (IRsaKeyService rsaKeyService) =>
{
    return Results.Ok(rsaKeyService.GetJwks());
});

// 4. OAuth 2.0 Token Endpoint (RFC 6749 Section 4.4)
app.MapPost("/oauth/token", async (
    HttpContext httpContext,
    AuthDbContext db,
    ISecretHasher hasher,
    ITokenService tokenService,
    ILogger<Program> logger) =>
{
    string? grantType = null;
    string? clientId = null;
    string? clientSecret = null;
    string? requestedScope = null;

    if (httpContext.Request.HasFormContentType)
    {
        var form = await httpContext.Request.ReadFormAsync();
        grantType = form["grant_type"].ToString();
        clientId = form["client_id"].ToString();
        clientSecret = form["client_secret"].ToString();
        requestedScope = form["scope"].ToString();
    }
    else if (httpContext.Request.HasJsonContentType())
    {
        var json = await httpContext.Request.ReadFromJsonAsync<TokenRequestDto>();
        if (json != null)
        {
            grantType = json.grant_type;
            clientId = json.client_id;
            clientSecret = json.client_secret;
            requestedScope = json.scope;
        }
    }

    if (string.IsNullOrWhiteSpace(grantType) || grantType != "client_credentials")
    {
        return Results.BadRequest(new
        {
            error = "unsupported_grant_type",
            error_description = "Only 'client_credentials' grant_type is supported."
        });
    }

    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
    {
        return Results.BadRequest(new
        {
            error = "invalid_client",
            error_description = "client_id and client_secret are required."
        });
    }

    // Lookup client in DB
    var client = await db.Clients
        .Include(c => c.Grants)
            .ThenInclude(g => g.Permission)
                .ThenInclude(p => p!.Api)
        .FirstOrDefaultAsync(c => c.ClientId == clientId);

    if (client == null || !client.IsActive)
    {
        logger.LogWarning("Authentication failed for unknown or inactive client: {ClientId}", clientId);
        return Results.Json(new
        {
            error = "invalid_client",
            error_description = "Client credentials are invalid or client is disabled."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!hasher.VerifySecret(clientSecret, client.SecretHash))
    {
        logger.LogWarning("Authentication failed: invalid client secret for {ClientId}", clientId);
        return Results.Json(new
        {
            error = "invalid_client",
            error_description = "Client credentials are invalid."
        }, statusCode: StatusCodes.Status401Unauthorized);
    }

    // Resolve allowed permissions and audiences
    var activeGrants = client.Grants
        .Where(g => g.Permission != null && g.Permission.IsActive && g.Permission.Api != null && g.Permission.Api.IsActive)
        .ToList();

    var allowedScopes = activeGrants.Select(g => g.Permission!.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var allowedAudiences = activeGrants.Select(g => g.Permission!.Api!.Audience).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    List<string> grantedScopes;
    if (!string.IsNullOrWhiteSpace(requestedScope))
    {
        var requestedList = requestedScope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Verify requested scopes against allowed
        foreach (var req in requestedList)
        {
            if (!allowedScopes.Contains(req))
            {
                return Results.BadRequest(new
                {
                    error = "invalid_scope",
                    error_description = $"The requested scope '{req}' is not granted to this client."
                });
            }
        }
        grantedScopes = requestedList.ToList();
    }
    else
    {
        // If no scope requested, issue all granted scopes
        grantedScopes = allowedScopes.ToList();
    }

    // Generate JWT Access Token
    int expiresInSeconds = 3600;
    string accessToken = tokenService.IssueAccessToken(
        issuer: issuer,
        clientId: client.ClientId,
        audiences: allowedAudiences,
        scopes: grantedScopes,
        lifetime: TimeSpan.FromSeconds(expiresInSeconds));

    logger.LogInformation("Issued token for client {ClientId} with scopes: {Scopes}", clientId, string.Join(" ", grantedScopes));

    return Results.Ok(new
    {
        access_token = accessToken,
        token_type = "Bearer",
        expires_in = expiresInSeconds,
        scope = string.Join(" ", grantedScopes)
    });
});

app.MapAdminEndpoints();
app.MapMockOidcEndpoints();
app.MapCheckEndpoints();

app.Run();

public record TokenRequestDto(
    string? grant_type,
    string? client_id,
    string? client_secret,
    string? scope);
