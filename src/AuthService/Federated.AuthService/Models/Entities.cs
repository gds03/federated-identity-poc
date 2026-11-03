using System;
using System.Collections.Generic;

namespace Federated.AuthService.Models;

public class Client
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ClientId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<Grant> Grants { get; set; } = new();
}

public class Api
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

        public List<Permission> Permissions { get; set; } = new();
    public List<ApiRoute> Routes { get; set; } = new();
}

public class Permission
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ApiId { get; set; } = string.Empty;
    public Api? Api { get; set; }

    public string Scope { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

        public List<Grant> Grants { get; set; } = new();
    public List<ApiRoute> Routes { get; set; } = new();
}

/// <summary>
/// A route (HTTP method + path pattern) exposed by a registered API, and the permission (scope)
/// a client must hold to call it. Routes are the unit that /check evaluates: a request is allowed
/// only if it matches an active route AND the client holds the permission that route requires.
/// A route with no PermissionId is public (any known, active client may call it).
/// </summary>
public class ApiRoute
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ApiId { get; set; } = string.Empty;
    public Api? Api { get; set; }

    /// <summary>Upper-case HTTP method, or "*" for any method.</summary>
    public string Method { get; set; } = "GET";

    /// <summary>Path pattern, e.g. "/orders", "/orders/{id}", "/reports/**".</summary>
    public string PathPattern { get; set; } = string.Empty;

    /// <summary>Permission required to call this route. Null means the route is public.</summary>
    public string? PermissionId { get; set; }
    public Permission? Permission { get; set; }

    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Grant
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ClientId { get; set; } = string.Empty;
    public Client? Client { get; set; }

    public string PermissionId { get; set; } = string.Empty;
    public Permission? Permission { get; set; }

    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AuditEntry
{
    public int Id { get; set; }
    public string Actor { get; set; } = "system";
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
