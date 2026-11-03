using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Federated.Common;

var builder = WebApplication.CreateBuilder(args);

var authority = builder.Configuration["Authentication:Authority"] ?? "http://localhost:5001";
var audience = builder.Configuration["Authentication:Audience"] ?? "https://customers.internal";

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
    options.AddPolicy("customers.read", policy => policy.RequireScope("customers.read"));
});

var authMode = builder.Configuration["Authentication:AuthMode"] ?? "Jwt";

var app = builder.Build();

app.Logger.LogInformation("CustomersApi initialized with AuthMode: {AuthMode}", authMode);

app.UseTransactionId();

if (authMode.Equals("CentralCheck", StringComparison.OrdinalIgnoreCase))
{
    app.UseCentralCheck(audience);
}
else
{
    app.UseAuthentication();
    app.UseAuthorization();
}

var customers = new List<CustomerDto>
{
    new(1, "Alice Smith", "alice@example.com", "Tier 1 Enterprise"),
    new(2, "Bob Jones", "bob@example.com", "Tier 2 MidMarket"),
    new(3, "Charlie Brown", "charlie@example.com", "Tier 3 Standard")
};

// 1. Health check (Anonymous)
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "CustomersApi" }));

// 2. GET /customers (requires scope: customers.read)
app.MapGet("/customers", (ClaimsPrincipal user, ILogger<Program> logger) =>
{
    var clientId = user.FindFirst("client_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    logger.LogInformation("Caller {ClientId} accessed GET /customers", clientId);
    return Results.Ok(customers);
}).RequireAuthorization("customers.read");

// 3. GET /customers/{id} (requires scope: customers.read)
app.MapGet("/customers/{id:int}", (int id, ClaimsPrincipal user) =>
{
    var cust = customers.FirstOrDefault(c => c.Id == id);
    return cust != null ? Results.Ok(cust) : Results.NotFound(new { message = $"Customer {id} not found." });
}).RequireAuthorization("customers.read");

app.Run();

public record CustomerDto(int Id, string Name, string Email, string Tier);
