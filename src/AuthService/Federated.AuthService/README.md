# AuthService (`Federated.AuthService`)

Central authorization authority and OpenID Connect identity provider running on `http://localhost:5001`.

---

## 1. Startup Configuration (Runs ONCE)

When `AuthService` boots:
1. **Database Initialization**: Sets up SQLite DbContext (`FederatedAuth.db`) and seeds initial APIs, permissions, clients, and grants if empty.
2. **Cryptographic Key Service**: Generates or loads the persistent 2048-bit RSA private key (`keys/private-key.pem`) used to sign RS256 JWTs.
3. **CORS Policy**: Configured to allow Admin UI requests from `http://localhost:5173` and expose `X-Correlation-Id`.
4. **Middleware Pipeline**: Configured once with `app.UseCors()` and `app.UseTransactionId()`.
5. **Endpoint Mappings**:
   - `MapGet("/.well-known/openid-configuration")`: OIDC Discovery metadata.
   - `MapGet("/.well-known/jwks.json")`: Publishes public RSA keys (`n`, `e`, `kid`).
   - `MapPost("/oauth/token")`: Client credentials token issuance.
   - `MapCheckEndpoints()`: Handles real-time `/check` queries from test APIs.
   - `MapAdminEndpoints()`: REST API for Admin UI (clients, APIs, matrix, audit).
   - `MapMockOidcEndpoints()`: Local PKCE OIDC server for Admin UI admin login.

---

## 2. Request Handling Paths

### Path A: Client Credentials Grant (`POST /oauth/token`)
1. Client sends `client_id` and `client_secret` with `grant_type: "client_credentials"`.
2. `AuthService` looks up the client in SQLite, verifies `IsActive == true`.
3. Validates secret against `SecretHash` using **PBKDF2-SHA256**.
4. Traverses active relationships: `Client → Grants → Permissions → Apis`.
5. Issues a signed RS256 JWT containing:
   - `iss`: `"http://localhost:5001"`
   - `aud`: List of API audiences where client has at least one active grant
   - `scope`: Space-separated list of granted scopes (e.g. `"orders.read inventory.read"`)
   - `exp`: 3600 seconds (1 hour)

---

### Path B: Central Check (`POST /check`)
Called internally by downstream APIs using `CentralCheckMiddleware`:
1. Extracts `client_id` (either directly or by parsing the token).
2. Looks up live grants in SQLite right now.
3. Determines required scope based on incoming route and method (e.g. `GET /orders` → requires `orders.read`).
4. Checks if client has an active grant for that scope.
5. Returns JSON response:
   ```json
   {
     "allowed": true,
     "reason": "Access allowed. Client has active grant for 'orders.read'.",
     "clientId": "api-a",
     "requiredScope": "orders.read",
     "clientScopes": ["orders.read", "inventory.read"]
   }
   ```
   If denied, returns `"allowed": false` with the exact policy violation reason, prompting the caller API to reject the request with `403 Forbidden`.
