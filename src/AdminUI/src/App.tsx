import React, { useState } from 'react';
import { AuthProvider, useAuth } from './auth/AuthContext';
import { ClientsTab } from './components/ClientsTab';
import { ApisTab } from './components/ApisTab';
import { AccessMatrixTab } from './components/AccessMatrixTab';
import { AccessCheckerTab } from './components/AccessCheckerTab';
import { AuditLogTab } from './components/AuditLogTab';
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
  RotateCcw,
} from 'lucide-react';

type Tab = 'clients' | 'apis' | 'matrix' | 'checker' | 'audit';

const AdminLayout: React.FC = () => {
  const [activeTab, setActiveTab] = useState<Tab>('clients');
  const { user, isAdmin, switchRole, login } = useAuth();

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
        <div className="navbar-auth">
          {user ? (
            <div className="user-profile-badge">
              <div className="user-avatar">
                <User size={16} />
              </div>
              <div className="user-info">
                <span className="user-name">{user.name}</span>
                <span className="user-email text-xs text-muted">{user.email}</span>
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

              {/* Fast User Switcher for Testing */}
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
            </div>
          ) : (
            <button className="btn btn-primary btn-sm" onClick={() => login()}>
              <LogIn size={14} /> Sign In with OneLogin / OIDC
            </button>
          )}

          <div className="navbar-badge">
            <span className="status-dot"></span>
            <span>AuthService: <strong>:5001</strong></span>
          </div>
        </div>
      </header>

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
            <div className="text-xs text-muted">
              <div>OIDC: <strong>Authorization Code + PKCE</strong></div>
              <div>Mode: <strong>{isAdmin ? 'Admin (Read/Write)' : 'Read-Only'}</strong></div>
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
