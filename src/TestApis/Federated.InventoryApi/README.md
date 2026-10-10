# InventoryApi (`Federated.InventoryApi`)

Resource server exposing inventory stock queries and updates (`GET /inventory`, `PUT /inventory/{id}`).

---

## 1. Startup Configuration (Runs ONCE at process boot)

When `InventoryApi` starts up:
1. **Service Registration**:
   Registers the singleton `ScopeHandler` and two distinct authorization policies:
   - Policy `"inventory.read"` requiring scope `inventory.read`
   - Policy `"inventory.write"` requiring scope `inventory.write`
2. **Middleware Pipeline**:
   `app.UseTransactionId()` is registered, followed by the selected `AuthMode` (`Jwt` or `CentralCheck`).
3. **Route Policies Applied**:
   ```csharp
   app.MapGet("/inventory", ...).RequireAuthorization("inventory.read");
   app.MapPut("/inventory/{id:int}", ...).RequireAuthorization("inventory.write");
   ```

---

## 2. Request Execution Path & Read vs. Write Protection

Here is what happens when two different requests land on `InventoryApi`:

```mermaid
flowchart TD
    Client["Client: api-a\n(Has grant: inventory.read\nLacks grant: inventory.write)"]

    Client -->|GET /inventory| R1["Request 1: Read Stock"]
    Client -->|PUT /inventory/1| R2["Request 2: Update Stock"]

    subgraph Pipeline ["InventoryApi Request Pipeline"]
        Tx["TransactionIdMiddleware (Captures X-Correlation-Id)"]
        Auth["Authentication (Validates Signature & aud: https://inventory.internal)"]
        
        P1{"Policy: inventory.read"}
        P2{"Policy: inventory.write"}
        
        H1["GET Handler: Returns Inventory Items"]
        H2["PUT Handler: Updates DB Stock Level"]
    end

    R1 --> Tx --> Auth --> P1
    P1 -- "Scope present in token" --> H1 --> Res1["200 OK"]

    R2 --> Tx --> Auth --> P2
    P2 -- "Scope missing in token!" --> Deny2["403 Forbidden\n(Handler aborted before stock update!)"]
```

### Key Safety Guarantee:
Because `.RequireAuthorization("inventory.write")` executes in the middleware pipeline **before** the lambda endpoint:
- When a client lacks `inventory.write`, the route handler **never runs**.
- Database state and stock numbers can never be modified by callers lacking write permission.
