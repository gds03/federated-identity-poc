export type IdpProvider = 'mock' | 'auth0';

export interface OidcConfiguration {
  provider: IdpProvider;
  authority: string;
  clientId: string;
  redirectUri: string;
  scope: string;
  responseType: string;
  auth0Domain?: string;
  auth0ClientId?: string;
}

export interface OidcEndpoints {
  authorization_endpoint: string;
  token_endpoint: string;
  end_session_endpoint?: string;
  userinfo_endpoint?: string;
  jwks_uri?: string;
}

const STORAGE_KEY_PROVIDER = 'federated_idp_provider';
const STORAGE_KEY_AUTH0_DOMAIN = 'federated_auth0_domain';
const STORAGE_KEY_AUTH0_CLIENT_ID = 'federated_auth0_client_id';

// Read defaults from environment variables or localStorage
export function getSavedProvider(): IdpProvider {
  const saved = localStorage.getItem(STORAGE_KEY_PROVIDER);
  if (saved === 'auth0' || saved === 'mock') {
    return saved;
  }
  if (import.meta.env.VITE_IDP_PROVIDER === 'auth0') {
    return 'auth0';
  }
  return 'mock';
}

export function getSavedAuth0Domain(): string {
  return (
    localStorage.getItem(STORAGE_KEY_AUTH0_DOMAIN) ||
    import.meta.env.VITE_AUTH0_DOMAIN ||
    'your-tenant.us.auth0.com'
  );
}

export function getSavedAuth0ClientId(): string {
  return (
    localStorage.getItem(STORAGE_KEY_AUTH0_CLIENT_ID) ||
    import.meta.env.VITE_AUTH0_CLIENT_ID ||
    ''
  );
}

export function formatAuth0Authority(domain: string): string {
  let clean = domain.trim();
  if (!clean.startsWith('http://') && !clean.startsWith('https://')) {
    clean = `https://${clean}`;
  }
  return clean.replace(/\/+$/, '');
}

export function getOidcConfig(providerOverride?: IdpProvider): OidcConfiguration {
  const provider = providerOverride || getSavedProvider();
  const auth0Domain = getSavedAuth0Domain();
  const auth0ClientId = getSavedAuth0ClientId();

  if (provider === 'auth0') {
    const authority = formatAuth0Authority(auth0Domain);
    return {
      provider: 'auth0',
      authority,
      clientId: auth0ClientId.trim(),
      redirectUri: window.location.origin,
      scope: 'openid profile email',
      responseType: 'code',
      auth0Domain,
      auth0ClientId,
    };
  }

  // Local Mock OIDC Server on AuthService (:5001)
  return {
    provider: 'mock',
    authority: 'http://localhost:5001/mock-oidc',
    clientId: 'admin-ui-spa',
    redirectUri: window.location.origin,
    scope: 'openid profile email groups',
    responseType: 'code',
    auth0Domain,
    auth0ClientId,
  };
}

export function saveIdpSettings(provider: IdpProvider, auth0Domain?: string, auth0ClientId?: string) {
  localStorage.setItem(STORAGE_KEY_PROVIDER, provider);
  if (auth0Domain !== undefined) {
    localStorage.setItem(STORAGE_KEY_AUTH0_DOMAIN, auth0Domain.trim());
  }
  if (auth0ClientId !== undefined) {
    localStorage.setItem(STORAGE_KEY_AUTH0_CLIENT_ID, auth0ClientId.trim());
  }
}

export async function resolveOidcEndpoints(config: OidcConfiguration): Promise<OidcEndpoints> {
  const cleanAuthority = config.authority.replace(/\/+$/, '');

  try {
    const res = await fetch(`${cleanAuthority}/.well-known/openid-configuration`);
    if (res.ok) {
      const doc = await res.json();
      return {
        authorization_endpoint: doc.authorization_endpoint,
        token_endpoint: doc.token_endpoint,
        end_session_endpoint: doc.end_session_endpoint,
        userinfo_endpoint: doc.userinfo_endpoint,
        jwks_uri: doc.jwks_uri,
      };
    }
  } catch (err) {
    console.warn('OIDC Discovery failed, falling back to endpoint conventions:', err);
  }

  // Fallback defaults if discovery is unavailable
  if (config.provider === 'auth0' || cleanAuthority.includes('auth0.com')) {
    return {
      authorization_endpoint: `${cleanAuthority}/authorize`,
      token_endpoint: `${cleanAuthority}/oauth/token`,
      end_session_endpoint: `${cleanAuthority}/v2/logout`,
      userinfo_endpoint: `${cleanAuthority}/userinfo`,
      jwks_uri: `${cleanAuthority}/.well-known/jwks.json`,
    };
  }

  return {
    authorization_endpoint: `${cleanAuthority}/authorize`,
    token_endpoint: `${cleanAuthority}/token`,
    userinfo_endpoint: `${cleanAuthority}/userinfo`,
    jwks_uri: `${cleanAuthority}/.well-known/jwks.json`,
  };
}

// Global active configuration
export const oidcConfig: OidcConfiguration = getOidcConfig();
