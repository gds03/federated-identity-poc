using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Federated.AuthService.Data;
using Federated.AuthService.Models;

namespace Federated.AuthService.Services;

public record RouteDecision(
    bool Allowed,
    string Reason,
    string ClientId,
    string RequiredScope,
    IReadOnlyList<string> ClientScopes,
    string? MatchedRoute = null);

/// <summary>One row of the "effective routes" view for a client.</summary>
public record EffectiveRoute(
    string ApiId,
    string ApiName,
    string Audience,
    string RouteId,
    string Method,
    string PathPattern,
    string? Scope,
    bool IsPublic,
    bool Allowed,
    string Reason);

public interface IRouteAccessEvaluator
{
    /// <summary>
    /// Single source of truth for "may this client call this method on this route of this API?".
    /// Used by both POST /check (runtime) and the Admin access checker (simulation).
    /// Default-deny: a request is allowed only if it matches an active registered route
    /// and the client holds the permission that route requires.
    /// </summary>
    Task<RouteDecision> EvaluateAsync(
        string clientId, string audience, string route, string method, CancellationToken ct = default);

    /// <summary>Lists every active route of every active API, flagged by whether the client may call it.</summary>
    Task<IReadOnlyList<EffectiveRoute>> GetEffectiveRoutesAsync(Client client, CancellationToken ct = default);
}

public class RouteAccessEvaluator : IRouteAccessEvaluator
{
    private readonly AuthDbContext _db;

    public RouteAccessEvaluator(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<RouteDecision> EvaluateAsync(
        string clientId, string audience, string route, string method, CancellationToken ct = default)
    {
        var none = Array.Empty<string>();

        var client = await LoadClientAsync(c => c.ClientId == clientId, ct);

        if (client == null)
        {
            return new RouteDecision(false, $"Client '{clientId}' does not exist in central registry.", clientId, "unknown", none);
        }

        if (!client.IsActive)
        {
            return new RouteDecision(false, $"Client '{clientId}' is currently disabled.", clientId, "unknown", none);
        }

        var liveGrants = LiveGrants(client);
        var clientScopes = ScopesOf(liveGrants);

        if (string.IsNullOrWhiteSpace(audience))
        {
            return new RouteDecision(false, "Target API audience is required to evaluate a route.", clientId, "unknown", clientScopes);
        }

        var api = await _db.Apis
            .Include(a => a.Routes)
                .ThenInclude(r => r.Permission)
            .FirstOrDefaultAsync(a => a.Audience == audience, ct);

        if (api == null || !api.IsActive)
        {
            return new RouteDecision(false,
                $"Target API with audience '{audience}' is not registered or is inactive.",
                clientId, "unknown", clientScopes);
        }

        string normMethod = RoutePatternMatcher.NormalizeMethod(method);
        string normRoute = RoutePatternMatcher.NormalizePath(route);

        var matched = FindBestRoute(api.Routes, normMethod, normRoute);

        if (matched == null)
        {
            return new RouteDecision(false,
                $"No route registered on '{api.Name}' matches {normMethod} {normRoute}. Access is denied by default.",
                clientId, "unknown", clientScopes);
        }

        string routeLabel = $"{matched.Method} {matched.PathPattern}";

        if (matched.PermissionId == null)
        {
            return new RouteDecision(true,
                $"Route '{routeLabel}' is public; no permission required.",
                clientId, "none", clientScopes, routeLabel);
        }

        var permission = matched.Permission;
        if (permission == null || !permission.IsActive)
        {
            return new RouteDecision(false,
                $"Route '{routeLabel}' requires permission '{permission?.Scope ?? matched.PermissionId}', which is disabled.",
                clientId, permission?.Scope ?? "unknown", clientScopes, routeLabel);
        }

        bool granted = liveGrants.Any(g => g.PermissionId == permission.Id);
        if (!granted)
        {
            return new RouteDecision(false,
                $"Client lacks required scope '{permission.Scope}' for route '{routeLabel}'. Active scopes: [{string.Join(", ", clientScopes)}].",
                clientId, permission.Scope, clientScopes, routeLabel);
        }

        return new RouteDecision(true,
            $"Access allowed. Route '{routeLabel}' requires '{permission.Scope}' and the client holds an active grant for it.",
            clientId, permission.Scope, clientScopes, routeLabel);
    }

    public async Task<IReadOnlyList<EffectiveRoute>> GetEffectiveRoutesAsync(Client client, CancellationToken ct = default)
    {
        var loaded = await LoadClientAsync(c => c.Id == client.Id, ct) ?? client;
        var grantedPermissionIds = LiveGrants(loaded).Select(g => g.PermissionId).ToHashSet();

        var apis = await _db.Apis
            .Where(a => a.IsActive)
            .Include(a => a.Routes.Where(r => r.IsActive))
                .ThenInclude(r => r.Permission)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);

        var result = new List<EffectiveRoute>();
        foreach (var api in apis)
        {
            foreach (var r in api.Routes.OrderBy(r => r.PathPattern).ThenBy(r => r.Method))
            {
                bool isPublic = r.PermissionId == null;
                bool permissionActive = r.Permission?.IsActive ?? false;
                bool allowed = loaded.IsActive &&
                               (isPublic || (permissionActive && grantedPermissionIds.Contains(r.PermissionId!)));

                string reason = !loaded.IsActive ? "Client is disabled."
                    : isPublic ? "Public route."
                    : !permissionActive ? $"Permission '{r.Permission?.Scope}' is disabled."
                    : allowed ? $"Granted via '{r.Permission!.Scope}'."
                    : $"Missing grant '{r.Permission!.Scope}'.";

                result.Add(new EffectiveRoute(
                    api.Id, api.Name, api.Audience, r.Id, r.Method, r.PathPattern,
                    r.Permission?.Scope, isPublic, allowed, reason));
            }
        }

        return result;
    }

    private static ApiRoute? FindBestRoute(IEnumerable<ApiRoute> routes, string method, string path)
    {
        ApiRoute? best = null;
        int bestSpecificity = -1;
        bool bestExactMethod = false;

        foreach (var r in routes)
        {
            if (!r.IsActive) continue;

            bool exactMethod = string.Equals(r.Method, method, StringComparison.OrdinalIgnoreCase);
            if (!exactMethod && r.Method != "*") continue;

            if (!RoutePatternMatcher.TryMatch(r.PathPattern, path, out int specificity)) continue;

            bool better = best == null
                          || specificity > bestSpecificity
                          || (specificity == bestSpecificity && exactMethod && !bestExactMethod);

            if (better)
            {
                best = r;
                bestSpecificity = specificity;
                bestExactMethod = exactMethod;
            }
        }

        return best;
    }

    private Task<Client?> LoadClientAsync(System.Linq.Expressions.Expression<Func<Client, bool>> predicate, CancellationToken ct)
    {
        return _db.Clients
            .Include(c => c.Grants)
                .ThenInclude(g => g.Permission)
                    .ThenInclude(p => p!.Api)
            .FirstOrDefaultAsync(predicate, ct);
    }

    /// <summary>Only grants whose permission AND api are active are considered "live".</summary>
    private static List<Grant> LiveGrants(Client client)
        => client.Grants
            .Where(g => g.Permission != null && g.Permission.IsActive
                        && g.Permission.Api != null && g.Permission.Api.IsActive)
            .ToList();

    private static IReadOnlyList<string> ScopesOf(IEnumerable<Grant> grants)
        => grants
            .Select(g => g.Permission!.Scope)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
