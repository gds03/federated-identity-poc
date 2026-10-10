using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Federated.Common;

namespace Federated.TestClient;

public class Program
{
    private static readonly HttpClient HttpClient = new();
    private static readonly TokenManager TokenManager = new(
        authUrl: "http://localhost:5001/oauth/token",
        clientId: "api-a",
        clientSecret: "secret-api-a-123"
    );

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        PrintHeader("FEDERATED IDENTITY TEST RUNNER (API A CLIENT - 3 APIS)");

        // 1. OrdersApi - Read (Allowed: api-a has orders.read)
        await RunScenario(
            title: "OrdersApi: GET /orders (Allowed)",
            description: "api-a calls OrdersApi with 'orders.read'.",
            expectedStatus: "200 OK",
            action: async (correlationId) =>
            {
                string token = await TokenManager.GetAccessTokenAsync(correlationId);
                var req = new HttpRequestMessage(HttpMethod.Get, "http://localhost:5002/orders");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Add(CommonConstants.CorrelationIdHeader, correlationId);
                return await HttpClient.SendAsync(req);
            });

        // 2. OrdersApi - Write (Denied: api-a lacks orders.write)
        await RunScenario(
            title: "OrdersApi: POST /orders (Denied)",
            description: "api-a attempts POST /orders without 'orders.write'.",
            expectedStatus: "403 Forbidden",
            action: async (correlationId) =>
            {
                string token = await TokenManager.GetAccessTokenAsync(correlationId);
                var req = new HttpRequestMessage(HttpMethod.Post, "http://localhost:5002/orders")
                {
                    Content = JsonContent.Create(new { CustomerName = "Exploit Corp", Amount = 9999.00 })
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Add(CommonConstants.CorrelationIdHeader, correlationId);
                return await HttpClient.SendAsync(req);
            });

        // 3. CustomersApi - Read (Denied: api-a lacks customers.read)
        await RunScenario(
            title: "CustomersApi: GET /customers (Denied)",
            description: "api-a attempts to read customers without 'customers.read'.",
            expectedStatus: "401 or 403 Forbidden/Unauthorized",
            action: async (correlationId) =>
            {
                string token = await TokenManager.GetAccessTokenAsync(correlationId);
                var req = new HttpRequestMessage(HttpMethod.Get, "http://localhost:5003/customers");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Add(CommonConstants.CorrelationIdHeader, correlationId);
                return await HttpClient.SendAsync(req);
            });

        // 4. InventoryApi - Read (Allowed: api-a has inventory.read)
        await RunScenario(
            title: "InventoryApi: GET /inventory (Allowed)",
            description: "api-a calls InventoryApi with 'inventory.read'.",
            expectedStatus: "200 OK",
            action: async (correlationId) =>
            {
                string token = await TokenManager.GetAccessTokenAsync(correlationId);
                var req = new HttpRequestMessage(HttpMethod.Get, "http://localhost:5004/inventory");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Add(CommonConstants.CorrelationIdHeader, correlationId);
                return await HttpClient.SendAsync(req);
            });

        // 5. InventoryApi - Update Stock (Denied: api-a lacks inventory.write)
        await RunScenario(
            title: "InventoryApi: PUT /inventory/1 (Denied)",
            description: "api-a attempts PUT /inventory/1 without 'inventory.write'.",
            expectedStatus: "403 Forbidden",
            action: async (correlationId) =>
            {
                string token = await TokenManager.GetAccessTokenAsync(correlationId);
                var req = new HttpRequestMessage(HttpMethod.Put, "http://localhost:5004/inventory/1")
                {
                    Content = JsonContent.Create(new { StockLevel = 999 })
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                req.Headers.Add(CommonConstants.CorrelationIdHeader, correlationId);
                return await HttpClient.SendAsync(req);
            });

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\nAll 3 API test scenarios completed!\n");
        Console.ResetColor();
    }

    private static async Task RunScenario(string title, string description, string expectedStatus, Func<string, Task<HttpResponseMessage>> action)
    {
        string correlationId = $"corr-{Guid.NewGuid():N}";
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n==================================================================");
        Console.WriteLine($" {title}");
        Console.WriteLine($"==================================================================");
        Console.ResetColor();
        Console.WriteLine($"Description: {description}");
        Console.WriteLine($"Expected   : {expectedStatus}");
        Console.WriteLine($"Correlation: {correlationId}");

        try
        {
            var response = await action(correlationId);
            string responseBody = await response.Content.ReadAsStringAsync();
            string returnedCorrelationId = response.Headers.TryGetValues(CommonConstants.CorrelationIdHeader, out var vals)
                ? string.Join(", ", vals)
                : "None";

            Console.Write("Actual     : ");
            if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
            {
                Console.ForegroundColor = ConsoleColor.Green;
            }
            else if ((int)response.StatusCode == 401)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
            }
            else if ((int)response.StatusCode == 403)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
            }

            Console.WriteLine($"{(int)response.StatusCode} {response.StatusCode}");
            Console.ResetColor();

            Console.WriteLine($"Returned Id: {returnedCorrelationId}");
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                var preview = responseBody.Length > 200 ? responseBody.Substring(0, 200) + "..." : responseBody;
                Console.WriteLine($"Body       : {preview.Trim()}");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine($"Call failed with exception: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static void PrintHeader(string text)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================================");
        Console.WriteLine($"   {text}");
        Console.WriteLine("==================================================================");
        Console.ResetColor();
    }
}

public class TokenManager
{
    private readonly HttpClient _http = new();
    private readonly string _authUrl;
    private readonly string _clientId;
    private readonly string _clientSecret;

    private string? _cachedToken;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;
    private readonly object _lock = new();

    public TokenManager(string authUrl, string clientId, string clientSecret)
    {
        _authUrl = authUrl;
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    public async Task<string> GetAccessTokenAsync(string correlationId)
    {
        lock (_lock)
        {
            if (_cachedToken != null && _expiresAtUtc > DateTimeOffset.UtcNow.AddSeconds(30))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[TokenManager] Reusing cached token (expires in {(_expiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds:F0}s).");
                Console.ResetColor();
                return _cachedToken;
            }
        }

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[TokenManager] Requesting new token from {_authUrl} for client '{_clientId}'...");
        Console.ResetColor();

        var request = new HttpRequestMessage(HttpMethod.Post, _authUrl)
        {
            Content = JsonContent.Create(new
            {
                grant_type = "client_credentials",
                client_id = _clientId,
                client_secret = _clientSecret
            })
        };
        request.Headers.Add(CommonConstants.CorrelationIdHeader, correlationId);

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponseDto>();
        if (tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.access_token))
        {
            throw new InvalidOperationException("Failed to obtain access token from response.");
        }

        lock (_lock)
        {
            _cachedToken = tokenResponse.access_token;
            _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.expires_in);
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"[TokenManager] Acquired new token. Scopes: [{tokenResponse.scope}], expires in {tokenResponse.expires_in}s.");
            Console.ResetColor();
        }

        return _cachedToken;
    }
}

public record TokenResponseDto(
    [property: JsonPropertyName("access_token")] string access_token,
    [property: JsonPropertyName("token_type")] string token_type,
    [property: JsonPropertyName("expires_in")] int expires_in,
    [property: JsonPropertyName("scope")] string scope);
