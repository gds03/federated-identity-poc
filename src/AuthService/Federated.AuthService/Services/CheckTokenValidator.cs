using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Microsoft.IdentityModel.Tokens;

namespace Federated.AuthService.Services;

public record CheckTokenResult(bool IsValid, string? Error, string? ClientId, string? Audience)
{
    public static CheckTokenResult Fail(string error) => new(false, error, null, null);
}

public interface ICheckTokenValidator
{
    /// <summary>
    /// Verifies the token was issued by this AuthService: RS256 signature, issuer and lifetime.
    /// When <paramref name="expectedAudience"/> is given the token must be addressed to it.
    /// </summary>
    CheckTokenResult Validate(string token, string? expectedAudience);
}

public class CheckTokenValidator : ICheckTokenValidator
{
    private readonly IRsaKeyService _rsaKeyService;
    private readonly string _issuer;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public CheckTokenValidator(IRsaKeyService rsaKeyService, string issuer)
    {
        _rsaKeyService = rsaKeyService;
        _issuer = issuer;
    }

    public CheckTokenResult Validate(string token, string? expectedAudience)
    {
        string raw = token.Trim();
        if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw["Bearer ".Length..].Trim();
        }

        bool checkAudience = !string.IsNullOrWhiteSpace(expectedAudience);
        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _rsaKeyService.GetSigningKey(),
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = checkAudience,
            ValidAudience = checkAudience ? expectedAudience : null,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        try
        {
            _handler.ValidateToken(raw, parameters, out var validated);
            var jwt = (JwtSecurityToken)validated;

            string? clientId = jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value;
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return CheckTokenResult.Fail("Token has no client_id claim.");
            }

            string? audience = checkAudience
                ? expectedAudience
                : jwt.Audiences.Count() == 1 ? jwt.Audiences.First() : null;

            return new CheckTokenResult(true, null, clientId, audience);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return CheckTokenResult.Fail($"Invalid token: {ex.GetType().Name}.");
        }
    }
}
