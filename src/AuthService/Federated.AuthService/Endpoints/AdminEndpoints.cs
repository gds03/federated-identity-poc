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
        }).RequireAuthorization(AdminPolicies.Read);

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
        }).RequireAuthorization(AdminPolicies.Write);

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
        }).RequireAuthorization(AdminPolicies.Write);

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
        }).RequireAuthorization(AdminPolicies.Write);

        // ==========================================
        // 2. APIS & PERMISSIONS
        // ==========================================
        group.MapGet("/apis", async (AuthDbContext db) =>
        {
                        var apis = await db.Apis
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
                    }),
                    Routes = a.Routes
                        .OrderBy(r => r.PathPattern).ThenBy(r => r.Method)
                        .Select(r => new
                        {
                            r.Id,
                            r.Method,
                            r.PathPattern,
                            r.PermissionId,
                            Scope = r.Permission != null ? r.Permission.Scope : null,
                            r.Description,
                            r.IsActive
                        })
                })
                .ToListAsync();

            return Results.Ok(apis);
        }).RequireAuthorization(AdminPolicies.Read);

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
        }).RequireAuthorization(AdminPolicies.Write);

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
        }).RequireAuthorization(AdminPolicies.Write);

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
        }).RequireAuthorization(AdminPolicies.Write);

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
        }).RequireAuthorization(AdminPolicies.Write);

                // ==========================================
        // 2b. API ROUTES (which route needs which permission)
        // ==========================================
        group.MapPost("/apis/{id}/routes", async (string id, CreateRouteDto dto, AuthDbContext db) =>
        {
            var api = await db.Apis.FindAsync(id);
            if (api == null) return Results.NotFound(new { message = "API not found." });

            string? permissionId = string.IsNullOrWhiteSpace(dto.PermissionId) ? null : dto.PermissionId;

            var error = await ValidateRouteAsync(db, api, dto.Method, dto.PathPattern, permissionId, null);
            if (error != null) return Results.BadRequest(new { message = error });

            var route = new ApiRoute
            {
                ApiId = api.Id,
                Method = RoutePatternMatcher.NormalizeMethod(dto.Method),
                PathPattern = RoutePatternMatcher.NormalizePath(dto.PathPattern),
                PermissionId = permissionId,
                Description = dto.Description?.Trim() ?? string.Empty,
                IsActive = true
            };

            string? scope = permissionId == null ? null
                : (await db.Permissions.FindAsync(permissionId))?.Scope;

            db.ApiRoutes.Add(route);
            db.AuditEntries.Add(new AuditEntry
            {
                Actor = dto.Actor ?? "admin",
                Action = "CREATE_ROUTE",
                EntityType = "Route",
                EntityId = $"{api.Audience}:{route.Method} {route.PathPattern}",
                DetailsJson = JsonSerializer.Serialize(new { ApiAudience = api.Audience, After = RouteSnapshot(route, scope) })
            });

            await db.SaveChangesAsync();

            return Results.Created($"/api/admin/routes/{route.Id}", new
            {
                route.Id,
                route.Method,
                route.PathPattern,
                route.PermissionId,
                Scope = scope,
                route.Description,
                route.IsActive
            });
        }).RequireAuthorization(AdminPolicies.Write);

        group.MapPut("/routes/{id}", async (string id, UpdateRouteDto dto, AuthDbContext db) =>
        {
            var route = await db.ApiRoutes
                .Include(r => r.Api)
                .Include(r => r.Permission)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (route == null) return Results.NotFound(new { message = "Route not found." });

            string? permissionId = string.IsNullOrWhiteSpace(dto.PermissionId) ? null : dto.PermissionId;

            var error = await ValidateRouteAsync(db, route.Api!, dto.Method, dto.PathPattern, permissionId, route.Id);
            if (error != null) return Results.BadRequest(new { message = error });

            var before = RouteSnapshot(route, route.Permission?.Scope);

            route.Method = RoutePatternMatcher.NormalizeMethod(dto.Method);
            route.PathPattern = RoutePatternMatcher.NormalizePath(dto.PathPattern);
            route.PermissionId = permissionId;
            route.Description = dto.Description?.Trim() ?? string.Empty;
            route.IsActive = dto.IsActive;

            string? scope = permissionId == null ? null
                : (await db.Permissions.FindAsync(permissionId))?.Scope;

            db.AuditEntries.Add(new AuditEntry
            {
                Actor = dto.Actor ?? "admin",
                Action = "UPDATE_ROUTE",
                EntityType = "Route",
                EntityId = $"{route.Api!.Audience}:{route.Method} {route.PathPattern}",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    ApiAudience = route.Api.Audience,
                    Before = before,
                    After = RouteSnapshot(route, scope)
                })
            });

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                route.Id,
                route.Method,
                route.PathPattern,
                route.PermissionId,
                Scope = scope,
                route.Description,
                route.IsActive
            });
        }).RequireAuthorization(AdminPolicies.Write);

        // Effective routes for one client: every active route, flagged allowed / denied.
        group.MapGet("/clients/{id}/routes", async (string id, AuthDbContext db, IRouteAccessEvaluator evaluator) =>
        {
            var client = await db.Clients.FindAsync(id);
            if (client == null) return Results.NotFound(new { message = "Client not found." });

            var routes = await evaluator.GetEffectiveRoutesAsync(client);
            return Results.Ok(new { client.Id, client.ClientId, client.ClientName, client.IsActive, Routes = routes });
        }).RequireAuthorization(AdminPolicies.Read);

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
        }).RequireAuthorization(AdminPolicies.Read);

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
        }).RequireAuthorization(AdminPolicies.Write);

        // ==========================================
        // 4. ACCESS CHECKER
        // ==========================================
                // Uses the exact same evaluator as the runtime POST /check, so the simulator can never drift from real behavior.
        group.MapPost("/check-access", async (AccessCheckRequestDto req, IRouteAccessEvaluator evaluator) =>
        {
            var decision = await evaluator.EvaluateAsync(req.ClientId, req.ApiAudience, req.Route, req.Method);

            return Results.Ok(new AccessCheckResponseDto(
                decision.Allowed,
                decision.Reason,
                decision.RequiredScope,
                decision.ClientScopes,
                decision.MatchedRoute));
        }).RequireAuthorization(AdminPolicies.Read);

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
        }).RequireAuthorization(AdminPolicies.Read);

        return app;
    }

        private static readonly string[] ValidationMethods = RoutePatternMatcher.AllowedMethods;

    /// <summary>Validates a route definition against its API. Returns an error message or null.</summary>
    private static async Task<string?> ValidateRouteAsync(
        AuthDbContext db, Api api, string method, string pathPattern, string? permissionId, string? excludeRouteId)
    {
        if (!RoutePatternMatcher.IsValidMethod(method))
        {
            return $"Method must be one of: {string.Join(", ", ValidationMethods)}.";
        }

        var patternError = RoutePatternMatcher.Validate(pathPattern);
        if (patternError != null) return patternError;

        if (!string.IsNullOrWhiteSpace(permissionId))
        {
            var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Id == permissionId);
            if (perm == null) return "Permission not found.";
            if (perm.ApiId != api.Id) return $"Permission '{perm.Scope}' does not belong to API '{api.Name}'.";
        }

        string normMethod = RoutePatternMatcher.NormalizeMethod(method);
        string normPattern = RoutePatternMatcher.NormalizePath(pathPattern);
        bool duplicate = await db.ApiRoutes.AnyAsync(r =>
            r.ApiId == api.Id && r.Method == normMethod && r.PathPattern == normPattern && r.Id != excludeRouteId);
        if (duplicate) return $"Route '{normMethod} {normPattern}' already exists for this API.";

        return null;
    }

    private static object RouteSnapshot(ApiRoute r, string? scope) => new
    {
        r.Method,
        r.PathPattern,
        Scope = scope,
        Public = r.PermissionId == null,
        r.IsActive
    };
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
public record AccessCheckResponseDto(bool Allowed, string Reason, string RequiredScope, IEnumerable<string> ClientScopes, string? MatchedRoute = null);
public record CreateRouteDto(string Method, string PathPattern, string? PermissionId, string? Description, string? Actor);
public record UpdateRouteDto(string Method, string PathPattern, string? PermissionId, string? Description, bool IsActive, string? Actor);
