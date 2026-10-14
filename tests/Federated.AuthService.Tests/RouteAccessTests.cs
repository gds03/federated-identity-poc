using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Federated.AuthService.Data;
using Federated.AuthService.Models;
using Federated.AuthService.Services;
using Xunit;

namespace Federated.AuthService.Tests;

public class RouteAccessTests
{
    private const string OrdersAud = "https://orders.internal";

    private static AuthDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AuthDbContext(options);
    }

    private sealed record Fixture(AuthDbContext Db, Api Api, Permission Read, Permission Write, Client Reader);

    /// <summary>Orders API with read/write scopes and routes; "reader" holds only orders.read.</summary>
    private static async Task<Fixture> BuildAsync()
    {
        var db = CreateDb();

        var api = new Api { Name = "OrdersApi", Audience = OrdersAud };
        var read = new Permission { ApiId = api.Id, Scope = "orders.read" };
        var write = new Permission { ApiId = api.Id, Scope = "orders.write" };
        var reader = new Client { ClientId = "reader", ClientName = "Reader", SecretHash = "x" };

        db.Apis.Add(api);
        db.Permissions.AddRange(read, write);
        db.Clients.Add(reader);
        db.Grants.Add(new Grant { ClientId = reader.Id, PermissionId = read.Id });

        db.ApiRoutes.AddRange(
            new ApiRoute { ApiId = api.Id, Method = "GET", PathPattern = "/health", PermissionId = null },
            new ApiRoute { ApiId = api.Id, Method = "GET", PathPattern = "/orders", PermissionId = read.Id },
            new ApiRoute { ApiId = api.Id, Method = "GET", PathPattern = "/orders/{id}", PermissionId = read.Id },
            new ApiRoute { ApiId = api.Id, Method = "POST", PathPattern = "/orders", PermissionId = write.Id });

        await db.SaveChangesAsync();
        return new Fixture(db, api, read, write, reader);
    }

    // ---------- pattern matching ----------

    [Theory]
    [InlineData("/orders", "/orders", true)]
    [InlineData("/orders", "/ORDERS/", true)]
    [InlineData("/orders", "/orders?x=1", true)]
    [InlineData("/orders", "/orders/1", false)]
    [InlineData("/orders/{id}", "/orders/42", true)]
    [InlineData("/orders/{id}", "/orders", false)]
    [InlineData("/orders/{id}", "/orders/42/items", false)]
    [InlineData("/orders/*", "/orders/42", true)]
    [InlineData("/reports/**", "/reports", true)]
    [InlineData("/reports/**", "/reports/2024/q1", true)]
    [InlineData("/reports/**", "/other", false)]
    [InlineData("/", "/", true)]
    [InlineData("/", "/orders", false)]
    public void PatternMatcher_MatchesAsDocumented(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, RoutePatternMatcher.TryMatch(pattern, path, out _));
    }

    [Fact]
    public void PatternMatcher_PrefersLiteralOverParameterOverWildcard()
    {
        RoutePatternMatcher.TryMatch("/orders/export", "/orders/export", out int literal);
        RoutePatternMatcher.TryMatch("/orders/{id}", "/orders/export", out int param);
        RoutePatternMatcher.TryMatch("/orders/**", "/orders/export", out int catchAll);

        Assert.True(literal > param);
        Assert.True(param > catchAll);
    }

    [Theory]
    [InlineData("/orders", true)]
    [InlineData("/orders/{id}", true)]
    [InlineData("/reports/**", true)]
    [InlineData("orders", false)]
    [InlineData("", false)]
    [InlineData("/a/**/b", false)]
    [InlineData("/or ders", false)]
    [InlineData("/orders/{id", false)]
    [InlineData("/ord*", false)]
    [InlineData("/orders?x=1", false)]
    public void PatternMatcher_ValidatesPatterns(string pattern, bool valid)
    {
        Assert.Equal(valid, RoutePatternMatcher.Validate(pattern) == null);
    }

    // ---------- evaluation ----------

    [Fact]
    public async Task Allows_WhenRouteMatchesAndClientHoldsRequiredScope()
    {
        var f = await BuildAsync();
        var result = await new RouteAccessEvaluator(f.Db).EvaluateAsync("reader", OrdersAud, "/orders/7", "GET");

        Assert.True(result.Allowed);
        Assert.Equal("orders.read", result.RequiredScope);
        Assert.Equal("GET /orders/{id}", result.MatchedRoute);
    }

    [Fact]
    public async Task Denies_WhenClientLacksScopeRequiredByRoute()
    {
        var f = await BuildAsync();
        var result = await new RouteAccessEvaluator(f.Db).EvaluateAsync("reader", OrdersAud, "/orders", "POST");

        Assert.False(result.Allowed);
        Assert.Equal("orders.write", result.RequiredScope);
    }

    [Fact]
    public async Task Denies_ByDefault_WhenNoRouteIsRegistered()
    {
        var f = await BuildAsync();
        var evaluator = new RouteAccessEvaluator(f.Db);

        // Method not registered for this path, and path not registered at all
        Assert.False((await evaluator.EvaluateAsync("reader", OrdersAud, "/orders/7", "DELETE")).Allowed);
        Assert.False((await evaluator.EvaluateAsync("reader", OrdersAud, "/admin/secrets", "GET")).Allowed);
    }

    [Fact]
    public async Task PublicRoute_IsAllowedForAnyActiveClient()
    {
        var f = await BuildAsync();
        var result = await new RouteAccessEvaluator(f.Db).EvaluateAsync("reader", OrdersAud, "/health", "GET");

        Assert.True(result.Allowed);
        Assert.Equal("none", result.RequiredScope);
    }

    [Fact]
    public async Task Denies_WhenRouteIsDisabled()
    {
        var f = await BuildAsync();
        var route = await f.Db.ApiRoutes.FirstAsync(r => r.PathPattern == "/orders" && r.Method == "GET");
        route.IsActive = false;
        await f.Db.SaveChangesAsync();

        var result = await new RouteAccessEvaluator(f.Db).EvaluateAsync("reader", OrdersAud, "/orders", "GET");
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Denies_WhenRequiredPermissionIsDisabled()
    {
        var f = await BuildAsync();
        f.Read.IsActive = false;
        await f.Db.SaveChangesAsync();

        var result = await new RouteAccessEvaluator(f.Db).EvaluateAsync("reader", OrdersAud, "/orders", "GET");
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Denies_WhenGrantIsRevoked()
    {
        var f = await BuildAsync();
        var evaluator = new RouteAccessEvaluator(f.Db);
        Assert.True((await evaluator.EvaluateAsync("reader", OrdersAud, "/orders", "GET")).Allowed);

        f.Db.Grants.RemoveRange(f.Db.Grants.Where(g => g.ClientId == f.Reader.Id));
        await f.Db.SaveChangesAsync();

        Assert.False((await new RouteAccessEvaluator(f.Db).EvaluateAsync("reader", OrdersAud, "/orders", "GET")).Allowed);
    }

    [Fact]
    public async Task Denies_UnknownOrDisabledClient_UnknownOrInactiveApi_MissingAudience()
    {
        var f = await BuildAsync();
        var evaluator = new RouteAccessEvaluator(f.Db);

        Assert.False((await evaluator.EvaluateAsync("ghost", OrdersAud, "/orders", "GET")).Allowed);
        Assert.False((await evaluator.EvaluateAsync("reader", "https://nope.internal", "/orders", "GET")).Allowed);
        Assert.False((await evaluator.EvaluateAsync("reader", "", "/orders", "GET")).Allowed);

        f.Api.IsActive = false;
        await f.Db.SaveChangesAsync();
        Assert.False((await new RouteAccessEvaluator(f.Db).EvaluateAsync("reader", OrdersAud, "/orders", "GET")).Allowed);

        f.Api.IsActive = true;
        f.Reader.IsActive = false;
        await f.Db.SaveChangesAsync();
        Assert.False((await new RouteAccessEvaluator(f.Db).EvaluateAsync("reader", OrdersAud, "/orders", "GET")).Allowed);
    }

    [Fact]
    public async Task MoreSpecificRoute_WinsOverCatchAll()
    {
        var f = await BuildAsync();
        // Catch-all needs write; the specific export route is public-read
        f.Db.ApiRoutes.AddRange(
            new ApiRoute { ApiId = f.Api.Id, Method = "*", PathPattern = "/orders/**", PermissionId = f.Write.Id },
            new ApiRoute { ApiId = f.Api.Id, Method = "GET", PathPattern = "/orders/export", PermissionId = f.Read.Id });
        await f.Db.SaveChangesAsync();

        var result = await new RouteAccessEvaluator(f.Db).EvaluateAsync("reader", OrdersAud, "/orders/export", "GET");

        Assert.True(result.Allowed);
        Assert.Equal("GET /orders/export", result.MatchedRoute);
    }

    [Fact]
    public async Task EffectiveRoutes_ReflectClientGrants()
    {
        var f = await BuildAsync();
        var routes = await new RouteAccessEvaluator(f.Db).GetEffectiveRoutesAsync(f.Reader);

        Assert.True(routes.Single(r => r.Method == "GET" && r.PathPattern == "/orders").Allowed);
        Assert.True(routes.Single(r => r.PathPattern == "/health").Allowed);
        Assert.False(routes.Single(r => r.Method == "POST" && r.PathPattern == "/orders").Allowed);
    }
}
