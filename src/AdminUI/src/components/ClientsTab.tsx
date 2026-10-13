import React, { useState, useEffect } from 'react';
import { adminApi } from '../api';
import type { ClientItem } from '../types';
import { useAuth } from '../auth/AuthContext';
import { KeyRound, Plus, ShieldCheck, ShieldAlert, RefreshCw, Copy, Check, Eye, Lock } from 'lucide-react';

export const ClientsTab: React.FC = () => {
  const { isReadOnly, token } = useAuth();
  const [clients, setClients] = useState<ClientItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Create modal state
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [newClientId, setNewClientId] = useState('');
  const [newClientName, setNewClientName] = useState('');
  const [creating, setCreating] = useState(false);

  // Secret revelation modal
  const [revealedSecret, setRevealedSecret] = useState<{ clientId: string; secret: string; isNew: boolean } | null>(null);
  const [copied, setCopied] = useState(false);

  const loadClients = async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await adminApi.getClients();
      setClients(data);
    } catch (err: any) {
      setError(err.message || 'Failed to load clients');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadClients();
  }, [token]);

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newClientId || !newClientName) return;
    try {
      setCreating(true);
      const result = await adminApi.createClient(newClientId, newClientName);
      setShowCreateModal(false);
      setNewClientId('');
      setNewClientName('');
      setRevealedSecret({
        clientId: result.clientId,
        secret: result.rawSecret,
        isNew: true,
      });
      loadClients();
    } catch (err: any) {
      alert(`Error creating client: ${err.message}`);
    } finally {
      setCreating(false);
    }
  };

  const handleToggleStatus = async (client: ClientItem) => {
    const action = client.isActive ? 'disable' : 'enable';
    if (!confirm(`Are you sure you want to ${action} client '${client.clientId}'?`)) return;
    try {
      await adminApi.toggleClientStatus(client.id, !client.isActive);
      loadClients();
    } catch (err: any) {
      alert(`Error toggling status: ${err.message}`);
    }
  };

  const handleRotateSecret = async (client: ClientItem) => {
    if (!confirm(`Rotate secret for '${client.clientId}'? Existing tokens will still be valid until expiry, but the old secret will immediately stop working.`)) return;
    try {
      const result = await adminApi.rotateSecret(client.id);
      setRevealedSecret({
        clientId: result.clientId,
        secret: result.rawSecret,
        isNew: false,
      });
    } catch (err: any) {
      alert(`Error rotating secret: ${err.message}`);
    }
  };

  const copyToClipboard = (text: string) => {
    navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="tab-pane">
      <div className="section-header">
        <div>
          <h2>Client Applications</h2>
          <p className="subtitle">Manage OAuth 2.0 client credentials, rotate hashed secrets, and control access state.</p>
        </div>
        <div className="actions">
          <button className="btn btn-secondary" onClick={loadClients} disabled={loading}>
            <RefreshCw size={16} className={loading ? 'spin' : ''} /> Refresh
          </button>
          <button
            className="btn btn-primary"
            onClick={() => setShowCreateModal(true)}
            disabled={isReadOnly}
            title={isReadOnly ? "Requires 'access-admin' role" : undefined}
          >
            {isReadOnly ? <Lock size={16} /> : <Plus size={16} />} Register Client
          </button>
        </div>
      </div>

      {isReadOnly && (
        <div className="alert alert-warning mb-4 flex items-center gap-2">
          <Lock size={16} />
          <span><strong>Read-Only Mode:</strong> Your signed-in role (<code>viewer</code>) permits viewing only. Modifications require the <code>access-admin</code> role.</span>
        </div>
      )}

      {error && <div className="alert alert-danger">{error}</div>}

      <div className="card">
        <table className="data-table">
          <thead>
            <tr>
              <th>Client ID</th>
              <th>Display Name</th>
              <th>Status</th>
              <th>Grants</th>
              <th>Registered</th>
              <th className="text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            {clients.map((c) => (
              <tr key={c.id}>
                <td>
                  <span className="mono font-bold text-accent">{c.clientId}</span>
                </td>
                <td>{c.clientName}</td>
                <td>
                  {c.isActive ? (
                    <span className="badge badge-success"><ShieldCheck size={12} /> Active</span>
                  ) : (
                    <span className="badge badge-danger"><ShieldAlert size={12} /> Disabled</span>
                  )}
                </td>
                <td>
                  <span className="badge badge-neutral">{c.grantCount} scopes</span>
                </td>
                <td className="text-muted text-sm">{new Date(c.createdAtUtc).toLocaleDateString()}</td>
                <td className="text-right">
                  <div className="btn-group">
                    <button
                      className="btn btn-sm btn-outline"
                      title={isReadOnly ? "Requires 'access-admin' role" : "Rotate Secret"}
                      onClick={() => handleRotateSecret(c)}
                      disabled={!c.isActive || isReadOnly}
                    >
                      <KeyRound size={14} /> Rotate Secret
                    </button>
                    <button
                      className={`btn btn-sm ${c.isActive ? 'btn-danger' : 'btn-success'}`}
                      onClick={() => handleToggleStatus(c)}
                      disabled={isReadOnly}
                      title={isReadOnly ? "Requires 'access-admin' role" : undefined}
                    >
                      {c.isActive ? 'Disable' : 'Enable'}
                    </button>
                  </div>
                </td>
              </tr>
            ))}
            {clients.length === 0 && !loading && (
              <tr>
                <td colSpan={6} className="text-center text-muted py-4">No clients registered yet.</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Register Client Modal */}
      {showCreateModal && (
        <div className="modal-backdrop">
          <div className="modal-content">
            <h3>Register New Client Application</h3>
            <p className="text-muted text-sm mb-4">
              A high-entropy secret will be automatically generated and hashed before saving.
            </p>
            <form onSubmit={handleCreate}>
              <div className="form-group">
                <label>Client ID (Machine identifier)</label>
                <input
                  type="text"
                  placeholder="e.g. billing-service, mobile-proxy"
                  value={newClientId}
                  onChange={(e) => setNewClientId(e.target.value)}
                  required
                />
              </div>
              <div className="form-group">
                <label>Display / App Name</label>
                <input
                  type="text"
                  placeholder="e.g. Billing Sync Worker"
                  value={newClientName}
                  onChange={(e) => setNewClientName(e.target.value)}
                  required
                />
              </div>
              <div className="modal-actions">
                <button type="button" className="btn btn-secondary" onClick={() => setShowCreateModal(false)}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary" disabled={creating}>
                  {creating ? 'Registering...' : 'Generate & Register'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Secret Revelation Modal */}
      {revealedSecret && (
        <div className="modal-backdrop">
          <div className="modal-content modal-warning">
            <div className="modal-header-icon">
              <Eye size={28} className="text-warning" />
            </div>
            <h3>{revealedSecret.isNew ? 'New Client Secret Generated' : 'Client Secret Rotated'}</h3>
            <p className="text-warning-lead">
              <strong>IMPORTANT:</strong> This is the <u>only time</u> this secret will ever be displayed in plain text.
              It is stored only as a cryptographic hash in the database.
            </p>
            <div className="secret-display-box">
              <span className="secret-label">Client ID:</span>
              <span className="mono font-bold mb-2 block">{revealedSecret.clientId}</span>
              <span className="secret-label">Client Secret:</span>
              <div className="secret-field">
                <input type="text" readOnly value={revealedSecret.secret} className="mono" />
                <button className="btn btn-secondary btn-sm" onClick={() => copyToClipboard(revealedSecret.secret)}>
                  {copied ? <Check size={14} className="text-success" /> : <Copy size={14} />}
                  {copied ? 'Copied!' : 'Copy'}
                </button>
              </div>
            </div>
            <div className="modal-actions">
              <button className="btn btn-primary" onClick={() => setRevealedSecret(null)}>
                I have saved this secret securely
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
