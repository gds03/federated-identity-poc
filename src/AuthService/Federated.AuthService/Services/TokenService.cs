using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Federated.AuthService.Services;

public interface ITokenService
{
    string IssueAccessToken(
        string issuer,
        string clientId,
        IEnumerable<string> audiences,
        IEnumerable<string> scopes,
        TimeSpan lifetime);
}

public class TokenService : ITokenService
{
    private readonly IRsaKeyService _rsaKeyService;
    private readonly JwtSecurityTokenHandler _handler = new();

    public TokenService(IRsaKeyService rsaKeyService)
    {
        _rsaKeyService = rsaKeyService;
    }

    public string IssueAccessToken(
        string issuer,
        string clientId,
        IEnumerable<string> audiences,
        IEnumerable<string> scopes,
        TimeSpan lifetime)
    {
        var signingCredentials = new SigningCredentials(
            _rsaKeyService.GetSigningKey(),
            SecurityAlgorithms.RsaSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clientId),
            new("client_id", clientId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        // Add space-delimited scope claim according to RFC 6749 & RFC 8693
        string scopeString = string.Join(" ", scopes);
        if (!string.IsNullOrWhiteSpace(scopeString))
        {
            claims.Add(new Claim("scope", scopeString));
        }

        // Add audiences
        foreach (var aud in audiences)
        {
            if (!string.IsNullOrWhiteSpace(aud))
            {
                claims.Add(new Claim(JwtRegisteredClaimNames.Aud, aud));
            }
        }

        var now = DateTime.UtcNow;
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now,
            IssuedAt = now,
            Expires = now.Add(lifetime),
            SigningCredentials = signingCredentials
        };

        var securityToken = _handler.CreateToken(tokenDescriptor);
        return _handler.WriteToken(securityToken);
    }
}
