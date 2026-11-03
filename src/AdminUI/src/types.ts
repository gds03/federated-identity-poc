export interface ClientItem {
  id: string;
  clientId: string;
  clientName: string;
  isActive: boolean;
  createdAtUtc: string;
  grantCount: number;
}

export interface ClientCreatedResult extends ClientItem {
  rawSecret: string;
}

export interface PermissionItem {
  id: string;
  scope: string;
  description: string;
  isActive: boolean;
}

export interface RouteItem {
  id: string;
  method: string;
  pathPattern: string;
  permissionId: string | null;
  scope: string | null;
  description: string;
  isActive: boolean;
}

export interface ApiItem {
  id: string;
  name: string;
  audience: string;
  description: string;
  isActive: boolean;
  permissions: PermissionItem[];
  routes: RouteItem[];
}

export interface EffectiveRoute {
  apiId: string;
  apiName: string;
  audience: string;
  routeId: string;
  method: string;
  pathPattern: string;
  scope: string | null;
  isPublic: boolean;
  allowed: boolean;
  reason: string;
}

export interface ClientRoutesResult {
  id: string;
  clientId: string;
  clientName: string;
  isActive: boolean;
  routes: EffectiveRoute[];
}

export interface MatrixData {
  clients: { id: string; clientId: string; clientName: string; isActive: boolean }[];
  permissions: {
    id: string;
    scope: string;
    description: string;
    isActive: boolean;
    apiId: string;
    apiName: string;
    apiAudience: string;
  }[];
  grants: { id: string; clientId: string; permissionId: string }[];
}

export interface AccessCheckResult {
  allowed: boolean;
  reason: string;
    requiredScope: string;
  clientScopes: string[];
  matchedRoute?: string | null;
}

export interface AuditLogItem {
  id: number;
  actor: string;
  action: string;
  entityType: string;
  entityId: string;
  detailsJson: string;
  timestampUtc: string;
}
