using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Federated.AuthService.Models;
using Federated.AuthService.Services;

namespace Federated.AuthService.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AuthDbContext db, ISecretHasher hasher)
    {
        await db.Database.EnsureCreatedAsync();
        await EnsureRouteTableAsync(db);

        if (await db.Apis.AnyAsync())
        {
            // Already seeded; make sure databases created before routes existed get the default route registry.
            await SeedDefaultRoutesAsync(db);
            return;
        }


        // 1. APIs
        var ordersApi = new Api
        {
            Name = "OrdersApi",
            Audience = "https://orders.internal",
            Description = "Order processing and tracking service"
        };
        var customersApi = new Api
        {
            Name = "CustomersApi",
            Audience = "https://customers.internal",
            Description = "Customer profile and management service"
        };
        var inventoryApi = new Api
        {
            Name = "InventoryApi",
            Audience = "https://inventory.internal",
            Description = "Warehouse stock and product catalog service"
        };

        db.Apis.AddRange(ordersApi, customersApi, inventoryApi);
        await db.SaveChangesAsync();

        // 2. Permissions
        var permOrdersRead = new Permission { ApiId = ordersApi.Id, Scope = "orders.read", Description = "Read order details" };
        var permOrdersWrite = new Permission { ApiId = ordersApi.Id, Scope = "orders.write", Description = "Create and update orders" };

        var permCustRead = new Permission { ApiId = customersApi.Id, Scope = "customers.read", Description = "Read customer accounts" };

        var permInvRead = new Permission { ApiId = inventoryApi.Id, Scope = "inventory.read", Description = "Read inventory levels" };
        var permInvWrite = new Permission { ApiId = inventoryApi.Id, Scope = "inventory.write", Description = "Modify inventory levels" };

        db.Permissions.AddRange(permOrdersRead, permOrdersWrite, permCustRead, permInvRead, permInvWrite);
        await db.SaveChangesAsync();

        // 3. Clients
        var clientA = new Client
        {
            ClientId = "api-a",
            ClientName = "Service API A",
            SecretHash = hasher.HashSecret("secret-api-a-123")
        };
        var clientB = new Client
        {
            ClientId = "api-b",
            ClientName = "Service API B",
            SecretHash = hasher.HashSecret("secret-api-b-456")
        };
        var clientNone = new Client
        {
            ClientId = "api-no-access",
            ClientName = "Untrusted Client",
            SecretHash = hasher.HashSecret("secret-none-789")
        };

        db.Clients.AddRange(clientA, clientB, clientNone);
        await db.SaveChangesAsync();

        // 4. Grants
        // api-a has orders.read, inventory.read
        db.Grants.AddRange(
            new Grant { ClientId = clientA.Id, PermissionId = permOrdersRead.Id },
            new Grant { ClientId = clientA.Id, PermissionId = permInvRead.Id }
        );

        // api-b has orders.read, orders.write, customers.read
        db.Grants.AddRange(
            new Grant { ClientId = clientB.Id, PermissionId = permOrdersRead.Id },
            new Grant { ClientId = clientB.Id, PermissionId = permOrdersWrite.Id },
            new Grant { ClientId = clientB.Id, PermissionId = permCustRead.Id }
        );

        // 5. Initial Audit Entries
        db.AuditEntries.Add(new AuditEntry
        {
            Actor = "system-seeder",
            Action = "INITIAL_SEED",
            EntityType = "System",
            EntityId = "seed",
            DetailsJson = "{\"message\":\"Initial seed of default APIs, permissions, and clients created.\"}"
        });

                await db.SaveChangesAsync();

        await SeedDefaultRoutesAsync(db);
    }

    /// <summary>
    /// The project uses EnsureCreated (no migrations), which never alters an existing database.
    /// For SQLite databases created before the route registry existed, create the missing table.
    /// </summary>
    private static async Task EnsureRouteTableAsync(AuthDbContext db)
    {
        if (!db.Database.IsSqlite()) return;

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""ApiRoutes"" (
    ""Id"" TEXT NOT NULL CONSTRAINT ""PK_ApiRoutes"" PRIMARY KEY,
    ""ApiId"" TEXT NOT NULL,
    ""Method"" TEXT NOT NULL,
    ""PathPattern"" TEXT NOT NULL,
    ""PermissionId"" TEXT NULL,
    ""Description"" TEXT NOT NULL,
    ""IsActive"" INTEGER NOT NULL,
    CONSTRAINT ""FK_ApiRoutes_Apis_ApiId"" FOREIGN KEY (""ApiId"") REFERENCES ""Apis"" (""Id"") ON DELETE CASCADE,
    CONSTRAINT ""FK_ApiRoutes_Permissions_PermissionId"" FOREIGN KEY (""PermissionId"") REFERENCES ""Permissions"" (""Id"") ON DELETE RESTRICT
)");

        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_ApiRoutes_ApiId_Method_PathPattern\" ON \"ApiRoutes\" (\"ApiId\", \"Method\", \"PathPattern\")");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_ApiRoutes_PermissionId\" ON \"ApiRoutes\" (\"PermissionId\")");
    }

    /// <summary>
    /// Registers the routes exposed by the three sample APIs, each tied to the scope it requires.
    /// Only runs while the route registry is empty, so admin edits are never overwritten.
    /// </summary>
    private static async Task SeedDefaultRoutesAsync(AuthDbContext db)
    {
        if (await db.ApiRoutes.AnyAsync()) return;

        var defaults = new (string Audience, string Method, string Path, string? Scope, string Description)[]
        {
            ("https://orders.internal", "GET", "/health", null, "Health check (public)"),
            ("https://orders.internal", "GET", "/orders", "orders.read", "List orders"),
            ("https://orders.internal", "GET", "/orders/{id}", "orders.read", "Get one order"),
            ("https://orders.internal", "POST", "/orders", "orders.write", "Create an order"),

            ("https://customers.internal", "GET", "/health", null, "Health check (public)"),
            ("https://customers.internal", "GET", "/customers", "customers.read", "List customers"),
            ("https://customers.internal", "GET", "/customers/{id}", "customers.read", "Get one customer"),

            ("https://inventory.internal", "GET", "/health", null, "Health check (public)"),
            ("https://inventory.internal", "GET", "/inventory", "inventory.read", "List inventory"),
            ("https://inventory.internal", "PUT", "/inventory/{id}", "inventory.write", "Update stock level"),
        };

        var apis = await db.Apis.ToDictionaryAsync(a => a.Audience);
        var permissions = await db.Permissions.ToListAsync();

        foreach (var d in defaults)
        {
            if (!apis.TryGetValue(d.Audience, out var api)) continue;

            string? permissionId = null;
            if (d.Scope != null)
            {
                var perm = permissions.FirstOrDefault(p => p.ApiId == api.Id && p.Scope == d.Scope);
                if (perm == null) continue;
                permissionId = perm.Id;
            }

            db.ApiRoutes.Add(new ApiRoute
            {
                ApiId = api.Id,
                Method = d.Method,
                PathPattern = d.Path,
                PermissionId = permissionId,
                Description = d.Description
            });
        }

                if (!db.ChangeTracker.Entries<ApiRoute>().Any()) return;

        db.AuditEntries.Add(new AuditEntry
        {
            Actor = "system-seeder",
            Action = "SEED_ROUTES",
            EntityType = "System",
            EntityId = "seed-routes",
            DetailsJson = "{\"message\":\"Initial route registry created for the sample APIs.\"}"
        });

        await db.SaveChangesAsync();
    }
}
