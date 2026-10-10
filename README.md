# Local Federated Identity & Access Management Service

A complete local solution demonstrating how a central authentication and authorization service works in **.NET 10** using Minimal APIs, Entity Framework Core with SQLite, and a modern **React + TypeScript + Vite** Admin UI.

---

## Architecture Overview

```
                      ┌──────────────────────────────────────┐
                      │          Admin UI (Vite)             │
                      │       http://localhost:5173          │
                      │  (OIDC Auth Code Flow with PKCE)     │
                      └──────────────────┬───────────────────┘
                                         │ OIDC / REST
                                         ▼
                      ┌──────────────────────────────────────┐
                      │          AuthService                 │
                      │       http://localhost:5001          │
                      │  - OAuth 2.0 /oauth/token (RS256)    │
                      │  - JWKS: /.well-known/jwks.json      │
                      │  - Central Check: POST /check        │
                      │  - Mock OIDC: /mock-oidc/*           │
                      │  - SQLite DB (PBKDF2 Hashed Secrets) │
                      └──────────────────┬───────────────────┘
                                         │
                 ┌───────────────────────┼───────────────────────┐
                 │ JWKS / POST /check    │ JWKS / POST /check    │ JWKS / POST /check
                 ▼                       ▼                       ▼
     ┌───────────────────────┐ ┌───────────────────┐ ┌───────────────────────┐
     │      OrdersApi        │ │   CustomersApi    │ │     InventoryApi      │
     │ :5002 (orders.read/   │ │ :5003 (customers. │ │ :5004 (inventory.read/│
     │        orders.write)  │ │        read)      │ │        inventory.write│
     └───────────▲───────────┘ └─────────▲─────────┘ └───────────▲───────────┘
                 │                       │                       │
                 └───────────────────────┴───────────────────────┘
                                         │ Bearer Token
                               ┌─────────┴─────────┐
                               │    Test Client    │
                               │  "API A" Runner   │
                               │ (Token Caching)   │
                               └───────────────────┘
```

---

## Quick Start (Run Everything with One Command)

To build and launch all 4 backend services and the Admin UI simultaneously:

```powershell
./scripts/run-all.ps1
```

Once launched:
- **Admin UI**: [http://localhost:5173](http://localhost:5173)
- **AuthService**: [http://localhost:5001](http://localhost:5001)
- **OrdersApi**: [http://localhost:5002](http://localhost:5002)
- **CustomersApi**: [http://localhost:5003](http://localhost:5003)
- **InventoryApi**: [http://localhost:5004](http://localhost:5004)

To gracefully stop all services:
```powershell
./scripts/stop-all.ps1
```

---

## Running the API A Simulation Client

Execute the automated client to see allowed calls, 401 unauthenticated calls, 403 forbidden calls, and token caching in action:

```powershell
dotnet run --project src/TestClient/Federated.TestClient/Federated.TestClient.csproj
```

---

## Running the Test Suite

```powershell
dotnet test
```
All 9 unit tests cover:
- PBKDF2 client secret hashing and verification
- Grant assignment, active scope resolution, and revocation
- Soft-disabled clients and inactive APIs
- RS256 token issuance with custom claims, audiences, and scopes
- Cryptographic signature validation against RSA public keys
- Token expiration detection
- Audience mismatch enforcement
- `ScopeHandler` policy evaluation

---

## Two Authorization Modes (Dual Mode for Learning)

Each test API can be toggled between two architectural patterns in its `appsettings.json` (or via command line):

```json
"Authentication": {
  "Authority": "http://localhost:5001",
  "Audience": "https://orders.internal",
  "AuthMode": "Jwt"   // Options: "Jwt" or "CentralCheck"
}
```

### Mode 1: Local JWT Validation (`AuthMode: "Jwt"`)
- Downstream APIs fetch and cache the RSA public keys from `AuthService` via JWKS (`/.well-known/jwks.json`).
- Signature, expiration, and audience checks run in-memory inside the API in microseconds.
- **Tradeoff**: Very fast and resilient against AuthService downtime, but grant revocations take effect only after the token expires (cache latency).

### Mode 2: Central Evaluation (`AuthMode: "CentralCheck"`)
- Downstream APIs forward every incoming call to `AuthService`'s `POST /check` endpoint: *"May this client execute this method on this route?"*
- **Tradeoff**: Revocations in the Admin UI are **instantaneous** across all services, but introduces an extra network hop per call and makes AuthService a direct runtime dependency.

---

## Admin UI Screens

1. **Clients**: Register client applications with high-entropy secrets (displayed once, saved as PBKDF2 hashes), disable/enable clients, rotate secrets.
2. **APIs & Scopes**: Register resource servers and define granular OAuth scopes.
3. **Access Matrix**: Grid mapping Clients (rows) to Scopes (columns). Toggling a grant triggers a **confirmation modal displaying the exact diff** before applying.
4. **Access Checker**: Interactive policy simulator to test if any `(Client, API, Route, Method)` is allowed or denied and why.
5. **Audit Log**: Immutable chronological timeline of who changed what, when, and the JSON diff details.

---

## Role-Based Access Control & OneLogin Sign-In

- **Enterprise OIDC Flow**: Follows OpenID Connect Authorization Code Flow with PKCE (RFC 7636).
- **Roles**:
  - `access-admin`: Full access to create, edit, toggle grants, and rotate secrets.
  - `viewer`: Read-only access; all mutation controls are disabled with informative tooltips.
- **Built-in Mock OIDC Provider**: Allows 1-click switching between Alice Admin and Bob Viewer directly in the navbar.
- **OneLogin Integration**: Simply update `src/AdminUI/src/auth/oidcConfig.ts` with your OneLogin domain and Client ID to authenticate against real OneLogin.
