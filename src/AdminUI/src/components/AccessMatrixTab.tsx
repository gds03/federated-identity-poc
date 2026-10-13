import React, { useState, useEffect } from 'react';
import { adminApi } from '../api';
import type { MatrixData } from '../types';
import { useAuth } from '../auth/AuthContext';
import { RefreshCw, CheckCircle2, XCircle, ArrowRight, AlertTriangle, Lock } from 'lucide-react';

interface PendingToggle {
  client: MatrixData['clients'][0];
  permission: MatrixData['permissions'][0];
  currentlyGranted: boolean;
}

export const AccessMatrixTab: React.FC = () => {
  const { isReadOnly, token } = useAuth();
  const [data, setData] = useState<MatrixData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Pending toggle modal state
  const [pendingToggle, setPendingToggle] = useState<PendingToggle | null>(null);
  const [saving, setSaving] = useState(false);
  const [lastDiffResult, setLastDiffResult] = useState<string | null>(null);

  const loadMatrix = async () => {
    try {
      setLoading(true);
      setError(null);
      const res = await adminApi.getMatrix();
      setData(res);
    } catch (err: any) {
      setError(err.message || 'Failed to load access matrix');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadMatrix();
  }, [token]);

  const isGranted = (clientId: string, permissionId: string): boolean => {
    if (!data) return false;
    return data.grants.some((g) => g.clientId === clientId && g.permissionId === permissionId);
  };

  const initiateToggle = (client: MatrixData['clients'][0], permission: MatrixData['permissions'][0]) => {
    const current = isGranted(client.id, permission.id);
    setPendingToggle({
      client,
      permission,
      currentlyGranted: current,
    });
  };

  const confirmToggle = async () => {
    if (!pendingToggle) return;
    try {
      setSaving(true);
      const res = await adminApi.toggleGrant(
        pendingToggle.client.id,
        pendingToggle.permission.id,
        !pendingToggle.currentlyGranted
      );
      setLastDiffResult(res.diff);
      setPendingToggle(null);
      await loadMatrix();
      setTimeout(() => setLastDiffResult(null), 4000);
    } catch (err: any) {
      alert(`Failed to update grant: ${err.message}`);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="tab-pane">
      <div className="section-header">
        <div>
          <h2>Access Matrix</h2>
          <p className="subtitle">
            Configure client grants. Clients as rows, scopes as columns. Toggling requires diff confirmation.
          </p>
        </div>
        <div className="actions">
          <button className="btn btn-secondary" onClick={loadMatrix} disabled={loading}>
            <RefreshCw size={16} className={loading ? 'spin' : ''} /> Refresh
          </button>
        </div>
      </div>

      {isReadOnly && (
        <div className="alert alert-warning mb-4 flex items-center gap-2">
          <Lock size={16} />
          <span><strong>Read-Only Mode:</strong> Grant modifications in the matrix require the <code>access-admin</code> role.</span>
        </div>
      )}

      {error && <div className="alert alert-danger">{error}</div>}

      {lastDiffResult && (
        <div className="alert alert-success flex items-center gap-2">
          <CheckCircle2 size={16} />
          <span>Audit Logged: <strong>{lastDiffResult}</strong></span>
        </div>
      )}

      {data && (
        <div className="card matrix-container">
          <table className="matrix-table">
            <thead>
              <tr>
                <th className="client-col-header">Client Application</th>
                {data.permissions.map((p) => (
                  <th key={p.id} className="permission-col-header">
                    <div className="col-header-api">{p.apiName}</div>
                    <div className="col-header-scope mono">{p.scope}</div>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {data.clients.map((c) => (
                <tr key={c.id} className={!c.isActive ? 'opacity-50' : ''}>
                  <td className="client-cell">
                    <div className="client-name font-bold">{c.clientName}</div>
                    <div className="client-id mono text-xs text-muted">{c.clientId}</div>
                    {!c.isActive && <span className="badge badge-danger text-xs mt-1">Disabled</span>}
                  </td>
                  {data.permissions.map((p) => {
                    const granted = isGranted(c.id, p.id);
                    return (
                      <td key={p.id} className="matrix-cell">
                        <button
                          type="button"
                          className={`matrix-toggle ${granted ? 'granted' : 'revoked'}`}
                          onClick={() => !isReadOnly && initiateToggle(c, p)}
                          disabled={isReadOnly || !c.isActive}
                          title={
                            isReadOnly
                              ? "Read-only mode (requires access-admin)"
                              : `${c.clientId} ➔ ${p.scope} (${granted ? 'Click to revoke' : 'Click to grant'})`
                          }
                        >
                          {granted ? (
                            <CheckCircle2 size={18} className="text-success" />
                          ) : (
                            <XCircle size={18} className="text-muted" />
                          )}
                        </button>
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Confirmation with Diff Modal */}
      {pendingToggle && (
        <div className="modal-backdrop">
          <div className="modal-content">
            <div className="modal-header-icon">
              <AlertTriangle size={28} className="text-accent" />
            </div>
            <h3>Confirm Access Grant Modification</h3>
            <p className="text-muted text-sm mb-4">
              Review the authorization difference before applying to the identity database:
            </p>

            <div className="diff-card">
              <div className="diff-row">
                <span className="diff-label">Client App:</span>
                <span className="diff-val">
                  <strong>{pendingToggle.client.clientName}</strong> (<code className="mono">{pendingToggle.client.clientId}</code>)
                </span>
              </div>
              <div className="diff-row">
                <span className="diff-label">Target Service:</span>
                <span className="diff-val">
                  {pendingToggle.permission.apiName} (<code className="mono">{pendingToggle.permission.apiAudience}</code>)
                </span>
              </div>
              <div className="diff-row">
                <span className="diff-label">Scope:</span>
                <span className="diff-val mono font-bold text-accent">{pendingToggle.permission.scope}</span>
              </div>
              <div className="diff-row border-t pt-3 mt-3">
                <span className="diff-label">Grant Change:</span>
                <div className="diff-states">
                  <span className={`diff-state-badge ${pendingToggle.currentlyGranted ? 'badge-success' : 'badge-neutral'}`}>
                    {pendingToggle.currentlyGranted ? 'GRANTED' : 'DENIED'}
                  </span>
                  <ArrowRight size={16} className="text-muted" />
                  <span className={`diff-state-badge ${!pendingToggle.currentlyGranted ? 'badge-success' : 'badge-danger'}`}>
                    {!pendingToggle.currentlyGranted ? 'GRANTED' : 'REVOKED'}
                  </span>
                </div>
              </div>
            </div>

            <div className="modal-actions">
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setPendingToggle(null)}
                disabled={saving}
              >
                Cancel
              </button>
              <button
                type="button"
                className={`btn ${pendingToggle.currentlyGranted ? 'btn-danger' : 'btn-primary'}`}
                onClick={confirmToggle}
                disabled={saving}
              >
                {saving
                  ? 'Saving Change...'
                  : pendingToggle.currentlyGranted
                  ? 'Confirm Revocation'
                  : 'Confirm Grant'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
