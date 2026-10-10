import React, { useState, useEffect } from 'react';
import { adminApi } from '../api';
import type { ApiItem } from '../types';
import { useAuth } from '../auth/AuthContext';
import { Server, Plus, RefreshCw, Key, Globe, Lock } from 'lucide-react';

export const ApisTab: React.FC = () => {
  const { isReadOnly } = useAuth();
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
  }, []);

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

  return (
    <div className="tab-pane">
      <div className="section-header">
        <div>
          <h2>APIs & Permissions</h2>
          <p className="subtitle">Register resource servers (audiences) and define granular OAuth scopes.</p>
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
