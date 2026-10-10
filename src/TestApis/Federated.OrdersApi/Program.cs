using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Federated.Common;

var builder = WebApplication.CreateBuilder(args);

var authority = builder.Configuration["Authentication:Authority"] ?? "http://localhost:5001";
var audience = builder.Configuration["Authentication:Audience"] ?? "https://orders.internal";

// Configure JWT Bearer Authentication (validates against AuthService JWKS)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

// Scope Authorization
builder.Services.AddSingleton<IAuthorizationHandler, ScopeHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("orders.read", policy => policy.RequireScope("orders.read"));
    options.AddPolicy("orders.write", policy => policy.RequireScope("orders.write"));
});

var authMode = builder.Configuration["Authentication:AuthMode"] ?? "Jwt";

var app = builder.Build();

app.Logger.LogInformation("OrdersApi initialized with AuthMode: {AuthMode}", authMode);

app.UseTransactionId();

if (authMode.Equals("CentralCheck", StringComparison.OrdinalIgnoreCase))
{
    app.UseCentralCheck();
}
else
{
    app.UseAuthentication();
    app.UseAuthorization();
}

// Sample Orders in memory
var orders = new List<OrderDto>
{
    new(1, "ORD-1001", "Acme Corp", 250.00m, "Processing"),
    new(2, "ORD-1002", "Globex Inc", 1250.50m, "Shipped"),
    new(3, "ORD-1003", "Soylent Corp", 89.99m, "Delivered")
};

// 1. Health check (Anonymous)
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "OrdersApi" }));

// 2. GET /orders (requires scope: orders.read)
app.MapGet("/orders", (ClaimsPrincipal user, ILogger<Program> logger) =>
{
    var clientId = user.FindFirst("client_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    logger.LogInformation("Caller {ClientId} accessed GET /orders", clientId);
    return Results.Ok(orders);
}).RequireAuthorization("orders.read");

// 3. GET /orders/{id} (requires scope: orders.read)
app.MapGet("/orders/{id:int}", (int id, ClaimsPrincipal user) =>
{
    var order = orders.FirstOrDefault(o => o.Id == id);
    return order != null ? Results.Ok(order) : Results.NotFound(new { message = $"Order {id} not found." });
}).RequireAuthorization("orders.read");

// 4. POST /orders (requires scope: orders.write)
app.MapPost("/orders", (CreateOrderDto newOrder, ClaimsPrincipal user, ILogger<Program> logger) =>
{
    var clientId = user.FindFirst("client_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var created = new OrderDto(orders.Count + 1, $"ORD-{2000 + orders.Count + 1}", newOrder.CustomerName, newOrder.Amount, "Created");
    orders.Add(created);
    logger.LogInformation("Caller {ClientId} created new order {OrderNumber}", clientId, created.OrderNumber);
    return Results.Created($"/orders/{created.Id}", created);
}).RequireAuthorization("orders.write");

app.Run();

public record OrderDto(int Id, string OrderNumber, string CustomerName, decimal Amount, string Status);
public record CreateOrderDto(string CustomerName, decimal Amount);
