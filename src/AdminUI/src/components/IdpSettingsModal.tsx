import React, { useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { type IdpProvider } from '../auth/oidcConfig';
import { Settings, Shield, Globe, Check, Copy, AlertCircle, Sparkles } from 'lucide-react';

interface IdpSettingsModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export const IdpSettingsModal: React.FC<IdpSettingsModalProps> = ({ isOpen, onClose }) => {
  const {
    provider,
    auth0Domain,
    auth0ClientId,
    switchProvider,
    updateAuth0Config,
    isAdmin,
    toggleAdminBypass,
  } = useAuth();

  const [selectedProvider, setSelectedProvider] = useState<IdpProvider>(provider);
  const [domain, setDomain] = useState(auth0Domain);
  const [clientId, setClientId] = useState(auth0ClientId);
  const [copiedField, setCopiedField] = useState<string | null>(null);
  const [saveSuccess, setSaveSuccess] = useState(false);

  if (!isOpen) return null;

  const currentOrigin = window.location.origin;

  const copyToClipboard = (text: string, field: string) => {
    navigator.clipboard.writeText(text);
    setCopiedField(field);
    setTimeout(() => setCopiedField(null), 2000);
  };

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    updateAuth0Config(domain, clientId);
    switchProvider(selectedProvider, domain, clientId);
    setSaveSuccess(true);
    setTimeout(() => {
      setSaveSuccess(false);
      onClose();
    }, 800);
  };

  return (
    <div className="modal-backdrop">
      <div className="modal-content" style={{ maxWidth: '640px', width: '92%' }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '16px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <div style={{ background: 'rgba(59, 130, 246, 0.2)', padding: '8px', borderRadius: '8px' }}>
              <Settings size={22} className="text-accent" />
            </div>
            <div>
              <h3 style={{ margin: 0, fontSize: '1.2rem', fontWeight: 600 }}>Identity Provider (IdP) Settings</h3>
              <p style={{ margin: 0, fontSize: '0.8rem', color: 'var(--text-muted)' }}>
                Switch between built-in local Mock OIDC and your real Auth0 tenant
              </p>
            </div>
          </div>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={onClose}
            style={{ padding: '4px 10px' }}
          >
            ✕
          </button>
        </div>

        <form onSubmit={handleSave}>
          {/* Provider Selector Cards */}
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', marginBottom: '20px' }}>
            <div
              onClick={() => setSelectedProvider('mock')}
              style={{
                border: `2px solid ${selectedProvider === 'mock' ? 'var(--primary)' : 'var(--border-color)'}`,
                background: selectedProvider === 'mock' ? 'rgba(59, 130, 246, 0.08)' : 'var(--bg-card)',
                borderRadius: '10px',
                padding: '14px',
                cursor: 'pointer',
                transition: 'all 0.2s',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '6px' }}>
                <Shield size={18} className="text-accent" />
                <strong style={{ fontSize: '0.95rem' }}>Local Mock OIDC</strong>
              </div>
              <p style={{ fontSize: '0.78rem', color: 'var(--text-muted)', margin: 0 }}>
                Runs internally on <code className="mono">http://localhost:5001/mock-oidc</code>. Instant local test users (Alice / Bob).
              </p>
            </div>

            <div
              onClick={() => setSelectedProvider('auth0')}
              style={{
                border: `2px solid ${selectedProvider === 'auth0' ? 'var(--primary)' : 'var(--border-color)'}`,
                background: selectedProvider === 'auth0' ? 'rgba(59, 130, 246, 0.08)' : 'var(--bg-card)',
                borderRadius: '10px',
                padding: '14px',
                cursor: 'pointer',
                transition: 'all 0.2s',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '6px' }}>
                <Globe size={18} className="text-success" />
                <strong style={{ fontSize: '0.95rem' }}>Real Auth0 Tenant</strong>
              </div>
              <p style={{ fontSize: '0.78rem', color: 'var(--text-muted)', margin: 0 }}>
                Cloud OpenID Connect with PKCE (<code className="mono">auth0.com</code>). Enterprise MFA, directory & real login.
              </p>
            </div>
          </div>

          {/* Auth0 Configuration Details */}
          {selectedProvider === 'auth0' && (
            <div
              style={{
                background: 'rgba(15, 23, 42, 0.6)',
                border: '1px solid var(--border-color)',
                borderRadius: '10px',
                padding: '16px',
                marginBottom: '20px',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: '6px', marginBottom: '12px' }}>
                <Sparkles size={16} className="text-accent" />
                <span style={{ fontSize: '0.85rem', fontWeight: 600 }}>Auth0 Tenant Credentials</span>
              </div>

              <div className="form-group" style={{ marginBottom: '12px' }}>
                <label style={{ fontSize: '0.8rem', fontWeight: 500, display: 'block', marginBottom: '4px' }}>
                  Auth0 Domain:
                </label>
                <input
                  type="text"
                  placeholder="e.g. dev-taacleywsdh33h5b.us.auth0.com"
                  value={domain}
                  onChange={(e) => setDomain(e.target.value)}
                  required
                  style={{ width: '100%' }}
                />
              </div>

              <div className="form-group" style={{ marginBottom: '16px' }}>
                <label style={{ fontSize: '0.8rem', fontWeight: 500, display: 'block', marginBottom: '4px' }}>
                  Auth0 Client ID (from Single Page App):
                </label>
                <input
                  type="text"
                  placeholder="e.g. xK90abc123DEF456..."
                  value={clientId}
                  onChange={(e) => setClientId(e.target.value)}
                  required
                  style={{ width: '100%' }}
                />
              </div>

              {/* Required Auth0 Dashboard Settings Guide */}
              <div
                style={{
                  background: 'var(--bg-card)',
                  borderRadius: '8px',
                  padding: '12px',
                  fontSize: '0.8rem',
                  border: '1px solid var(--border-color)',
                }}
              >
                <div style={{ fontWeight: 600, marginBottom: '6px', color: 'var(--text-main)' }}>
                  Required settings in Auth0 Dashboard Application:
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <span style={{ color: 'var(--text-muted)' }}>Allowed Callback URLs:</span>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                      <code className="mono text-xs">{currentOrigin}</code>
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        style={{ padding: '2px 6px', fontSize: '10px' }}
                        onClick={() => copyToClipboard(currentOrigin, 'callback')}
                      >
                        {copiedField === 'callback' ? <Check size={12} className="text-success" /> : <Copy size={12} />}
                      </button>
                    </div>
                  </div>

                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <span style={{ color: 'var(--text-muted)' }}>Allowed Logout URLs:</span>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                      <code className="mono text-xs">{currentOrigin}</code>
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        style={{ padding: '2px 6px', fontSize: '10px' }}
                        onClick={() => copyToClipboard(currentOrigin, 'logout')}
                      >
                        {copiedField === 'logout' ? <Check size={12} className="text-success" /> : <Copy size={12} />}
                      </button>
                    </div>
                  </div>

                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <span style={{ color: 'var(--text-muted)' }}>Allowed Web Origins:</span>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                      <code className="mono text-xs">{currentOrigin}</code>
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        style={{ padding: '2px 6px', fontSize: '10px' }}
                        onClick={() => copyToClipboard(currentOrigin, 'origin')}
                      >
                        {copiedField === 'origin' ? <Check size={12} className="text-success" /> : <Copy size={12} />}
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* Admin Role Helper */}
          <div
            style={{
              padding: '12px 14px',
              borderRadius: '8px',
              background: 'rgba(245, 158, 11, 0.08)',
              border: '1px solid rgba(245, 158, 11, 0.25)',
              marginBottom: '20px',
              display: 'flex',
              alignItems: 'flex-start',
              gap: '10px',
              fontSize: '0.8rem',
            }}
          >
            <AlertCircle size={18} className="text-warning" style={{ flexShrink: 0, marginTop: '2px' }} />
            <div>
              <div style={{ fontWeight: 600, color: '#fbbf24', marginBottom: '2px' }}>
                Role & Write Access Notice:
              </div>
              <div style={{ color: 'var(--text-muted)', lineHeight: '1.4' }}>
                To create/disable clients in the Admin UI, the user needs the <code className="mono">access-admin</code> role.
                In Auth0, you can configure an Action to include roles in the token, or toggle the local admin test override:
              </div>
              <div style={{ marginTop: '8px' }}>
                <button
                  type="button"
                  className={`btn btn-sm ${isAdmin ? 'btn-outline' : 'btn-primary'}`}
                  style={{ fontSize: '11px', padding: '3px 8px' }}
                  onClick={toggleAdminBypass}
                >
                  {isAdmin ? 'Disable Local Admin Override' : 'Enable Local Admin Test Override'}
                </button>
              </div>
            </div>
          </div>

          <div className="modal-actions" style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
            <button type="button" className="btn btn-secondary" onClick={onClose}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" style={{ minWidth: '140px' }}>
              {saveSuccess ? (
                <>
                  <Check size={14} /> Saved!
                </>
              ) : (
                'Save & Apply'
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
