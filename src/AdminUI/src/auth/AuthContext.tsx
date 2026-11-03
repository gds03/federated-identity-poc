import React, { createContext, useContext, useState, useEffect, useCallback, useRef } from 'react';
import {
  type IdpProvider,
  getOidcConfig,
  saveIdpSettings,
  resolveOidcEndpoints,
  getSavedProvider,
  getSavedAuth0Domain,
  getSavedAuth0ClientId,
} from './oidcConfig';
import { generateRandomString, generateCodeChallenge, parseJwtPayload } from './pkce';

export interface AuthUser {
  sub: string;
  name: string;
  email: string;
  roles: string[];
  groups: string[];
  provider: IdpProvider;
}

interface AuthContextType {
  user: AuthUser | null;
  token: string | null;
  isAdmin: boolean;
  isReadOnly: boolean;
  provider: IdpProvider;
  auth0Domain: string;
  auth0ClientId: string;
  isLoggingIn: boolean;
  authError: string | null;
  login: (userPreset?: 'alice' | 'bob') => Promise<void>;
  logout: () => Promise<void>;
  switchRole: (preset: 'alice' | 'bob') => Promise<void>;
  switchProvider: (newProvider: IdpProvider, domain?: string, clientId?: string) => void;
  updateAuth0Config: (domain: string, clientId: string) => void;
  toggleAdminBypass: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [provider, setProviderState] = useState<IdpProvider>(getSavedProvider);
  const [auth0Domain, setAuth0DomainState] = useState<string>(getSavedAuth0Domain);
  const [auth0ClientId, setAuth0ClientIdState] = useState<string>(getSavedAuth0ClientId);
  const [isLoggingIn, setIsLoggingIn] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);
  const [adminBypass, setAdminBypass] = useState<boolean>(() => {
    return sessionStorage.getItem('oidc_admin_bypass') === 'true';
  });

  // Guard against React 19 StrictMode double-execution of authorization code exchange
  const codeHandledRef = useRef<string | null>(null);

  const [user, setUser] = useState<AuthUser | null>(() => {
    const saved = sessionStorage.getItem('oidc_user');
    if (saved) {
      try {
        return JSON.parse(saved);
      } catch {}
    }
    // If Mock OIDC is the active provider, default to Alice Admin for convenient zero-friction local dev
    const activeProv = getSavedProvider();
    if (activeProv === 'mock') {
      const defaultUser: AuthUser = {
        sub: 'usr-alice-admin',
        name: 'Alice Admin',
        email: 'alice.admin@internal',
        roles: ['access-admin'],
        groups: ['access-admin'],
        provider: 'mock',
      };
      if (!sessionStorage.getItem('oidc_token')) {
        const header = btoa(JSON.stringify({ alg: 'RS256', typ: 'JWT' })).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
        const exp = Math.floor(Date.now() / 1000) + 86400;
        const payload = btoa(JSON.stringify({
          sub: defaultUser.sub,
          name: defaultUser.name,
          email: defaultUser.email,
          roles: defaultUser.roles,
          groups: defaultUser.groups,
          exp,
        })).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
        sessionStorage.setItem('oidc_token', `${header}.${payload}.mock_sig`);
      }
      return defaultUser;
    }
    // For Auth0, start unauthenticated until user signs in with Auth0
    return null;
  });

  const [token, setToken] = useState<string | null>(() => sessionStorage.getItem('oidc_token'));

  // Determine admin rights
  const isAdmin = Boolean(
    adminBypass ||
      user?.roles?.includes('access-admin') ||
      user?.groups?.includes('access-admin') ||
      user?.roles?.includes('admin')
  );
  const isReadOnly = !isAdmin;

  // Handle OIDC authorization code callback
  useEffect(() => {
    const handleCallback = async () => {
      const params = new URLSearchParams(window.location.search);
      const code = params.get('code');
      const returnedState = params.get('state');

      if (!code) return;

      // Prevent duplicate code exchange in React StrictMode
      if (codeHandledRef.current === code) {
        return;
      }
      codeHandledRef.current = code;

      // Strip query parameters immediately so reloads don't retry with spent code
      window.history.replaceState({}, document.title, window.location.pathname);

      setIsLoggingIn(true);
      setAuthError(null);

      const storedVerifier = sessionStorage.getItem('pkce_verifier');
      const storedState = sessionStorage.getItem('pkce_state');
      const storedProvider = (sessionStorage.getItem('pkce_provider') as IdpProvider) || provider;
      const storedClientId = sessionStorage.getItem('pkce_client_id');
      const storedRedirectUri = sessionStorage.getItem('pkce_redirect_uri');

      if (storedState && returnedState && storedState !== returnedState) {
        console.error('OIDC State mismatch! Possible CSRF attack.');
        setAuthError('OIDC state verification failed. Possible CSRF attack or expired session.');
        setIsLoggingIn(false);
        return;
      }

      if (!storedVerifier) {
        console.error('Missing PKCE verifier in session storage.');
        setAuthError('Missing PKCE code verifier in browser session. Please try signing in again.');
        setIsLoggingIn(false);
        return;
      }

      try {
        const config = getOidcConfig(storedProvider);
        const endpoints = await resolveOidcEndpoints(config);
        const activeClientId = storedClientId || config.clientId;
        const activeRedirectUri = storedRedirectUri || config.redirectUri;

        if (!activeClientId) {
          throw new Error('Missing Client ID for token exchange. Check IdP Settings.');
        }

        // Exchange code + code_verifier for tokens at token endpoint (RFC 7636)
        const response = await fetch(endpoints.token_endpoint, {
          method: 'POST',
          headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
          body: new URLSearchParams({
            grant_type: 'authorization_code',
            client_id: activeClientId,
            code,
            code_verifier: storedVerifier,
            redirect_uri: activeRedirectUri,
          }),
        });

        if (!response.ok) {
          const errText = await response.text();
          let parsedError = errText;
          try {
            const errJson = JSON.parse(errText);
            parsedError = errJson.error_description || errJson.error || errText;
          } catch {}
          throw new Error(`Token exchange failed (${response.status}): ${parsedError}`);
        }

        const data = await response.json();
        const idToken = data.id_token || data.access_token;
        const payload = parseJwtPayload(idToken);

        if (payload) {
          // Check standard claims and Auth0 custom namespaced claims
          const rawGroups =
            payload['https://federated.internal/roles'] ||
            payload['https://federated-poc/roles'] ||
            payload['https://federated.internal/groups'] ||
            payload.roles ||
            payload.groups ||
            payload.permissions ||
            [];

          const groupsArray = Array.isArray(rawGroups) ? rawGroups : [rawGroups];

          const authedUser: AuthUser = {
            sub: payload.sub || 'user-unknown',
            name: payload.name || payload.nickname || payload.preferred_username || payload.email || 'Auth0 User',
            email: payload.email || '',
            roles: groupsArray,
            groups: groupsArray,
            provider: storedProvider,
          };

          setUser(authedUser);
          setToken(idToken);
          setProviderState(storedProvider);
          sessionStorage.setItem('oidc_user', JSON.stringify(authedUser));
          sessionStorage.setItem('oidc_token', idToken);
        }
      } catch (err: any) {
        console.error('Failed to complete OIDC login:', err);
        setAuthError(err.message || 'Failed to complete OIDC login');
        codeHandledRef.current = null;
      } finally {
        setIsLoggingIn(false);
        sessionStorage.removeItem('pkce_verifier');
        sessionStorage.removeItem('pkce_state');
        sessionStorage.removeItem('pkce_provider');
        sessionStorage.removeItem('pkce_client_id');
        sessionStorage.removeItem('pkce_redirect_uri');
      }
    };

    handleCallback();
  }, [provider]);

  const login = useCallback(async (userPreset?: 'alice' | 'bob') => {
    try {
      setIsLoggingIn(true);
      setAuthError(null);
      codeHandledRef.current = null;

      const config = getOidcConfig(provider);
      if (provider === 'auth0' && !config.clientId) {
        throw new Error('Please enter your Auth0 Client ID in IdP Settings before logging in.');
      }

      const endpoints = await resolveOidcEndpoints(config);
      const verifier = generateRandomString(64);
      const challenge = await generateCodeChallenge(verifier);
      const state = generateRandomString(16);

      sessionStorage.setItem('pkce_verifier', verifier);
      sessionStorage.setItem('pkce_state', state);
      sessionStorage.setItem('pkce_provider', provider);
      sessionStorage.setItem('pkce_client_id', config.clientId);
      sessionStorage.setItem('pkce_redirect_uri', config.redirectUri);

      const authUrl = new URL(endpoints.authorization_endpoint);
      authUrl.searchParams.set('client_id', config.clientId);
      authUrl.searchParams.set('redirect_uri', config.redirectUri);
      authUrl.searchParams.set('response_type', config.responseType);
      authUrl.searchParams.set('scope', config.scope);
      authUrl.searchParams.set('code_challenge', challenge);
      authUrl.searchParams.set('code_challenge_method', 'S256');
      authUrl.searchParams.set('state', state);

      if (userPreset && provider === 'mock') {
        authUrl.searchParams.set('user', userPreset);
      }

      window.location.href = authUrl.toString();
    } catch (err: any) {
      console.error('Failed to initiate login:', err);
      setAuthError(err.message || 'Failed to initiate login');
      setIsLoggingIn(false);
    }
  }, [provider]);

  const logout = useCallback(async () => {
    const currentProv = provider;
    setUser(null);
    setToken(null);
    setAdminBypass(false);
    codeHandledRef.current = null;
    sessionStorage.removeItem('oidc_user');
    sessionStorage.removeItem('oidc_token');
    sessionStorage.removeItem('oidc_admin_bypass');

    if (currentProv === 'auth0') {
            try {
        const config = getOidcConfig('auth0');
        if (config.clientId) {
          // Always use Auth0's /v2/logout: it honors `returnTo` and sends the browser back to the app.
          // The discovery document's end_session_endpoint (/oidc/logout) ignores `returnTo`
          // and strands the user on Auth0's "You have successfully logged out" page.
          // NOTE: window.location.origin must be listed in Auth0 "Allowed Logout URLs".
          const logoutUrl = new URL(`${config.authority.replace(/\/+$/, '')}/v2/logout`);
          logoutUrl.searchParams.set('client_id', config.clientId);
          logoutUrl.searchParams.set('returnTo', window.location.origin);
          window.location.href = logoutUrl.toString();
          return;
        }
      } catch (e) {
        console.warn('Could not redirect to Auth0 logout endpoint:', e);
      }
    }
  }, [provider]);

  const switchRole = useCallback(async (preset: 'alice' | 'bob') => {
    if (provider === 'mock') {
      await login(preset);
    }
  }, [login, provider]);

  const switchProvider = useCallback((newProvider: IdpProvider, domain?: string, clientId?: string) => {
    setProviderState(newProvider);
    saveIdpSettings(newProvider, domain, clientId);
    if (domain) setAuth0DomainState(domain.trim());
    if (clientId) setAuth0ClientIdState(clientId.trim());

    // Reset current user when switching provider
    setUser(null);
    setToken(null);
    codeHandledRef.current = null;
    sessionStorage.removeItem('oidc_user');
    sessionStorage.removeItem('oidc_token');

    // If switched back to mock, populate default Alice Admin
    if (newProvider === 'mock') {
      const defaultAlice: AuthUser = {
        sub: 'usr-alice-admin',
        name: 'Alice Admin',
        email: 'alice.admin@internal',
        roles: ['access-admin'],
        groups: ['access-admin'],
        provider: 'mock',
      };
      setUser(defaultAlice);
      sessionStorage.setItem('oidc_user', JSON.stringify(defaultAlice));
    }
  }, []);

  const updateAuth0Config = useCallback((domain: string, clientId: string) => {
    setAuth0DomainState(domain.trim());
    setAuth0ClientIdState(clientId.trim());
    saveIdpSettings(provider, domain, clientId);
  }, [provider]);

  const toggleAdminBypass = useCallback(() => {
    setAdminBypass((prev) => {
      const next = !prev;
      sessionStorage.setItem('oidc_admin_bypass', String(next));
      return next;
    });
  }, []);

  return (
    <AuthContext.Provider
      value={{
        user,
        token,
        isAdmin,
        isReadOnly,
        provider,
        auth0Domain,
        auth0ClientId,
        isLoggingIn,
        authError,
        login,
        logout,
        switchRole,
        switchProvider,
        updateAuth0Config,
        toggleAdminBypass,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
