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
