import React, { useState, useEffect } from 'react';
import { adminApi } from '../api';
import type { AuditLogItem } from '../types';
import { useAuth } from '../auth/AuthContext';
import { RefreshCw, FileText, User } from 'lucide-react';

export const AuditLogTab: React.FC = () => {
  const { token } = useAuth();
  const [logs, setLogs] = useState<AuditLogItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedDetails, setSelectedDetails] = useState<string | null>(null);

  const loadLogs = async () => {
    try {
      setLoading(true);
      setError(null);
      const res = await adminApi.getAuditLog();
      setLogs(res);
    } catch (err: any) {
      setError(err.message || 'Failed to load audit logs');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadLogs();
  }, [token]);

  const getActionBadgeClass = (action: string) => {
    if (action.includes('CREATE') || action.includes('GRANT')) return 'badge-success';
    if (action.includes('DISABLE') || action.includes('REVOKE')) return 'badge-danger';
    if (action.includes('ROTATE')) return 'badge-warning';
    return 'badge-neutral';
  };

  return (
    <div className="tab-pane">
      <div className="section-header">
        <div>
          <h2>Security Audit Log</h2>
          <p className="subtitle">Immutable record of who changed what, when, and exact parameters altered.</p>
        </div>
        <div className="actions">
          <button className="btn btn-secondary" onClick={loadLogs} disabled={loading}>
            <RefreshCw size={16} className={loading ? 'spin' : ''} /> Refresh
          </button>
        </div>
      </div>

      {error && <div className="alert alert-danger">{error}</div>}

      <div className="card">
        <table className="data-table">
          <thead>
            <tr>
              <th>Timestamp (UTC)</th>
              <th>Actor</th>
              <th>Action</th>
              <th>Entity Type</th>
              <th>Entity ID</th>
              <th className="text-right">Details</th>
            </tr>
          </thead>
          <tbody>
            {logs.map((log) => (
              <tr key={log.id}>
                <td className="text-muted text-sm mono">
                  {new Date(log.timestampUtc).toISOString().replace('T', ' ').substring(0, 19)}
                </td>
                <td>
                  <div className="flex items-center gap-1.5">
                    <User size={14} className="text-muted" />
                    <span className="font-medium">{log.actor}</span>
                  </div>
                </td>
                <td>
                  <span className={`badge ${getActionBadgeClass(log.action)}`}>
                    {log.action}
                  </span>
                </td>
                <td>
                  <span className="badge badge-neutral">{log.entityType}</span>
                </td>
                <td className="mono text-xs font-bold text-accent">{log.entityId}</td>
                <td className="text-right">
                  <button
                    className="btn btn-sm btn-outline"
                    onClick={() => setSelectedDetails(log.detailsJson)}
                    title="View payload"
                  >
                    <FileText size={14} /> View
                  </button>
                </td>
              </tr>
            ))}
            {logs.length === 0 && !loading && (
              <tr>
                <td colSpan={6} className="text-center text-muted py-4">No audit entries found.</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* JSON Viewer Modal */}
      {selectedDetails && (
        <div className="modal-backdrop">
          <div className="modal-content">
            <h3>Audit Entry Details</h3>
            <pre className="json-viewer mono text-xs">
              {(() => {
                try {
                  return JSON.stringify(JSON.parse(selectedDetails), null, 2);
                } catch {
                  return selectedDetails;
                }
              })()}
            </pre>
            <div className="modal-actions">
              <button className="btn btn-primary" onClick={() => setSelectedDetails(null)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
