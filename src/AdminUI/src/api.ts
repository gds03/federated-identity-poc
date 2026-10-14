import type {
  ClientItem,
  ClientCreatedResult,
  ApiItem,
  RouteItem,
  ClientRoutesResult,
  MatrixData,
  AccessCheckResult,
  AuditLogItem,
} from './types';

const BASE_URL = 'http://localhost:5001/api/admin';

function getCurrentActor(): string {
  try {
    const raw = sessionStorage.getItem('oidc_user');
    if (raw) {
      const u = JSON.parse(raw);
      if (u.email) return u.email;
    }
  } catch {}
  return 'alice.admin@internal';
}

// Helper to make API calls with transaction correlation tracking and OIDC bearer token
async function fetchJson<T>(url: string, options?: RequestInit): Promise<T> {
  const headers = new Headers(options?.headers || {});
  if (!headers.has('Content-Type') && options?.method && options.method !== 'GET') {
    headers.set('Content-Type', 'application/json');
  }

  const token = sessionStorage.getItem('oidc_token');
  if (token && !headers.has('Authorization')) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  headers.set('X-Correlation-Id', `ui-corr-${Math.random().toString(36).substring(2, 10)}`);

  const response = await fetch(url, { ...options, headers });
  if (!response.ok) {
    const errorText = await response.text();
    let message = `Request failed: ${response.status} ${response.statusText}`;
    try {
      const parsed = JSON.parse(errorText);
      if (parsed.message) message = parsed.message;
      else if (parsed.error_description) message = parsed.error_description;
    } catch {
      if (errorText) message = errorText;
    }
    throw new Error(message);
  }
  return response.json();
}

export const adminApi = {
  // Clients
  getClients: () => fetchJson<ClientItem[]>(`${BASE_URL}/clients`),
  createClient: (clientId: string, clientName: string, actor = getCurrentActor()) =>
    fetchJson<ClientCreatedResult>(`${BASE_URL}/clients`, {
      method: 'POST',
      body: JSON.stringify({ clientId, clientName, actor }),
    }),
  toggleClientStatus: (id: string, isActive: boolean, actor = getCurrentActor()) =>
    fetchJson<{ id: string; clientId: string; isActive: boolean }>(
      `${BASE_URL}/clients/${id}/toggle-status`,
      {
        method: 'POST',
        body: JSON.stringify({ isActive, actor }),
      }
    ),
  rotateSecret: (id: string, actor = getCurrentActor()) =>
    fetchJson<{ id: string; clientId: string; rawSecret: string }>(
      `${BASE_URL}/clients/${id}/rotate-secret`,
      {
        method: 'POST',
        body: JSON.stringify({ actor }),
      }
    ),

  // APIs & Permissions
  getApis: () => fetchJson<ApiItem[]>(`${BASE_URL}/apis`),
  createApi: (name: string, audience: string, description: string, actor = getCurrentActor()) =>
    fetchJson<ApiItem>(`${BASE_URL}/apis`, {
      method: 'POST',
      body: JSON.stringify({ name, audience, description, actor }),
    }),
  createPermission: (apiId: string, scope: string, description: string, actor = getCurrentActor()) =>
    fetchJson<any>(`${BASE_URL}/apis/${apiId}/permissions`, {
      method: 'POST',
      body: JSON.stringify({ scope, description, actor }),
    }),

    // Routes (which route of an API requires which permission)
  createRoute: (
    apiId: string,
    method: string,
    pathPattern: string,
    permissionId: string | null,
    description: string,
    actor = getCurrentActor()
  ) =>
    fetchJson<RouteItem>(`${BASE_URL}/apis/${apiId}/routes`, {
      method: 'POST',
      body: JSON.stringify({ method, pathPattern, permissionId, description, actor }),
    }),
  updateRoute: (
    id: string,
    route: { method: string; pathPattern: string; permissionId: string | null; description: string; isActive: boolean },
    actor = getCurrentActor()
  ) =>
    fetchJson<RouteItem>(`${BASE_URL}/routes/${id}`, {
      method: 'PUT',
      body: JSON.stringify({ ...route, actor }),
    }),
  getClientRoutes: (clientInternalId: string) =>
    fetchJson<ClientRoutesResult>(`${BASE_URL}/clients/${clientInternalId}/routes`),

  // Access Matrix
  getMatrix: () => fetchJson<MatrixData>(`${BASE_URL}/matrix`),
  toggleGrant: (clientId: string, permissionId: string, enable: boolean, actor = getCurrentActor()) =>
    fetchJson<{ success: boolean; diff: string; clientId: string; permissionId: string; enable: boolean }>(
      `${BASE_URL}/matrix/toggle`,
      {
        method: 'POST',
        body: JSON.stringify({ clientId, permissionId, enable, actor }),
      }
    ),

  // Access Checker
  checkAccess: (clientId: string, apiAudience: string, route: string, method: string) =>
    fetchJson<AccessCheckResult>(`${BASE_URL}/check-access`, {
      method: 'POST',
      body: JSON.stringify({ clientId, apiAudience, route, method }),
    }),

  // Audit Log
  getAuditLog: () => fetchJson<AuditLogItem[]>(`${BASE_URL}/audit`),
};
