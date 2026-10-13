using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Federated.AuthService.Services;

public static class AdminPolicies
{
    public const string Read = "admin:read";
    public const string Write = "admin:write";
}

public class AdminAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private static readonly JwtSecurityTokenHandler JwtHandler = new();

    public AdminAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? authHeader = Request.Headers.Authorization;
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string rawToken = authHeader["Bearer ".Length..].Trim();
        if (!JwtHandler.CanReadToken(rawToken))
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed JWT Bearer token."));
        }

        try
        {
            var jwt = JwtHandler.ReadJwtToken(rawToken);

            // Verify lifetime
            if (jwt.ValidTo < DateTime.UtcNow)
            {
                return Task.FromResult(AuthenticateResult.Fail("Token has expired."));
            }

            var claims = new List<Claim>(jwt.Claims);

            // Ensure NameIdentifier and Name are populated for actor tracking
            var subClaim = jwt.Claims.FirstOrDefault(c => c.Type is "sub" or ClaimTypes.NameIdentifier);
            var nameClaim = jwt.Claims.FirstOrDefault(c => c.Type is "email" or "name" or "preferred_username" or ClaimTypes.Name);

            if (subClaim != null && !claims.Any(c => c.Type == ClaimTypes.NameIdentifier))
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, subClaim.Value));
            }

            if (nameClaim != null && !claims.Any(c => c.Type == ClaimTypes.Name))
            {
                claims.Add(new Claim(ClaimTypes.Name, nameClaim.Value));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (Exception ex)
        {
            return Task.FromResult(AuthenticateResult.Fail(ex));
        }
    }
}
