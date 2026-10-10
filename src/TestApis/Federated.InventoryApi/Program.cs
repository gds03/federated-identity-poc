using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Federated.Common;

var builder = WebApplication.CreateBuilder(args);

var authority = builder.Configuration["Authentication:Authority"] ?? "http://localhost:5001";
var audience = builder.Configuration["Authentication:Audience"] ?? "https://inventory.internal";

// Configure JWT Bearer Authentication
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
    options.AddPolicy("inventory.read", policy => policy.RequireScope("inventory.read"));
    options.AddPolicy("inventory.write", policy => policy.RequireScope("inventory.write"));
});

var authMode = builder.Configuration["Authentication:AuthMode"] ?? "Jwt";

var app = builder.Build();

app.Logger.LogInformation("InventoryApi initialized with AuthMode: {AuthMode}", authMode);

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

var items = new List<InventoryItemDto>
{
    new(1, "SKU-WIDGET-01", "Precision Widget A", 150),
    new(2, "SKU-GADGET-02", "Titanium Gadget B", 45),
    new(3, "SKU-DEVICE-03", "Control Sensor C", 210)
};

// 1. Health check (Anonymous)
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "InventoryApi" }));

// 2. GET /inventory (requires scope: inventory.read)
app.MapGet("/inventory", (ClaimsPrincipal user, ILogger<Program> logger) =>
{
    var clientId = user.FindFirst("client_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    logger.LogInformation("Caller {ClientId} accessed GET /inventory", clientId);
    return Results.Ok(items);
}).RequireAuthorization("inventory.read");

// 3. PUT /inventory/{id} (requires scope: inventory.write)
app.MapPut("/inventory/{id:int}", (int id, UpdateStockDto update, ClaimsPrincipal user, ILogger<Program> logger) =>
{
    var clientId = user.FindFirst("client_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var item = items.FirstOrDefault(i => i.Id == id);
    if (item == null)
    {
        return Results.NotFound(new { message = $"Inventory item {id} not found." });
    }

    item.StockLevel = update.StockLevel;
    logger.LogInformation("Caller {ClientId} updated stock for SKU {Sku} to {Stock}", clientId, item.Sku, item.StockLevel);
    return Results.Ok(item);
}).RequireAuthorization("inventory.write");

app.Run();

public class InventoryItemDto
{
    public int Id { get; set; }
    public string Sku { get; set; }
    public string Name { get; set; }
    public int StockLevel { get; set; }

    public InventoryItemDto(int id, string sku, string name, int stockLevel)
    {
        Id = id;
        Sku = sku;
        Name = name;
        StockLevel = stockLevel;
    }
}

public record UpdateStockDto(int StockLevel);
