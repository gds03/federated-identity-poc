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

        if (await db.Apis.AnyAsync())
        {
            return; // Already seeded
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

                        // 6. Routes exposed by the sample APIs, each tied to the scope it requires
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

                var apis = new[] { ordersApi, customersApi, inventoryApi }.ToDictionary(a => a.Audience);
        var permissions = new[] { permOrdersRead, permOrdersWrite, permCustRead, permInvRead, permInvWrite };

        foreach (var d in defaults)
        {
            var api = apis[d.Audience];
            string? permissionId = d.Scope == null
                ? null
                : permissions.Single(p => p.ApiId == api.Id && p.Scope == d.Scope).Id;

            db.ApiRoutes.Add(new ApiRoute
            {
                ApiId = api.Id,
                Method = d.Method,
                PathPattern = d.Path,
                PermissionId = permissionId,
                Description = d.Description
            });
        }

        await db.SaveChangesAsync();
    }
}
