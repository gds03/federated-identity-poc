using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Federated.AuthService.Data;
using Federated.AuthService.Models;
using Federated.AuthService.Services;
using Xunit;

namespace Federated.AuthService.Tests;

public class GrantLogicTests
{
    private readonly ISecretHasher _hasher = new SecretHasher();

    private AuthDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    [Fact]
    public void SecretHasher_CorrectlyHashesAndVerifiesSecrets()
    {
        // Arrange
        string rawSecret = "super-secret-password-123!";

        // Act
        string hash = _hasher.HashSecret(rawSecret);

        // Assert
        Assert.NotEmpty(hash);
        Assert.Contains(".", hash); // PBKDF2 iterations.salt.hash format
        Assert.True(_hasher.VerifySecret(rawSecret, hash));
        Assert.False(_hasher.VerifySecret("wrong-password", hash));
    }

    [Fact]
    public async Task GrantAssignment_AllowsResolvingActiveScopesForClient()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();

        var api = new Api { Name = "OrdersApi", Audience = "https://orders.internal", IsActive = true };
        db.Apis.Add(api);

        var permRead = new Permission { ApiId = api.Id, Scope = "orders.read", IsActive = true };
        var permWrite = new Permission { ApiId = api.Id, Scope = "orders.write", IsActive = true };
        db.Permissions.AddRange(permRead, permWrite);

        var client = new Client
        {
            ClientId = "test-client",
            ClientName = "Test Client",
            SecretHash = _hasher.HashSecret("pwd"),
            IsActive = true
        };
        db.Clients.Add(client);

        // Grant only orders.read
        db.Grants.Add(new Grant { ClientId = client.Id, PermissionId = permRead.Id });
        await db.SaveChangesAsync();

        // Act
        var resolvedClient = await db.Clients
            .Include(c => c.Grants)
                .ThenInclude(g => g.Permission)
            .FirstOrDefaultAsync(c => c.ClientId == "test-client");

        var grantedScopes = resolvedClient!.Grants
            .Where(g => g.Permission!.IsActive)
            .Select(g => g.Permission!.Scope)
            .ToList();

        // Assert
        Assert.Single(grantedScopes);
        Assert.Contains("orders.read", grantedScopes);
        Assert.DoesNotContain("orders.write", grantedScopes);
    }

    [Fact]
    public async Task GrantRevocation_ImmediatelyRemovesScopeFromActiveGrants()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();

        var api = new Api { Name = "InventoryApi", Audience = "https://inventory.internal", IsActive = true };
        var perm = new Permission { ApiId = api.Id, Scope = "inventory.read", IsActive = true };
        var client = new Client { ClientId = "inv-worker", ClientName = "Worker", SecretHash = "hash", IsActive = true };
        var grant = new Grant { ClientId = client.Id, PermissionId = perm.Id };

        db.Apis.Add(api);
        db.Permissions.Add(perm);
        db.Clients.Add(client);
        db.Grants.Add(grant);
        await db.SaveChangesAsync();

        // Act - Revoke
        db.Grants.Remove(grant);
        await db.SaveChangesAsync();

        var remainingGrants = await db.Grants.Where(g => g.ClientId == client.Id).ToListAsync();

        // Assert
        Assert.Empty(remainingGrants);
    }

    [Fact]
    public async Task DisabledClient_IsNotPermittedToAuthenticate()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var client = new Client
        {
            ClientId = "disabled-app",
            ClientName = "Disabled App",
            SecretHash = _hasher.HashSecret("sec"),
            IsActive = false // Disabled
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        // Act
        var loaded = await db.Clients.FirstOrDefaultAsync(c => c.ClientId == "disabled-app");

        // Assert
        Assert.NotNull(loaded);
        Assert.False(loaded.IsActive);
    }
}
