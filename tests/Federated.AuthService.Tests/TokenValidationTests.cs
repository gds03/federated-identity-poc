using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Federated.AuthService.Services;
using Federated.Common;
using Xunit;

namespace Federated.AuthService.Tests;

public class TokenValidationTests
{
    private readonly IRsaKeyService _rsaKeyService;
    private readonly ITokenService _tokenService;

    public TokenValidationTests()
    {
        // Use a test key folder
        string testKeyDir = Path.Combine(Path.GetTempPath(), "fed_auth_test_keys_" + Guid.NewGuid().ToString("N"));
        _rsaKeyService = new RsaKeyService(testKeyDir);
        _tokenService = new TokenService(_rsaKeyService);
    }

    [Fact]
    public void TokenService_IssuesValidJwtWithExpectedClaims()
    {
        // Arrange
        string issuer = "http://localhost:5001";
        string clientId = "service-a";
        var audiences = new[] { "https://orders.internal", "https://inventory.internal" };
        var scopes = new[] { "orders.read", "inventory.read" };

        // Act
        string token = _tokenService.IssueAccessToken(
            issuer: issuer,
            clientId: clientId,
            audiences: audiences,
            scopes: scopes,
            lifetime: TimeSpan.FromMinutes(15));

        // Assert
        Assert.NotEmpty(token);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.Equal(_rsaKeyService.KeyId, jwt.Header.Kid);
        Assert.Equal(issuer, jwt.Issuer);
        Assert.Equal(clientId, jwt.Subject);
        Assert.Equal("orders.read inventory.read", jwt.Claims.First(c => c.Type == "scope").Value);
        Assert.Contains(jwt.Audiences, a => a == "https://orders.internal");
    }

    [Fact]
    public void TokenValidation_SucceedsWithMatchingPublicKeyAndAudience()
    {
        // Arrange
        string issuer = "http://localhost:5001";
        string clientId = "api-runner";
        string audience = "https://orders.internal";

        string token = _tokenService.IssueAccessToken(
            issuer: issuer,
            clientId: clientId,
            audiences: new[] { audience },
            scopes: new[] { "orders.read" },
            lifetime: TimeSpan.FromMinutes(10));

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _rsaKeyService.GetSigningKey(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var handler = new JwtSecurityTokenHandler();

        // Act
        var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);

        // Assert
        Assert.NotNull(principal);
        Assert.Equal(clientId, principal.FindFirst("client_id")?.Value);
    }

    [Fact]
    public void TokenValidation_FailsWhenTokenIsExpired()
    {
        // Arrange
        string issuer = "http://localhost:5001";
        string audience = "https://orders.internal";
        var handler = new JwtSecurityTokenHandler();

        var pastTime = DateTime.UtcNow.AddMinutes(-10);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(new[] { new Claim("client_id", "expired-client") }),
            NotBefore = pastTime,
            IssuedAt = pastTime,
            Expires = pastTime.AddMinutes(5), // Expired 5 minutes ago!
            SigningCredentials = new SigningCredentials(_rsaKeyService.GetSigningKey(), SecurityAlgorithms.RsaSha256)
        };

        var tokenObj = handler.CreateToken(tokenDescriptor);
        string expiredToken = handler.WriteToken(tokenObj);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _rsaKeyService.GetSigningKey(),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        // Act & Assert
        Assert.Throws<SecurityTokenExpiredException>(() =>
        {
            handler.ValidateToken(expiredToken, validationParameters, out _);
        });
    }

    [Fact]
    public void TokenValidation_FailsWhenAudienceMismatches()
    {
        // Arrange
        string issuer = "http://localhost:5001";

        // Token only issued for OrdersApi
        string token = _tokenService.IssueAccessToken(
            issuer: issuer,
            clientId: "orders-worker",
            audiences: new[] { "https://orders.internal" },
            scopes: new[] { "orders.read" },
            lifetime: TimeSpan.FromMinutes(10));

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            // Resource server expects CustomersApi
            ValidAudience = "https://customers.internal",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _rsaKeyService.GetSigningKey()
        };

        var handler = new JwtSecurityTokenHandler();

        // Act & Assert
        Assert.Throws<SecurityTokenInvalidAudienceException>(() =>
        {
            handler.ValidateToken(token, validationParameters, out _);
        });
    }

    [Fact]
    public async Task ScopeHandler_AllowsWhenScopePresentAndDeniesWhenMissing()
    {
        // Arrange
        var handler = new ScopeHandler();
        var requirement = new ScopeRequirement("orders.write");

        // Case 1: ClaimsPrincipal only has "orders.read inventory.read"
        var identityWithoutScope = new ClaimsIdentity(new[]
        {
            new Claim("scope", "orders.read inventory.read")
        }, "TestAuth");
        var contextDenied = new AuthorizationHandlerContext(
            new[] { requirement },
            new ClaimsPrincipal(identityWithoutScope),
            null);

        // Act
        await handler.HandleAsync(contextDenied);

        // Assert Case 1: HasSucceeded should be false
        Assert.False(contextDenied.HasSucceeded);

        // Case 2: ClaimsPrincipal has "orders.read orders.write"
        var identityWithScope = new ClaimsIdentity(new[]
        {
            new Claim("scope", "orders.read orders.write")
        }, "TestAuth");
        var contextAllowed = new AuthorizationHandlerContext(
            new[] { requirement },
            new ClaimsPrincipal(identityWithScope),
            null);

        // Act
        await handler.HandleAsync(contextAllowed);

        // Assert Case 2: HasSucceeded should be true
        Assert.True(contextAllowed.HasSucceeded);
    }
}

public class CheckTokenValidatorTests
{
    private const string Issuer = "http://localhost:5001";
    private const string Aud = "https://orders.internal";
    private readonly IRsaKeyService _keys = new RsaKeyService(Path.Combine(Path.GetTempPath(), "fed_check_keys_" + Guid.NewGuid().ToString("N")));

    private string Issue(IRsaKeyService keys, string issuer = Issuer) =>
        new TokenService(keys).IssueAccessToken(issuer, "api-a", new[] { Aud }, new[] { "orders.read" }, TimeSpan.FromMinutes(5));

    [Fact]
    public void Accepts_ValidToken_AndExtractsClientAndAudience()
    {
        var result = new CheckTokenValidator(_keys, Issuer).Validate("Bearer " + Issue(_keys), Aud);
        Assert.True(result.IsValid);
        Assert.Equal("api-a", result.ClientId);
        Assert.Equal(Aud, result.Audience);
    }

    [Fact]
    public void Rejects_TokenSignedWithAnotherKey()
    {
        var attacker = new RsaKeyService(Path.Combine(Path.GetTempPath(), "fed_check_keys_" + Guid.NewGuid().ToString("N")));
        var result = new CheckTokenValidator(_keys, Issuer).Validate(Issue(attacker), Aud);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Rejects_UnsignedForgedToken()
    {
        var forged = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            Issuer, Aud, new[] { new Claim("client_id", "admin") }, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5)));
        Assert.False(new CheckTokenValidator(_keys, Issuer).Validate(forged, Aud).IsValid);
    }

    [Fact]
    public void Rejects_ExpiredToken()
    {
        var expired = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            Issuer, Aud, new[] { new Claim("client_id", "api-a") },
            DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-5),
            new SigningCredentials(_keys.GetSigningKey(), SecurityAlgorithms.RsaSha256)));
        Assert.False(new CheckTokenValidator(_keys, Issuer).Validate(expired, Aud).IsValid);
    }

    [Fact]
    public void Rejects_WrongIssuer_And_WrongAudience_And_Garbage()
    {
        var validator = new CheckTokenValidator(_keys, Issuer);
        Assert.False(validator.Validate(Issue(_keys, issuer: "http://evil"), Aud).IsValid);
        Assert.False(validator.Validate(Issue(_keys), "https://inventory.internal").IsValid);
        Assert.False(validator.Validate("not-a-jwt", Aud).IsValid);
    }
}
