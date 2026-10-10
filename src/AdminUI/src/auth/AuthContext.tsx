import React, { createContext, useContext, useState, useEffect } from 'react';
import { oidcConfig } from './oidcConfig';
import { generateRandomString, generateCodeChallenge, parseJwtPayload } from './pkce';

export interface AuthUser {
  sub: string;
  name: string;
  email: string;
  roles: string[];
  groups: string[];
}

interface AuthContextType {
  user: AuthUser | null;
  token: string | null;
  isAdmin: boolean;
  isReadOnly: boolean;
  login: (userPreset?: 'alice' | 'bob') => Promise<void>;
  logout: () => void;
  switchRole: (preset: 'alice' | 'bob') => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<AuthUser | null>(() => {
    const saved = sessionStorage.getItem('oidc_user');
    if (saved) {
      try {
        return JSON.parse(saved);
      } catch {}
    }
    // Default to Alice Admin for instant local dev experience
    return {
      sub: 'usr-alice-admin',
      name: 'Alice Admin',
      email: 'alice.admin@internal',
      roles: ['access-admin'],
      groups: ['access-admin'],
    };
  });

  const [token, setToken] = useState<string | null>(() => sessionStorage.getItem('oidc_token'));

  // Determine admin rights
  const isAdmin = Boolean(
    user?.roles?.includes('access-admin') || user?.groups?.includes('access-admin')
  );
  const isReadOnly = !isAdmin;

  // Handle OIDC authorization code callback
  useEffect(() => {
    const handleCallback = async () => {
      const params = new URLSearchParams(window.location.search);
      const code = params.get('code');
      const returnedState = params.get('state');

      if (!code) return;

      const storedVerifier = sessionStorage.getItem('pkce_verifier');
      const storedState = sessionStorage.getItem('pkce_state');

      if (storedState && returnedState && storedState !== returnedState) {
        console.error('OIDC State mismatch! Possible CSRF attack.');
        return;
      }

      if (!storedVerifier) {
        console.error('Missing PKCE verifier in session storage.');
        return;
      }

      try {
        // Exchange code + code_verifier for tokens at token endpoint (RFC 7636)
        const response = await fetch(`${oidcConfig.authority}/token`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
          body: new URLSearchParams({
            grant_type: 'authorization_code',
            client_id: oidcConfig.clientId,
            code,
            code_verifier: storedVerifier,
            redirect_uri: oidcConfig.redirectUri,
          }),
        });

        if (!response.ok) {
          const err = await response.text();
          throw new Error(`Token exchange failed: ${err}`);
        }

        const data = await response.json();
        const idToken = data.id_token || data.access_token;
        const payload = parseJwtPayload(idToken);

        if (payload) {
          const rawGroups = payload.groups || payload.roles || [];
          const groupsArray = Array.isArray(rawGroups) ? rawGroups : [rawGroups];

          const authedUser: AuthUser = {
            sub: payload.sub,
            name: payload.name || payload.preferred_username || 'Admin User',
            email: payload.email || '',
            roles: groupsArray,
            groups: groupsArray,
          };

          setUser(authedUser);
          setToken(idToken);
          sessionStorage.setItem('oidc_user', JSON.stringify(authedUser));
          sessionStorage.setItem('oidc_token', idToken);
        }

        // Clean query parameters from URL
        window.history.replaceState({}, document.title, window.location.pathname);
      } catch (err) {
        console.error('Failed to complete OIDC login:', err);
      } finally {
        sessionStorage.removeItem('pkce_verifier');
        sessionStorage.removeItem('pkce_state');
      }
    };

    handleCallback();
  }, []);

  const login = async (userPreset?: 'alice' | 'bob') => {
    const verifier = generateRandomString(64);
    const challenge = await generateCodeChallenge(verifier);
    const state = generateRandomString(16);

    sessionStorage.setItem('pkce_verifier', verifier);
    sessionStorage.setItem('pkce_state', state);

    const authUrl = new URL(`${oidcConfig.authority}/authorize`);
    authUrl.searchParams.set('client_id', oidcConfig.clientId);
    authUrl.searchParams.set('redirect_uri', oidcConfig.redirectUri);
    authUrl.searchParams.set('response_type', oidcConfig.responseType);
    authUrl.searchParams.set('scope', oidcConfig.scope);
    authUrl.searchParams.set('code_challenge', challenge);
    authUrl.searchParams.set('code_challenge_method', 'S256');
    authUrl.searchParams.set('state', state);
    if (userPreset) {
      authUrl.searchParams.set('user', userPreset);
    }

    window.location.href = authUrl.toString();
  };

  const logout = () => {
    setUser(null);
    setToken(null);
    sessionStorage.removeItem('oidc_user');
    sessionStorage.removeItem('oidc_token');
  };

  const switchRole = async (preset: 'alice' | 'bob') => {
    await login(preset);
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        token,
        isAdmin,
        isReadOnly,
        login,
        logout,
        switchRole,
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
