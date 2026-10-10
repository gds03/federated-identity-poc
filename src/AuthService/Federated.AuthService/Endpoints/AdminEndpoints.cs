using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Federated.AuthService.Data;
using Federated.AuthService.Models;
using Federated.AuthService.Services;

namespace Federated.AuthService.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin");

        // ==========================================
        // 1. CLIENTS
        // ==========================================
        group.MapGet("/clients", async (AuthDbContext db) =>
        {
            var clients = await db.Clients
                .Include(c => c.Grants)
                .OrderByDescending(c => c.CreatedAtUtc)
                .Select(c => new
                {
                    c.Id,
                    c.ClientId,
                    c.ClientName,
                    c.IsActive,
                    c.CreatedAtUtc,
                    GrantCount = c.Grants.Count
                })
                .ToListAsync();

            return Results.Ok(clients);
        });

        group.MapPost("/clients", async (CreateClientDto dto, AuthDbContext db, ISecretHasher hasher) =>
        {
            if (string.IsNullOrWhiteSpace(dto.ClientId) || string.IsNullOrWhiteSpace(dto.ClientName))
            {
                return Results.BadRequest(new { message = "ClientId and ClientName are required." });
            }

            if (await db.Clients.AnyAsync(c => c.ClientId == dto.ClientId))
            {
                return Results.BadRequest(new { message = $"Client '{dto.ClientId}' already exists." });
            }

            // Generate secure random secret (shown only ONCE)
            string rawSecret = "sec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
                .Replace("/", "_").Replace("+", "-").TrimEnd('=');

            var client = new Client
            {
                ClientId = dto.ClientId.Trim(),
                ClientName = dto.ClientName.Trim(),
                SecretHash = hasher.HashSecret(rawSecret),
                IsActive = true
            };

            db.Clients.Add(client);

            // Audit
            db.AuditEntries.Add(new AuditEntry
            {
                Actor = dto.Actor ?? "admin",
                Action = "CREATE_CLIENT",
                EntityType = "Client",
                EntityId = client.ClientId,
                DetailsJson = JsonSerializer.Serialize(new { client.ClientId, client.ClientName })
            });

            await db.SaveChangesAsync();

            return Results.Created($"/api/admin/clients/{client.Id}", new
            {
                client.Id,
                client.ClientId,
                client.ClientName,
                client.IsActive,
                client.CreatedAtUtc,
                RawSecret = rawSecret // Only exposed at creation
            });
        });

        group.MapPost("/clients/{id}/toggle-status", async (string id, StatusToggleDto dto, AuthDbContext db) =>
        {
            var client = await db.Clients.FindAsync(id);
            if (client == null) return Results.NotFound(new { message = "Client not found." });

            bool previous = client.IsActive;
            client.IsActive = dto.IsActive;

            db.AuditEntries.Add(new AuditEntry
            {
                Actor = dto.Actor ?? "admin",
                Action = dto.IsActive ? "ENABLE_CLIENT" : "DISABLE_CLIENT",
                EntityType = "Client",
                EntityId = client.ClientId,
                DetailsJson = JsonSerializer.Serialize(new { client.ClientId, PreviousStatus = previous, NewStatus = client.IsActive })
            });

            await db.SaveChangesAsync();
            return Results.Ok(new { client.Id, client.ClientId, client.IsActive });
        });

        group.MapPost("/clients/{id}/rotate-secret", async (string id, ActorDto dto, AuthDbContext db, ISecretHasher hasher) =>
        {
            var client = await db.Clients.FindAsync(id);
            if (client == null) return Results.NotFound(new { message = "Client not found." });

            string newRawSecret = "sec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
                .Replace("/", "_").Replace("+", "-").TrimEnd('=');

            client.SecretHash = hasher.HashSecret(newRawSecret);

            db.AuditEntries.Add(new AuditEntry
            {
                Actor = dto?.Actor ?? "admin",
                Action = "ROTATE_SECRET",
                EntityType = "Client",
                EntityId = client.ClientId,
                DetailsJson = JsonSerializer.Serialize(new { client.ClientId, Message = "Client secret was rotated." })
            });

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                client.Id,
                client.ClientId,
                RawSecret = newRawSecret // Only shown once upon rotation
            });
        });

        // ==========================================
        // 2. APIS & PERMISSIONS
        // ==========================================
        group.MapGet("/apis", async (AuthDbContext db) =>
        {
            var apis = await db.Apis
                .Include(a => a.Permissions)
                .OrderBy(a => a.Name)
                .Select(a => new
                {
                    a.Id,
                    a.Name,
                    a.Audience,
                    a.Description,
                    a.IsActive,
                    Permissions = a.Permissions.Select(p => new
                    {
                        p.Id,
                        p.Scope,
                        p.Description,
                        p.IsActive
                    })
                })
                .ToListAsync();

            return Results.Ok(apis);
        });

        group.MapPost("/apis", async (CreateApiDto dto, AuthDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Audience))
            {
                return Results.BadRequest(new { message = "Name and Audience are required." });
            }

            if (await db.Apis.AnyAsync(a => a.Audience == dto.Audience))
            {
                return Results.BadRequest(new { message = $"Audience '{dto.Audience}' already exists." });
            }

            var api = new Api
            {
                Name = dto.Name.Trim(),
                Audience = dto.Audience.Trim(),
                Description = dto.Description?.Trim() ?? string.Empty,
                IsActive = true
            };

            db.Apis.Add(api);

            db.AuditEntries.Add(new AuditEntry
            {
                Actor = dto.Actor ?? "admin",
                Action = "CREATE_API",
                EntityType = "Api",
                EntityId = api.Audience,
                DetailsJson = JsonSerializer.Serialize(new { api.Name, api.Audience, api.Description })
            });

            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/apis/{api.Id}", api);
        });

        group.MapPut("/apis/{id}", async (string id, UpdateApiDto dto, AuthDbContext db) =>
        {
            var api = await db.Apis.FindAsync(id);
            if (api == null) return Results.NotFound(new { message = "API not found." });

            api.Name = dto.Name.Trim();
            api.Description = dto.Description?.Trim() ?? string.Empty;
            api.IsActive = dto.IsActive;

            db.AuditEntries.Add(new AuditEntry
            {
                Actor = dto.Actor ?? "admin",
                Action = "UPDATE_API",
                EntityType = "Api",
                EntityId = api.Audience,
                DetailsJson = JsonSerializer.Serialize(new { api.Name, api.Audience, api.IsActive })
            });

            await db.SaveChangesAsync();
            return Results.Ok(api);
        });

        group.MapPost("/apis/{id}/permissions", async (string id, CreatePermissionDto dto, AuthDbContext db) =>
        {
            var api = await db.Apis.FindAsync(id);
            if (api == null) return Results.NotFound(new { message = "API not found." });

            if (string.IsNullOrWhiteSpace(dto.Scope))
            {
                return Results.BadRequest(new { message = "Scope is required." });
            }

            if (await db.Permissions.AnyAsync(p => p.ApiId == id && p.Scope == dto.Scope))
            {
                return Results.BadRequest(new { message = $"Scope '{dto.Scope}' already exists for this API." });
            }

            var perm = new Permission
            {
                ApiId = id,
                Scope = dto.Scope.Trim(),
                Description = dto.Description?.Trim() ?? string.Empty,
                IsActive = true
            };

            db.Permissions.Add(perm);

            db.AuditEntries.Add(new AuditEntry
            {
                Actor = dto.Actor ?? "admin",
                Action = "CREATE_PERMISSION",
                EntityType = "Permission",
                EntityId = perm.Scope,
                DetailsJson = JsonSerializer.Serialize(new { ApiAudience = api.Audience, perm.Scope, perm.Description })
            });

            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/permissions/{perm.Id}", perm);
        });

        group.MapPut("/permissions/{id}", async (string id, UpdatePermissionDto dto, AuthDbContext db) =>
        {
            var perm = await db.Permissions.Include(p => p.Api).FirstOrDefaultAsync(p => p.Id == id);
            if (perm == null) return Results.NotFound(new { message = "Permission not found." });

            perm.Description = dto.Description?.Trim() ?? string.Empty;
            perm.IsActive = dto.IsActive;

            db.AuditEntries.Add(new AuditEntry
            {
                Actor = dto.Actor ?? "admin",
                Action = "UPDATE_PERMISSION",
                EntityType = "Permission",
                EntityId = perm.Scope,
                DetailsJson = JsonSerializer.Serialize(new { perm.Scope, perm.IsActive })
            });

            await db.SaveChangesAsync();
            return Results.Ok(perm);
        });

        // ==========================================
        // 3. ACCESS MATRIX
        // ==========================================
        group.MapGet("/matrix", async (AuthDbContext db) =>
        {
            var clients = await db.Clients
                .OrderBy(c => c.ClientId)
                .Select(c => new { c.Id, c.ClientId, c.ClientName, c.IsActive })
                .ToListAsync();

            var permissions = await db.Permissions
                .Include(p => p.Api)
                .OrderBy(p => p.Api!.Name).ThenBy(p => p.Scope)
                .Select(p => new
                {
                    p.Id,
                    p.Scope,
                    p.Description,
                    p.IsActive,
                    ApiId = p.ApiId,
                    ApiName = p.Api!.Name,
                    ApiAudience = p.Api.Audience
                })
                .ToListAsync();

            var grants = await db.Grants
                .Select(g => new { g.Id, g.ClientId, g.PermissionId })
                .ToListAsync();

            return Results.Ok(new { clients, permissions, grants });
        });

        group.MapPost("/matrix/toggle", async (ToggleGrantDto dto, AuthDbContext db) =>
        {
            var client = await db.Clients.FindAsync(dto.ClientId);
            var permission = await db.Permissions.Include(p => p.Api).FirstOrDefaultAsync(p => p.Id == dto.PermissionId);

            if (client == null || permission == null)
            {
                return Results.NotFound(new { message = "Client or Permission not found." });
            }

            var existing = await db.Grants.FirstOrDefaultAsync(g => g.ClientId == dto.ClientId && g.PermissionId == dto.PermissionId);

            string actionType;
            string diffDescription;

            if (dto.Enable)
            {
                if (existing == null)
                {
                    db.Grants.Add(new Grant
                    {
                        ClientId = dto.ClientId,
                        PermissionId = dto.PermissionId,
                        GrantedAtUtc = DateTime.UtcNow
                    });
                }
                actionType = "GRANT_PERMISSION";
                diffDescription = $"Granted scope '{permission.Scope}' ({permission.Api?.Name}) to client '{client.ClientId}'.";
            }
            else
            {
                if (existing != null)
                {
                    db.Grants.Remove(existing);
                }
                actionType = "REVOKE_PERMISSION";
                diffDescription = $"Revoked scope '{permission.Scope}' ({permission.Api?.Name}) from client '{client.ClientId}'.";
            }

            db.AuditEntries.Add(new AuditEntry
            {
                Actor = dto.Actor ?? "admin",
                Action = actionType,
                EntityType = "Grant",
                EntityId = $"{client.ClientId}:{permission.Scope}",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    client.ClientId,
                    permission.Scope,
                    ApiAudience = permission.Api?.Audience,
                    dto.Enable,
                    Summary = diffDescription
                })
            });

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                success = true,
                diff = diffDescription,
                dto.ClientId,
                dto.PermissionId,
                dto.Enable
            });
        });

        // ==========================================
        // 4. ACCESS CHECKER
        // ==========================================
        group.MapPost("/check-access", async (AccessCheckRequestDto req, AuthDbContext db) =>
        {
            var client = await db.Clients
                .Include(c => c.Grants)
                    .ThenInclude(g => g.Permission)
                        .ThenInclude(p => p!.Api)
                .FirstOrDefaultAsync(c => c.ClientId == req.ClientId);

            if (client == null)
            {
                return Results.Ok(new AccessCheckResponseDto(
                    Allowed: false,
                    Reason: $"Client '{req.ClientId}' does not exist.",
                    RequiredScope: "unknown",
                    ClientScopes: Array.Empty<string>()));
            }

            if (!client.IsActive)
            {
                return Results.Ok(new AccessCheckResponseDto(
                    Allowed: false,
                    Reason: $"Client '{req.ClientId}' is currently disabled.",
                    RequiredScope: "unknown",
                    ClientScopes: Array.Empty<string>()));
            }

            var api = await db.Apis.FirstOrDefaultAsync(a => a.Audience == req.ApiAudience);
            if (api == null || !api.IsActive)
            {
                return Results.Ok(new AccessCheckResponseDto(
                    Allowed: false,
                    Reason: $"Target API with audience '{req.ApiAudience}' is not registered or is inactive.",
                    RequiredScope: "unknown",
                    ClientScopes: Array.Empty<string>()));
            }

            // Determine required scope by route and HTTP method
            string requiredScope = DetermineRequiredScope(req.ApiAudience, req.Route, req.Method);

            var clientActiveScopes = client.Grants
                .Where(g => g.Permission != null && g.Permission.IsActive && g.Permission.Api != null && g.Permission.Api.IsActive)
                .Select(g => g.Permission!.Scope)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (requiredScope == "none")
            {
                return Results.Ok(new AccessCheckResponseDto(
                    Allowed: true,
                    Reason: "Route is publicly accessible / anonymous.",
                    RequiredScope: "none",
                    ClientScopes: clientActiveScopes));
            }

            bool hasScope = clientActiveScopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase);

            if (!hasScope)
            {
                return Results.Ok(new AccessCheckResponseDto(
                    Allowed: false,
                    Reason: $"Client lacks required scope '{requiredScope}'. Possessed scopes: [{string.Join(", ", clientActiveScopes)}].",
                    RequiredScope: requiredScope,
                    ClientScopes: clientActiveScopes));
            }

            return Results.Ok(new AccessCheckResponseDto(
                Allowed: true,
                Reason: $"Access allowed. Client holds valid grant for scope '{requiredScope}'.",
                RequiredScope: requiredScope,
                ClientScopes: clientActiveScopes));
        });

        // ==========================================
        // 5. AUDIT LOG
        // ==========================================
        group.MapGet("/audit", async (AuthDbContext db) =>
        {
            var logs = await db.AuditEntries
                .OrderByDescending(a => a.TimestampUtc)
                .Take(100)
                .ToListAsync();

            return Results.Ok(logs);
        });

        return app;
    }

    private static string DetermineRequiredScope(string audience, string route, string method)
    {
        string normRoute = route.Trim().ToLowerInvariant();
        string normMethod = method.Trim().ToUpperInvariant();

        if (normRoute == "/health") return "none";

        if (audience.Contains("orders"))
        {
            if (normMethod == "GET") return "orders.read";
            if (normMethod == "POST" || normMethod == "PUT" || normMethod == "DELETE") return "orders.write";
        }
        else if (audience.Contains("customers"))
        {
            if (normMethod == "GET") return "customers.read";
        }
        else if (audience.Contains("inventory"))
        {
            if (normMethod == "GET") return "inventory.read";
            if (normMethod == "PUT" || normMethod == "POST") return "inventory.write";
        }

        return $"{normRoute.TrimStart('/').Split('/')[0]}.read";
    }
}

public record CreateClientDto(string ClientId, string ClientName, string? Actor);
public record StatusToggleDto(bool IsActive, string? Actor);
public record ActorDto(string? Actor);
public record CreateApiDto(string Name, string Audience, string? Description, string? Actor);
public record UpdateApiDto(string Name, string? Description, bool IsActive, string? Actor);
public record CreatePermissionDto(string Scope, string? Description, string? Actor);
public record UpdatePermissionDto(string? Description, bool IsActive, string? Actor);
public record ToggleGrantDto(string ClientId, string PermissionId, bool Enable, string? Actor);
public record AccessCheckRequestDto(string ClientId, string ApiAudience, string Route, string Method);
public record AccessCheckResponseDto(bool Allowed, string Reason, string RequiredScope, IEnumerable<string> ClientScopes);
