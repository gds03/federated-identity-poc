import React, { useState, useEffect } from 'react';
import { adminApi } from '../api';
import type { ApiItem, RouteItem } from '../types';
import { useAuth } from '../auth/AuthContext';
import { Server, Plus, RefreshCw, Key, Globe, Lock, Route as RouteIcon } from 'lucide-react';

const HTTP_METHODS = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS', '*'];

export const ApisTab: React.FC = () => {
  const { isReadOnly, token } = useAuth();
  const [apis, setApis] = useState<ApiItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // New API modal
  const [showApiModal, setShowApiModal] = useState(false);
  const [apiName, setApiName] = useState('');
  const [apiAudience, setApiAudience] = useState('');
  const [apiDesc, setApiDesc] = useState('');
  const [creatingApi, setCreatingApi] = useState(false);

  // New Scope modal
  const [selectedApiForScope, setSelectedApiForScope] = useState<ApiItem | null>(null);
  const [scopeName, setScopeName] = useState('');
  const [scopeDesc, setScopeDesc] = useState('');
  const [creatingScope, setCreatingScope] = useState(false);

  // New Route modal
  const [selectedApiForRoute, setSelectedApiForRoute] = useState<ApiItem | null>(null);
  const [routeMethod, setRouteMethod] = useState('GET');
  const [routePath, setRoutePath] = useState('');
  const [routePermissionId, setRoutePermissionId] = useState(''); // '' = public
  const [routeDesc, setRouteDesc] = useState('');
  const [creatingRoute, setCreatingRoute] = useState(false);

  const loadApis = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await adminApi.getApis();
      setApis(data);
    } catch (err: any) {
      setError(err.message || 'Failed to load APIs');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadApis();
  }, [token]);

  const handleCreateApi = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!apiName || !apiAudience) return;
    try {
      setCreatingApi(true);
      await adminApi.createApi(apiName, apiAudience, apiDesc);
      setShowApiModal(false);
      setApiName('');
      setApiAudience('');
      setApiDesc('');
      loadApis();
    } catch (err: any) {
      alert(`Error creating API: ${err.message}`);
    } finally {
      setCreatingApi(false);
    }
  };

  const handleCreateScope = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedApiForScope || !scopeName) return;
    try {
      setCreatingScope(true);
      await adminApi.createPermission(selectedApiForScope.id, scopeName, scopeDesc);
      setSelectedApiForScope(null);
      setScopeName('');
      setScopeDesc('');
      loadApis();
    } catch (err: any) {
      alert(`Error creating permission: ${err.message}`);
    } finally {
      setCreatingScope(false);
    }
  };

    const openRouteModal = (api: ApiItem) => {
    setSelectedApiForRoute(api);
    setRouteMethod('GET');
    setRoutePath('');
    setRoutePermissionId(api.permissions[0]?.id ?? '');
    setRouteDesc('');
  };

  const handleCreateRoute = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedApiForRoute || !routePath) return;
    try {
      setCreatingRoute(true);
      await adminApi.createRoute(
        selectedApiForRoute.id,
        routeMethod,
        routePath.trim(),
        routePermissionId || null,
        routeDesc
      );
      setSelectedApiForRoute(null);
      loadApis();
    } catch (err: any) {
      alert(`Error creating route: ${err.message}`);
    } finally {
      setCreatingRoute(false);
    }
  };

  const handleToggleRoute = async (route: RouteItem) => {
    const action = route.isActive ? 'disable' : 'enable';
    if (!confirm(`Are you sure you want to ${action} route '${route.method} ${route.pathPattern}'?`)) return;
    try {
      await adminApi.updateRoute(route.id, {
        method: route.method,
        pathPattern: route.pathPattern,
        permissionId: route.permissionId,
        description: route.description,
        isActive: !route.isActive,
      });
      loadApis();
    } catch (err: any) {
      alert(`Error updating route: ${err.message}`);
    }
  };

  return (
    <div className="tab-pane">
      <div className="section-header">
        <div>
          <h2>APIs, Scopes & Routes</h2>
          <p className="subtitle">
            Register resource servers (audiences), define OAuth scopes, and declare which route requires which scope.
            Routes that are not registered are denied by default.
          </p>
        </div>
        <div className="actions">
          <button className="btn btn-secondary" onClick={loadApis} disabled={loading}>
            <RefreshCw size={16} className={loading ? 'spin' : ''} /> Refresh
          </button>
          <button
            className="btn btn-primary"
            onClick={() => setShowApiModal(true)}
            disabled={isReadOnly}
            title={isReadOnly ? "Requires 'access-admin' role" : undefined}
          >
            {isReadOnly ? <Lock size={16} /> : <Plus size={16} />} Register API
          </button>
        </div>
      </div>

      {isReadOnly && (
        <div className="alert alert-warning mb-4 flex items-center gap-2">
          <Lock size={16} />
          <span><strong>Read-Only Mode:</strong> API and permission modifications require the <code>access-admin</code> role.</span>
        </div>
      )}

      {error && <div className="alert alert-danger">{error}</div>}

      <div className="api-grid">
        {apis.map((api) => (
          <div key={api.id} className="card api-card">
            <div className="api-card-header">
              <div className="api-title-row">
                <Server size={20} className="text-accent" />
                <h3>{api.name}</h3>
                <span className={`badge ${api.isActive ? 'badge-success' : 'badge-danger'}`}>
                  {api.isActive ? 'Active' : 'Disabled'}
                </span>
              </div>
              <div className="api-audience-tag">
                <Globe size={14} />
                <span className="mono text-xs">{api.audience}</span>
              </div>
              <p className="api-description">{api.description || 'No description provided.'}</p>
            </div>

            <div className="api-permissions-section">
              <div className="permissions-header">
                <span className="text-xs uppercase font-bold text-muted">Scopes ({api.permissions.length})</span>
                <button
                  className="btn btn-sm btn-outline"
                  onClick={() => setSelectedApiForScope(api)}
                  disabled={isReadOnly}
                  title={isReadOnly ? "Requires 'access-admin' role" : undefined}
                >
                  <Plus size={12} /> Add Scope
                </button>
              </div>

              <div className="permissions-list">
                {api.permissions.map((perm) => (
                  <div key={perm.id} className="permission-item">
                    <div className="permission-main">
                      <Key size={14} className="text-accent" />
                      <span className="mono font-bold text-sm">{perm.scope}</span>
                    </div>
                    <span className="text-xs text-muted">{perm.description || 'No description'}</span>
                  </div>
                ))}
                                {api.permissions.length === 0 && (
                  <div className="text-xs text-muted py-2 italic">No scopes registered for this API yet.</div>
                )}
              </div>
            </div>

            <div className="api-permissions-section">
              <div className="permissions-header">
                <span className="text-xs uppercase font-bold text-muted">Routes ({api.routes.length})</span>
                <button
                  className="btn btn-sm btn-outline"
                  onClick={() => openRouteModal(api)}
                  disabled={isReadOnly}
                  title={isReadOnly ? "Requires 'access-admin' role" : undefined}
                >
                  <Plus size={12} /> Add Route
                </button>
              </div>

              <div className="permissions-list">
                {api.routes.map((route) => (
                  <div key={route.id} className="permission-item" style={route.isActive ? undefined : { opacity: 0.5 }}>
                    <div className="permission-main">
                      <RouteIcon size={14} className="text-accent" />
                      <span className="mono font-bold text-sm">{route.method} {route.pathPattern}</span>
                    </div>
                    <span className="text-xs text-muted">
                      {route.scope ? <>requires <code className="mono">{route.scope}</code></> : 'public (no scope)'}
                      {route.description ? ` — ${route.description}` : ''}
                    </span>
                    <button
                      className="btn btn-sm btn-outline"
                      onClick={() => handleToggleRoute(route)}
                      disabled={isReadOnly}
                      title={isReadOnly ? "Requires 'access-admin' role" : undefined}
                    >
                      {route.isActive ? 'Disable' : 'Enable'}
                    </button>
                  </div>
                ))}
                {api.routes.length === 0 && (
                  <div className="text-xs text-muted py-2 italic">
                    No routes registered: every request to this API is denied by default.
                  </div>
                )}
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* Register API Modal */}
      {showApiModal && (
        <div className="modal-backdrop">
          <div className="modal-content">
            <h3>Register Resource Server (API)</h3>
            <p className="text-muted text-sm mb-4">
              Defines an audience that client applications can be granted tokens for.
            </p>
            <form onSubmit={handleCreateApi}>
              <div className="form-group">
                <label>API Service Name</label>
                <input
                  type="text"
                  placeholder="e.g. PaymentsApi"
                  value={apiName}
                  onChange={(e) => setApiName(e.target.value)}
                  required
                />
              </div>
              <div className="form-group">
                <label>Audience Identifier (URI)</label>
                <input
                  type="text"
                  placeholder="e.g. https://payments.internal"
                  value={apiAudience}
                  onChange={(e) => setApiAudience(e.target.value)}
                  required
                />
              </div>
              <div className="form-group">
                <label>Description</label>
                <input
                  type="text"
                  placeholder="e.g. Card processing and invoices"
                  value={apiDesc}
                  onChange={(e) => setApiDesc(e.target.value)}
                />
              </div>
              <div className="modal-actions">
                <button type="button" className="btn btn-secondary" onClick={() => setShowApiModal(false)}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary" disabled={creatingApi}>
                  {creatingApi ? 'Registering...' : 'Register API'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Add Route Modal */}
      {selectedApiForRoute && (
        <div className="modal-backdrop">
          <div className="modal-content">
            <h3>Add Route to {selectedApiForRoute.name}</h3>
            <p className="text-muted text-sm mb-4">
              Clients may call this route only if they hold the selected scope. Use <code className="mono">{'{id}'}</code> for
              one path segment, <code className="mono">*</code> for any one segment, and a trailing <code className="mono">/**</code> for
              everything below a path.
            </p>
            <form onSubmit={handleCreateRoute}>
              <div className="form-row">
                <div className="form-group flex-1">
                  <label>HTTP Method</label>
                  <select value={routeMethod} onChange={(e) => setRouteMethod(e.target.value)}>
                    {HTTP_METHODS.map((m) => (
                      <option key={m} value={m}>{m === '*' ? '* (any method)' : m}</option>
                    ))}
                  </select>
                </div>
                <div className="form-group flex-2">
                  <label>Path Pattern</label>
                  <input
                    type="text"
                    placeholder="e.g. /orders/{id}"
                    value={routePath}
                    onChange={(e) => setRoutePath(e.target.value)}
                    required
                  />
                </div>
              </div>
              <div className="form-group">
                <label>Required Scope</label>
                <select value={routePermissionId} onChange={(e) => setRoutePermissionId(e.target.value)}>
                  <option value="">None (public route)</option>
                  {selectedApiForRoute.permissions.map((p) => (
                    <option key={p.id} value={p.id}>{p.scope}{p.isActive ? '' : ' [disabled]'}</option>
                  ))}
                </select>
              </div>
              <div className="form-group">
                <label>Description</label>
                <input
                  type="text"
                  placeholder="e.g. Create an order"
                  value={routeDesc}
                  onChange={(e) => setRouteDesc(e.target.value)}
                />
              </div>
              <div className="modal-actions">
                <button type="button" className="btn btn-secondary" onClick={() => setSelectedApiForRoute(null)}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary" disabled={creatingRoute}>
                  {creatingRoute ? 'Adding...' : 'Add Route'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Add Scope Modal */}
      {selectedApiForScope && (
        <div className="modal-backdrop">
          <div className="modal-content">
            <h3>Add Scope to {selectedApiForScope.name}</h3>
            <p className="text-muted text-sm mb-4">
              Audience: <code className="mono">{selectedApiForScope.audience}</code>
            </p>
            <form onSubmit={handleCreateScope}>
              <div className="form-group">
                <label>Scope Identifier</label>
                <input
                  type="text"
                  placeholder="e.g. payments.charge, payments.refund"
                  value={scopeName}
                  onChange={(e) => setScopeName(e.target.value)}
                  required
                />
              </div>
              <div className="form-group">
                <label>Scope Description</label>
                <input
                  type="text"
                  placeholder="e.g. Process credit card transactions"
                  value={scopeDesc}
                  onChange={(e) => setScopeDesc(e.target.value)}
                />
              </div>
              <div className="modal-actions">
                <button type="button" className="btn btn-secondary" onClick={() => setSelectedApiForScope(null)}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary" disabled={creatingScope}>
                  {creatingScope ? 'Adding...' : 'Add Scope'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
