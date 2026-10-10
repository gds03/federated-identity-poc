using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;
using Federated.AuthService.Services;

namespace Federated.AuthService.Endpoints;

public record AuthCodeRecord(
    string Code,
    string ClientId,
    string RedirectUri,
    string CodeChallenge,
    string CodeChallengeMethod,
    string UserId,
    string UserName,
    string UserEmail,
    List<string> Roles,
    DateTime ExpiresAtUtc);

public static class MockOidcEndpoints
{
    // Thread-safe code store
    private static readonly ConcurrentDictionary<string, AuthCodeRecord> CodeStore = new();

    public static IEndpointRouteBuilder MapMockOidcEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/mock-oidc");

        // 1. Discovery document
        group.MapGet("/.well-known/openid-configuration", (IConfiguration config) =>
        {
            string baseUrl = (config["AuthService:Issuer"] ?? "http://localhost:5001") + "/mock-oidc";
            return Results.Ok(new
            {
                issuer = baseUrl,
                authorization_endpoint = $"{baseUrl}/authorize",
                token_endpoint = $"{baseUrl}/token",
                userinfo_endpoint = $"{baseUrl}/userinfo",
                jwks_uri = $"{baseUrl}/.well-known/jwks.json",
                response_types_supported = new[] { "code" },
                subject_types_supported = new[] { "public" },
                id_token_signing_alg_values_supported = new[] { "RS256" },
                scopes_supported = new[] { "openid", "profile", "email", "groups" },
                token_endpoint_auth_methods_supported = new[] { "none" }, // Public SPA client
                claims_supported = new[] { "sub", "iss", "name", "email", "groups", "roles" },
                code_challenge_methods_supported = new[] { "S256" }
            });
        });

        // 2. JWKS
        group.MapGet("/.well-known/jwks.json", (IRsaKeyService rsa) =>
        {
            return Results.Ok(rsa.GetJwks());
        });

        // 3. Authorize endpoint (PKCE)
        group.MapGet("/authorize", (
            HttpContext context,
            string? client_id,
            string? redirect_uri,
            string? response_type,
            string? scope,
            string? state,
            string? code_challenge,
            string? code_challenge_method,
            string? user) =>
        {
            if (string.IsNullOrWhiteSpace(client_id) || string.IsNullOrWhiteSpace(redirect_uri))
            {
                return Results.BadRequest("client_id and redirect_uri are required.");
            }

            if (string.IsNullOrWhiteSpace(code_challenge))
            {
                return Results.BadRequest("code_challenge is required (PKCE is mandatory for SPAs).");
            }

            // Quick bypass if direct user param specified
            if (!string.IsNullOrWhiteSpace(user))
            {
                return GenerateCodeAndRedirect(user, client_id, redirect_uri, code_challenge, code_challenge_method ?? "S256", state);
            }

            // Render interactive Mock OIDC Login screen
            string html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8""/>
    <title>Mock OneLogin / OIDC Provider</title>
    <style>
        body {{
            background: #0f172a; color: #f8fafc; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            display: flex; justify-content: center; align-items: center; min-height: 100vh; margin: 0;
        }}
        .card {{
            background: #1e293b; border: 1px solid #334155; border-radius: 12px; padding: 32px; width: 440px; box-shadow: 0 10px 25px rgba(0,0,0,0.5);
        }}
        .badge {{ background: #3b82f6; color: white; padding: 4px 8px; border-radius: 6px; font-size: 12px; font-weight: bold; }}
        h2 {{ margin: 12px 0 6px 0; font-size: 20px; }}
        p {{ color: #94a3b8; font-size: 14px; margin-bottom: 24px; line-height: 1.4; }}
        .btn {{
            display: block; width: 100%; padding: 12px; margin-bottom: 12px; border-radius: 8px; border: 1px solid transparent;
            font-size: 14px; font-weight: 600; cursor: pointer; text-align: left; text-decoration: none; box-sizing: border-box;
            transition: all 0.15s ease;
        }}
        .btn-admin {{ background: rgba(16, 185, 129, 0.15); border-color: rgba(16, 185, 129, 0.3); color: #34d399; }}
        .btn-admin:hover {{ background: rgba(16, 185, 129, 0.25); }}
        .btn-viewer {{ background: rgba(148, 163, 184, 0.1); border-color: #475569; color: #cbd5e1; }}
        .btn-viewer:hover {{ background: rgba(148, 163, 184, 0.2); }}
        .user-title {{ font-weight: bold; font-size: 15px; display: block; }}
        .user-sub {{ font-size: 12px; opacity: 0.8; display: block; margin-top: 2px; }}
        .footer {{ font-size: 11px; color: #64748b; margin-top: 20px; text-align: center; }}
    </style>
</head>
<body>
    <div class=""card"">
        <span class=""badge"">MOCK OIDC PROVIDER</span>
        <h2>Choose Admin Identity to Sign In</h2>
        <p>This simulates your enterprise IdP (e.g. OneLogin) with OpenID Connect Authorization Code Flow + PKCE.</p>

        <a class=""btn btn-admin"" href=""/mock-oidc/authorize?client_id={Uri.EscapeDataString(client_id)}&redirect_uri={Uri.EscapeDataString(redirect_uri)}&code_challenge={Uri.EscapeDataString(code_challenge)}&code_challenge_method={Uri.EscapeDataString(code_challenge_method ?? "S256")}&state={Uri.EscapeDataString(state ?? "")}&user=alice"">
            <span class=""user-title"">👤 Alice Admin (Full Access)</span>
            <span class=""user-sub"">alice.admin@internal &bull; Role: <strong>access-admin</strong> (Can edit, grant &amp; rotate)</span>
        </a>

        <a class=""btn btn-viewer"" href=""/mock-oidc/authorize?client_id={Uri.EscapeDataString(client_id)}&redirect_uri={Uri.EscapeDataString(redirect_uri)}&code_challenge={Uri.EscapeDataString(code_challenge)}&code_challenge_method={Uri.EscapeDataString(code_challenge_method ?? "S256")}&state={Uri.EscapeDataString(state ?? "")}&user=bob"">
            <span class=""user-title"">👤 Bob Viewer (Read-Only)</span>
            <span class=""user-sub"">bob.viewer@internal &bull; Role: <strong>viewer</strong> (Read-only access)</span>
        </a>

        <div class=""footer"">
            PKCE Verified &bull; Redirect URI: {redirect_uri}
        </div>
    </div>
</body>
</html>";

            return Results.Content(html, "text/html");
        });

        // 4. Token endpoint (PKCE verification & ID Token issuance)
        group.MapPost("/token", async (
            HttpContext context,
            IConfiguration config,
            ITokenService tokenService,
            IRsaKeyService rsaKeyService) =>
        {
            string? grantType = null;
            string? code = null;
            string? codeVerifier = null;
            string? redirectUri = null;

            if (context.Request.HasFormContentType)
            {
                var form = await context.Request.ReadFormAsync();
                grantType = form["grant_type"].ToString();
                code = form["code"].ToString();
                codeVerifier = form["code_verifier"].ToString();
                redirectUri = form["redirect_uri"].ToString();
            }
            else if (context.Request.HasJsonContentType())
            {
                var json = await context.Request.ReadFromJsonAsync<OidcTokenRequest>();
                if (json != null)
                {
                    grantType = json.grant_type;
                    code = json.code;
                    codeVerifier = json.code_verifier;
                    redirectUri = json.redirect_uri;
                }
            }

            if (grantType != "authorization_code")
            {
                return Results.BadRequest(new { error = "unsupported_grant_type" });
            }

            if (string.IsNullOrWhiteSpace(code) || !CodeStore.TryRemove(code, out var record))
            {
                return Results.BadRequest(new { error = "invalid_grant", error_description = "Invalid or expired authorization code." });
            }

            if (record.ExpiresAtUtc < DateTime.UtcNow)
            {
                return Results.BadRequest(new { error = "invalid_grant", error_description = "Authorization code expired." });
            }

            // Verify PKCE (RFC 7636 Section 4.6)
            if (string.IsNullOrWhiteSpace(codeVerifier))
            {
                return Results.BadRequest(new { error = "invalid_grant", error_description = "Missing code_verifier." });
            }

            if (!VerifyPkce(codeVerifier, record.CodeChallenge, record.CodeChallengeMethod))
            {
                return Results.BadRequest(new { error = "invalid_grant", error_description = "PKCE verification failed: code_verifier does not match code_challenge." });
            }

            string issuer = (config["AuthService:Issuer"] ?? "http://localhost:5001") + "/mock-oidc";

            // Create ID token containing user profile and groups/roles
            var signingCredentials = new SigningCredentials(rsaKeyService.GetSigningKey(), SecurityAlgorithms.RsaSha256);
            var now = DateTime.UtcNow;

            var claims = new List<System.Security.Claims.Claim>
            {
                new("sub", record.UserId),
                new("name", record.UserName),
                new("email", record.UserEmail),
                new("preferred_username", record.UserEmail),
                new("aud", record.ClientId),
                new("iss", issuer),
                new("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), System.Security.Claims.ClaimValueTypes.Integer64),
            };

            foreach (var role in record.Roles)
            {
                claims.Add(new System.Security.Claims.Claim("groups", role));
                claims.Add(new System.Security.Claims.Claim("roles", role));
            }

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = record.ClientId,
                Subject = new System.Security.Claims.ClaimsIdentity(claims),
                NotBefore = now,
                IssuedAt = now,
                Expires = now.AddHours(2),
                SigningCredentials = signingCredentials
            };

            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwt = handler.CreateToken(tokenDescriptor);
            string idToken = handler.WriteToken(jwt);

            return Results.Ok(new
            {
                id_token = idToken,
                access_token = idToken,
                token_type = "Bearer",
                expires_in = 7200,
                scope = "openid profile email groups"
            });
        });

        // 5. Userinfo endpoint
        group.MapGet("/userinfo", (HttpContext context) =>
        {
            // If Bearer token passed, parse sub
            var authHeader = context.Request.Headers["Authorization"].ToString();
            return Results.Ok(new
            {
                sub = "user-1",
                name = "Alice Admin",
                email = "alice.admin@internal",
                groups = new[] { "access-admin" }
            });
        });

        return app;
    }

    private static IResult GenerateCodeAndRedirect(
        string userType,
        string clientId,
        string redirectUri,
        string codeChallenge,
        string codeChallengeMethod,
        string? state)
    {
        string code = "authcode_" + Guid.NewGuid().ToString("N");
        bool isAdmin = userType.Equals("alice", StringComparison.OrdinalIgnoreCase);

        var record = new AuthCodeRecord(
            Code: code,
            ClientId: clientId,
            RedirectUri: redirectUri,
            CodeChallenge: codeChallenge,
            CodeChallengeMethod: codeChallengeMethod,
            UserId: isAdmin ? "usr-alice-admin" : "usr-bob-viewer",
            UserName: isAdmin ? "Alice Admin" : "Bob Viewer",
            UserEmail: isAdmin ? "alice.admin@internal" : "bob.viewer@internal",
            Roles: isAdmin ? new List<string> { "access-admin" } : new List<string> { "viewer" },
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(5));

        CodeStore[code] = record;

        string targetUrl = $"{redirectUri}?code={Uri.EscapeDataString(code)}";
        if (!string.IsNullOrWhiteSpace(state))
        {
            targetUrl += $"&state={Uri.EscapeDataString(state)}";
        }

        return Results.Redirect(targetUrl);
    }

    private static bool VerifyPkce(string codeVerifier, string expectedChallenge, string method)
    {
        if (method == "plain")
        {
            return codeVerifier == expectedChallenge;
        }

        // S256: BASE64URL-ENCODE(SHA256(ASCII(code_verifier)))
        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
        string computedChallenge = Base64UrlEncoder.Encode(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computedChallenge),
            Encoding.ASCII.GetBytes(expectedChallenge));
    }
}

public record OidcTokenRequest(
    string? grant_type,
    string? code,
    string? code_verifier,
    string? client_id,
    string? redirect_uri);
