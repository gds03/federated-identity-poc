# Federated.Common Library

Shared library providing reusable middleware, authorization handlers, and contract DTOs across the Federated Identity microservices.

---

## Components

### 1. `TransactionIdMiddleware` (`app.UseTransactionId()`)
- **Configured**: Runs **once** at application startup in `Program.cs`.
- **On every request**:
  1. Inspects incoming HTTP headers for `X-Correlation-Id`.
  2. If absent, generates a new GUID (`Guid.NewGuid().ToString("N")`).
  3. Sets `Response.Headers["X-Correlation-Id"]` so callers receive the traceable ID.
  4. Wraps request execution inside `_logger.BeginScope("CorrelationId")`, making the ID automatically appear in every log entry emitted during that request.

---

### 2. `ScopeAuthorization` (`ScopeHandler` & `ScopeRequirement`)
- **Configured**: Registered as an `IAuthorizationHandler` singleton in `Program.cs`.
- **Extension Method**:
  ```csharp
  policy.RequireScope("orders.read");
  ```
- **On every request**:
  1. When an endpoint is marked with `.RequireAuthorization("orders.read")`, ASP.NET Core invokes `ScopeHandler.HandleRequirementAsync`.
  2. `ScopeHandler` extracts all `scope` claims from `context.User`.
  3. It splits space-delimited OAuth scopes (`orders.read inventory.read`).
  4. If the required scope is present, it calls `context.Succeed(requirement)`.
  5. If the scope is missing, `context.Succeed` is never called, causing ASP.NET Core to immediately terminate the request with **403 Forbidden**. The endpoint handler is **never executed**.

---

### 3. `CentralCheckMiddleware` (`app.UseCentralCheck()`)
- **Configured**: Called **once** on startup when `"AuthMode": "CentralCheck"`.
- **On every request**:
  1. Skips anonymous `/health` routes.
  2. Requires the `Authorization` Bearer token (the `X-Client-Id` header is no longer accepted; identity comes only from the verified token).
  3. Sends `POST http://localhost:5001/check` carrying:
     ```json
     {
       "token": "Bearer eyJhbGci...",
       "audience": "https://orders.internal",
       "route": "/orders",
       "method": "GET"
     }
     ```
  4. Passes the current `X-Correlation-Id` header in the internal call so the trace continues through `AuthService`.
  5. **If denied**: Immediately writes a `403 Forbidden` JSON response with the failure reason and short-circuits the pipeline (the route handler never runs).
  6. **If allowed**: Constructs a `ClaimsPrincipal` with `client_id` and `scope` claims, assigns it to `context.User`, and invokes `_next(context)` to allow execution to proceed.
