# OrdersApi (`Federated.OrdersApi`)

Resource server exposing protected orders endpoints (`GET /orders`, `GET /orders/{id}`, `POST /orders`).

---

## 1. How It Gets Configured on Application Startup (Runs ONCE)

When the process boots, ASP.NET Core executes `Program.cs` from top to bottom before accepting any incoming HTTP traffic:

```mermaid
flowchart TD
    Start["Application Startup (dotnet run)"] --> DI["1. Register DI Services\n• ScopeHandler singleton\n• Authorization policies ('orders.read', 'orders.write')\n• AddAuthentication(JwtBearer)"]
    DI --> Build["2. app = builder.Build()"]
    Build --> MW["3. Configure Middleware Pipeline (runs once!)\n• UseTransactionId()\n• Branch on AuthMode: CentralCheck vs Jwt"]
    MW --> Routes["4. Map Endpoint Routes\n• GET /orders (.RequireAuthorization('orders.read'))\n• POST /orders (.RequireAuthorization('orders.write'))"]
    Routes --> Listen["5. app.Run() (Listening on :5002)"]
```

### The Middleware Configuration Decision
```csharp
var authMode = builder.Configuration["Authentication:AuthMode"] ?? "Jwt";

if (authMode.Equals("CentralCheck", StringComparison.OrdinalIgnoreCase))
{
    // SETUP 1: Register CentralCheckMiddleware into the HTTP pipeline
    app.UseCentralCheck();
}
else
{
    // SETUP 2: Register standard ASP.NET Core JWT Bearer pipeline
    app.UseAuthentication();
    app.UseAuthorization();
}
```
> **Key concept**: `app.UseCentralCheck()` or `app.UseAuthentication()` is called **only once** at startup. This wires up the delegate chain that every subsequent incoming request travels through.

---

## 2. Request Execution Path (Runs on EVERY Incoming Request)

When a request arrives (e.g. `GET /orders`), here is the exact path it travels:

```mermaid
sequenceDiagram
    autonumber
    participant Caller as Calling Client
    participant TxMW as TransactionIdMiddleware
    participant CheckMW as CentralCheckMiddleware
    participant Auth as AuthService (:5001)
    participant ScopeAuth as ScopeHandler / Policy Check
    participant Endpoint as MapGet('/orders')

    Caller->>TxMW: GET /orders [Authorization: Bearer <token>, X-Correlation-Id]
    TxMW->>TxMW: Captures or generates X-Correlation-Id & begins logging scope
    
    alt Mode = CentralCheck
        TxMW->>CheckMW: Forwards HttpContext
        CheckMW->>Auth: POST /check { route: "/orders", method: "GET", token: "..." }
        Auth-->>CheckMW: 200 OK { allowed: true, clientId: "api-a", scopes: ["orders.read"] }
        Note over CheckMW: Creates ClaimsPrincipal(client_id, scope) on context.User
        CheckMW->>ScopeAuth: Passes request down pipeline
    else Mode = Jwt Bearer
        TxMW->>ScopeAuth: Decrypts JWT locally via cached JWKS, checks aud & exp
    end

    ScopeAuth->>ScopeAuth: Checks if context.User possesses required scope ('orders.read')
    alt Scope Absent
        ScopeAuth-->>Caller: 403 Forbidden (Handler aborts!)
    else Scope Present
        ScopeAuth->>Endpoint: Invokes endpoint handler
        Endpoint-->>Caller: 200 OK [Orders JSON]
    end
```

### Step-by-Step Path Detail:
1. **`TransactionIdMiddleware`**:
   - Inspects `X-Correlation-Id`. If present, keeps it; if absent, creates `Guid.NewGuid()`.
   - Attaches it to response headers and logging context so every subsequent log shares the ID.
2. **Authorization Layer**:
   - In **`CentralCheck`** mode: `CentralCheckMiddleware` sends the request details to `AuthService/check`. If `AuthService` says denied, it immediately responds with `403 Forbidden` and the endpoint code **never runs**. If allowed, it populates `context.User` with claims.
   - In **`Jwt`** mode: ASP.NET Core validates token signature against `AuthService` public keys, validates audience `https://orders.internal` (401 if missing), and extracts claims.
3. **`ScopeHandler` Policy Evaluation**:
   - The route specifies `.RequireAuthorization("orders.read")`.
   - `ScopeHandler` inspects the `scope` claims on `context.User`.
   - If the token lacks `orders.read`, ASP.NET Core halts the request and returns **`403 Forbidden`**. The route handler is never executed.
4. **Endpoint Execution**:
   - Only when all security gates succeed, the lambda handler executes and returns order data.
