export interface OidcConfiguration {
  authority: string;
  clientId: string;
  redirectUri: string;
  scope: string;
  responseType: string;
}

export const oidcConfig: OidcConfiguration = {
  // Central Authority (Local Mock OIDC Server):
  authority: 'http://localhost:5001/mock-oidc',

  // To switch to a production OneLogin tenant, simply set:
  // authority: 'https://<your-company>.onelogin.com/oidc/2',
  // clientId: '<your-onelogin-oidc-app-client-id>',

  clientId: 'admin-ui-spa',
  redirectUri: window.location.origin,
  scope: 'openid profile email groups',
  responseType: 'code',
};
