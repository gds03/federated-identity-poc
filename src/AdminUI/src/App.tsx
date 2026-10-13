import React, { useState } from 'react';
import { AuthProvider, useAuth } from './auth/AuthContext';
import { ClientsTab } from './components/ClientsTab';
import { ApisTab } from './components/ApisTab';
import { AccessMatrixTab } from './components/AccessMatrixTab';
import { AccessCheckerTab } from './components/AccessCheckerTab';
import { AuditLogTab } from './components/AuditLogTab';
import { IdpSettingsModal } from './components/IdpSettingsModal';
import {
  Shield,
  Users,
  Server,
  Grid,
  CheckSquare,
  History,
  UserCheck,
  ShieldAlert,
  ShieldCheck,
  User,
  LogIn,
  LogOut,
  RotateCcw,
  Settings,
  Globe,
  AlertTriangle,
} from 'lucide-react';

type Tab = 'clients' | 'apis' | 'matrix' | 'checker' | 'audit';

const AdminLayout: React.FC = () => {
  const [activeTab, setActiveTab] = useState<Tab>('clients');
  const [showIdpModal, setShowIdpModal] = useState(false);
  const {
    user,
    isAdmin,
    provider,
    auth0Domain,
    switchRole,
    login,
    logout,
    authError,
    isLoggingIn,
  } = useAuth();

  return (
    <div className="app-container">
      {/* Top Navbar */}
      <header className="navbar">
        <div className="navbar-brand">
          <div className="brand-logo">
            <Shield size={24} className="text-accent" />
          </div>
          <div>
            <h1 className="brand-title">Federated Identity</h1>
            <span className="brand-subtitle">Central Auth & Access Control</span>
          </div>
        </div>

        {/* Identity & Role Management Bar */}
        <div className="navbar-auth" style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          {/* IdP Indicator & Settings Trigger */}
          <button
            type="button"
            className="navbar-badge"
            style={{
              cursor: 'pointer',
              background: provider === 'auth0' ? 'rgba(16, 185, 129, 0.12)' : 'var(--bg-card)',
              borderColor: provider === 'auth0' ? 'var(--success)' : 'var(--border-color)',
              color: provider === 'auth0' ? 'var(--success)' : 'var(--text-main)',
            }}
            onClick={() => setShowIdpModal(true)}
            title="Configure Identity Provider (Mock vs Auth0)"
          >
            {provider === 'auth0' ? (
              <>
                <Globe size={14} className="text-success" />
                <span>
                  IdP: <strong>Auth0</strong> ({auth0Domain.split('.')[0] || 'Cloud'})
                </span>
              </>
            ) : (
              <>
                <span className="status-dot"></span>
                <span>
                  IdP: <strong>Local Mock</strong> (:5001)
                </span>
              </>
            )}
            <Settings size={14} style={{ marginLeft: '4px', opacity: 0.8 }} />
          </button>

          {user ? (
            <div className="user-profile-badge">
              <div className="user-avatar">
                <User size={16} />
              </div>
              <div className="user-info">
                <span className="user-name">{user.name}</span>
                <span className="user-email text-xs text-muted">{user.email || user.sub}</span>
              </div>
              <div className="role-pill">
                {isAdmin ? (
                  <span className="badge badge-success" title="Possesses 'access-admin' role">
                    <ShieldCheck size={12} /> access-admin
                  </span>
                ) : (
                  <span className="badge badge-warning" title="Viewer only. Mutations disabled.">
                    <ShieldAlert size={12} /> read-only (viewer)
                  </span>
                )}
              </div>

              {/* Fast User Switcher for Local Mock */}
              {provider === 'mock' && (
                <div className="btn-group ml-3">
                  {isAdmin ? (
                    <button
                      className="btn btn-sm btn-outline text-xs"
                      onClick={() => switchRole('bob')}
                      title="Simulate non-admin user signing in through OIDC"
                    >
                      <RotateCcw size={12} /> Switch to Bob (Read-Only)
                    </button>
                  ) : (
                    <button
                      className="btn btn-sm btn-primary text-xs"
                      onClick={() => switchRole('alice')}
                      title="Simulate admin user signing in through OIDC"
                    >
                      <UserCheck size={12} /> Switch to Alice (Admin)
                    </button>
                  )}
                </div>
              )}

              {/* Sign Out Button */}
              <button
                className="btn btn-sm btn-secondary text-xs ml-2"
                onClick={() => logout()}
                title="Sign out of current identity session"
                style={{ display: 'flex', alignItems: 'center', gap: '4px' }}
              >
                <LogOut size={12} />
                <span>Sign Out</span>
              </button>
            </div>
          ) : (
            <button
              className="btn btn-primary btn-sm"
              onClick={() => login()}
              disabled={isLoggingIn}
              style={{ display: 'flex', alignItems: 'center', gap: '6px' }}
            >
              <LogIn size={14} />
              <span>
                {isLoggingIn
                  ? 'Connecting...'
                  : provider === 'auth0'
                  ? 'Sign In with Auth0'
                  : 'Sign In with Mock OIDC'}
              </span>
            </button>
          )}

          <div className="navbar-badge">
            <span className="status-dot"></span>
            <span>
              AuthService: <strong>:5001</strong>
            </span>
          </div>
        </div>
      </header>

      {/* Optional Auth Error Alert */}
      {authError && (
        <div
          style={{
            background: 'var(--danger-bg)',
            borderBottom: '1px solid var(--danger)',
            color: 'var(--text-main)',
            padding: '10px 24px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            fontSize: '0.85rem',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <AlertTriangle size={16} className="text-danger" />
            <span>{authError}</span>
          </div>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={() => setShowIdpModal(true)}
            style={{ fontSize: '11px', padding: '2px 8px' }}
          >
            Check IdP Settings
          </button>
        </div>
      )}

      {/* Main Layout */}
      <div className="layout-body">
        {/* Sidebar Nav */}
        <aside className="sidebar">
          <nav className="nav-menu">
            <button
              className={`nav-item ${activeTab === 'clients' ? 'active' : ''}`}
              onClick={() => setActiveTab('clients')}
            >
              <Users size={18} />
              <span>Clients</span>
            </button>
            <button
              className={`nav-item ${activeTab === 'apis' ? 'active' : ''}`}
              onClick={() => setActiveTab('apis')}
            >
              <Server size={18} />
              <span>APIs & Scopes</span>
            </button>
            <button
              className={`nav-item ${activeTab === 'matrix' ? 'active' : ''}`}
              onClick={() => setActiveTab('matrix')}
            >
              <Grid size={18} />
              <span>Access Matrix</span>
            </button>
            <button
              className={`nav-item ${activeTab === 'checker' ? 'active' : ''}`}
              onClick={() => setActiveTab('checker')}
            >
              <CheckSquare size={18} />
              <span>Access Checker</span>
            </button>
            <button
              className={`nav-item ${activeTab === 'audit' ? 'active' : ''}`}
              onClick={() => setActiveTab('audit')}
            >
              <History size={18} />
              <span>Audit Log</span>
            </button>
          </nav>

          <div className="sidebar-footer">
            <div className="text-xs text-muted" style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
              <div>
                IdP: <strong>{provider === 'auth0' ? 'Auth0 Tenant' : 'Mock OIDC'}</strong>
              </div>
              <div>
                OIDC: <strong>Auth Code + PKCE</strong>
              </div>
              <div>
                Mode: <strong>{isAdmin ? 'Admin (Read/Write)' : 'Read-Only (Viewer)'}</strong>
              </div>
            </div>
          </div>
        </aside>

        {/* Content Pane */}
        <main className="content-area">
          {activeTab === 'clients' && <ClientsTab />}
          {activeTab === 'apis' && <ApisTab />}
          {activeTab === 'matrix' && <AccessMatrixTab />}
          {activeTab === 'checker' && <AccessCheckerTab />}
          {activeTab === 'audit' && <AuditLogTab />}
        </main>
      </div>

      {/* IdP Settings Modal */}
      <IdpSettingsModal isOpen={showIdpModal} onClose={() => setShowIdpModal(false)} />
    </div>
  );
};

export const App: React.FC = () => {
  return (
    <AuthProvider>
      <AdminLayout />
    </AuthProvider>
  );
};

export default App;
