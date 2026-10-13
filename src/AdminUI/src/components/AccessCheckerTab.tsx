import React, { useState, useEffect } from 'react';
import { adminApi } from '../api';
import type { ClientItem, ApiItem, AccessCheckResult } from '../types';
import { useAuth } from '../auth/AuthContext';
import { Search, ShieldCheck, ShieldAlert, Cpu } from 'lucide-react';

export const AccessCheckerTab: React.FC = () => {
  const { token } = useAuth();
  const [clients, setClients] = useState<ClientItem[]>([]);
  const [apis, setApis] = useState<ApiItem[]>([]);
  const [loading, setLoading] = useState(true);

  // Form selections
  const [selectedClientId, setSelectedClientId] = useState('');
  const [selectedAudience, setSelectedAudience] = useState('');
  const [route, setRoute] = useState('/orders');
  const [method, setMethod] = useState('GET');

  // Evaluation outcome
  const [evaluating, setEvaluating] = useState(false);
  const [result, setResult] = useState<AccessCheckResult | null>(null);

  useEffect(() => {
    const init = async () => {
      try {
        setLoading(true);
        const [cList, aList] = await Promise.all([adminApi.getClients(), adminApi.getApis()]);
        setClients(cList);
        setApis(aList);
        if (cList.length > 0) setSelectedClientId(cList[0].clientId);
        if (aList.length > 0) setSelectedAudience(aList[0].audience);
      } catch (err: any) {
        console.error('Failed to init AccessChecker:', err);
      } finally {
        setLoading(false);
      }
    };
    init();
  }, [token]);

  const handleEvaluate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedClientId || !selectedAudience || !route || !method) return;
    try {
      setEvaluating(true);
      const res = await adminApi.checkAccess(selectedClientId, selectedAudience, route, method);
      setResult(res);
    } catch (err: any) {
      alert(`Evaluation error: ${err.message}`);
    } finally {
      setEvaluating(false);
    }
  };

  const handleAudienceChange = (aud: string) => {
    setSelectedAudience(aud);
    if (aud.includes('orders')) setRoute('/orders');
    else if (aud.includes('customers')) setRoute('/customers');
    else if (aud.includes('inventory')) setRoute('/inventory');
  };

  return (
    <div className="tab-pane">
      <div className="section-header">
        <div>
          <h2>Access Checker & Policy Simulator</h2>
          <p className="subtitle">
            Simulate whether a client application is authorized to invoke a specific route and HTTP method.
          </p>
        </div>
      </div>

      <div className="checker-layout">
        {/* Form panel */}
        <div className="card checker-form-card">
          <form onSubmit={handleEvaluate}>
            <div className="form-group">
              <label>Client Application (Caller)</label>
              <select
                value={selectedClientId}
                onChange={(e) => setSelectedClientId(e.target.value)}
                disabled={loading}
              >
                {clients.map((c) => (
                  <option key={c.id} value={c.clientId}>
                    {c.clientName} ({c.clientId}) {!c.isActive ? '[DISABLED]' : ''}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <label>Target API Service (Audience)</label>
              <select
                value={selectedAudience}
                onChange={(e) => handleAudienceChange(e.target.value)}
                disabled={loading}
              >
                {apis.map((a) => (
                  <option key={a.id} value={a.audience}>
                    {a.name} ({a.audience})
                  </option>
                ))}
              </select>
            </div>

            <div className="form-row">
              <div className="form-group flex-1">
                <label>HTTP Method</label>
                <select value={method} onChange={(e) => setMethod(e.target.value)}>
                  <option value="GET">GET (Read)</option>
                  <option value="POST">POST (Create / Action)</option>
                  <option value="PUT">PUT (Update)</option>
                  <option value="DELETE">DELETE (Remove)</option>
                </select>
              </div>

              <div className="form-group flex-2">
                <label>API Route Path</label>
                <input
                  type="text"
                  placeholder="e.g. /orders, /customers/1, /inventory"
                  value={route}
                  onChange={(e) => setRoute(e.target.value)}
                  required
                />
              </div>
            </div>

            <div className="quick-routes">
              <span className="text-xs text-muted">Quick Presets:</span>
              <button type="button" className="btn-chip" onClick={() => { setRoute('/orders'); setMethod('GET'); }}>GET /orders</button>
              <button type="button" className="btn-chip" onClick={() => { setRoute('/orders'); setMethod('POST'); }}>POST /orders</button>
              <button type="button" className="btn-chip" onClick={() => { setRoute('/customers'); setMethod('GET'); }}>GET /customers</button>
              <button type="button" className="btn-chip" onClick={() => { setRoute('/inventory'); setMethod('GET'); }}>GET /inventory</button>
              <button type="button" className="btn-chip" onClick={() => { setRoute('/inventory/1'); setMethod('PUT'); }}>PUT /inventory/1</button>
            </div>

            <button type="submit" className="btn btn-primary w-full mt-4" disabled={evaluating}>
              <Search size={16} /> {evaluating ? 'Evaluating Access...' : 'Check Access Rule'}
            </button>
          </form>
        </div>

        {/* Evaluation Output panel */}
        <div className="card checker-result-card">
          {!result && (
            <div className="empty-checker-state">
              <Cpu size={48} className="text-muted mb-3" />
              <h3>Awaiting Simulation</h3>
              <p className="text-muted text-sm">
                Select a client, target API, route, and method, then click "Check Access Rule" to inspect evaluation details.
              </p>
            </div>
          )}

          {result && (
            <div className="evaluation-result">
              <div className={`result-banner ${result.allowed ? 'allowed' : 'denied'}`}>
                {result.allowed ? (
                  <>
                    <ShieldCheck size={36} className="text-success" />
                    <div>
                      <h3 className="text-success">ACCESS ALLOWED (200 OK)</h3>
                      <p className="text-sm">Client is authorized for this operation.</p>
                    </div>
                  </>
                ) : (
                  <>
                    <ShieldAlert size={36} className="text-danger" />
                    <div>
                      <h3 className="text-danger">ACCESS DENIED (403 FORBIDDEN / 401)</h3>
                      <p className="text-sm">Client request would be rejected.</p>
                    </div>
                  </>
                )}
              </div>

              <div className="result-details">
                <div className="detail-item">
                  <span className="detail-label">Reason:</span>
                  <span className="detail-value font-medium">{result.reason}</span>
                </div>
                <div className="detail-item">
                  <span className="detail-label">Required Scope:</span>
                  <span className="detail-value mono font-bold text-accent">{result.requiredScope}</span>
                </div>
                <div className="detail-item">
                  <span className="detail-label">Client's Active Scopes:</span>
                  <div className="scope-tags">
                    {result.clientScopes.length > 0 ? (
                      result.clientScopes.map((s) => (
                        <span
                          key={s}
                          className={`badge ${
                            s === result.requiredScope ? 'badge-success' : 'badge-neutral'
                          }`}
                        >
                          {s}
                        </span>
                      ))
                    ) : (
                      <span className="text-muted text-xs italic">No scopes granted to this client.</span>
                    )}
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
