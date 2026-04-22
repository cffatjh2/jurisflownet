export type OAuthProvider = 'google' | 'zoom' | 'microsoft';
export type OAuthTarget = 'gmail' | 'google-docs' | 'google-meet' | 'zoom' | 'microsoft-teams';

type OAuthTokenKeyPair = {
  access: string;
  refresh: string;
};

const TOKEN_KEYS: Record<OAuthTarget, OAuthTokenKeyPair> = {
  'gmail': { access: 'gmail_access_token', refresh: 'gmail_refresh_token' },
  'google-docs': { access: 'google_docs_access_token', refresh: 'google_docs_refresh_token' },
  'google-meet': { access: 'google_meet_access_token', refresh: 'google_meet_refresh_token' },
  'zoom': { access: 'zoom_access_token', refresh: 'zoom_refresh_token' },
  'microsoft-teams': { access: 'microsoft_access_token', refresh: 'microsoft_refresh_token' }
};

const hasWindow = () => typeof window !== 'undefined';

const getPrimaryAuthToken = (): string | null => {
  if (!hasWindow()) return null;
  return localStorage.getItem('auth_token') || localStorage.getItem('client_token');
};

export const buildOAuthAuthHeaders = (includeJsonContentType: boolean = true): HeadersInit => {
  const headers: Record<string, string> = {};
  const token = getPrimaryAuthToken();
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }

  if (hasWindow()) {
    const tenantSlug = localStorage.getItem('tenant_slug');
    if (tenantSlug) {
      headers['X-Tenant-Slug'] = tenantSlug;
    }
  }

  if (includeJsonContentType) {
    headers['Content-Type'] = 'application/json';
  }

  return headers;
};

const readSessionToken = (key: string): string | null => {
  if (!hasWindow()) return null;
  return sessionStorage.getItem(key);
};

const readLegacyToken = (key: string): string | null => {
  if (!hasWindow()) return null;
  return localStorage.getItem(key);
};

const removeLegacyToken = (key: string): void => {
  if (!hasWindow()) return;
  localStorage.removeItem(key);
};

export const getOAuthAccessToken = (target: OAuthTarget): string | null => {
  const keys = TOKEN_KEYS[target];
  const sessionToken = readSessionToken(keys.access);
  if (sessionToken) return sessionToken;

  // One-time migration from legacy localStorage keys.
  const legacyToken = readLegacyToken(keys.access);
  if (legacyToken && hasWindow()) {
    sessionStorage.setItem(keys.access, legacyToken);
    removeLegacyToken(keys.access);
    return legacyToken;
  }

  return null;
};

export const getOAuthRefreshToken = (target: OAuthTarget): string | null => {
  const keys = TOKEN_KEYS[target];
  if (hasWindow()) {
    sessionStorage.removeItem(keys.refresh);
    removeLegacyToken(keys.refresh);
  }

  return null;
};

export const setOAuthTokens = (target: OAuthTarget, accessToken: string, _refreshToken?: string | null): void => {
  if (!hasWindow()) return;
  const keys = TOKEN_KEYS[target];
  sessionStorage.setItem(keys.access, accessToken);
  removeLegacyToken(keys.access);
  sessionStorage.removeItem(keys.refresh);
  removeLegacyToken(keys.refresh);
};

export const clearOAuthTokens = (target: OAuthTarget): void => {
  if (!hasWindow()) return;
  const keys = TOKEN_KEYS[target];
  sessionStorage.removeItem(keys.access);
  sessionStorage.removeItem(keys.refresh);
  removeLegacyToken(keys.access);
  removeLegacyToken(keys.refresh);
};

export const getPreferredGoogleAccessToken = (): string | null => {
  return getOAuthAccessToken('google-meet')
    || getOAuthAccessToken('gmail')
    || getOAuthAccessToken('google-docs');
};

export const refreshGoogleOAuthAccessToken = async (
  target: Extract<OAuthTarget, 'gmail' | 'google-docs' | 'google-meet'>
): Promise<string | null> => {
  const response = await fetch('/api/google/oauth/refresh', {
    method: 'POST',
    headers: buildOAuthAuthHeaders(true),
    body: JSON.stringify({
      target
    })
  });

  const payload = await response.json().catch(() => ({}));
  if (!response.ok || !payload?.accessToken || typeof payload.accessToken !== 'string') {
    const message = payload?.message || 'Unable to refresh Google authorization.';
    throw new Error(message);
  }

  setOAuthTokens(target, payload.accessToken);
  return payload.accessToken;
};

export const requestOAuthState = async (
  provider: OAuthProvider,
  target: OAuthTarget,
  returnPath: string
): Promise<string> => {
  const response = await fetch('/api/oauth/state', {
    method: 'POST',
    headers: buildOAuthAuthHeaders(true),
    body: JSON.stringify({
      provider,
      target,
      returnPath
    })
  });

  const payload = await response.json().catch(() => ({}));
  if (!response.ok || !payload?.state || typeof payload.state !== 'string') {
    const message = payload?.message || 'Unable to initialize OAuth state.';
    throw new Error(message);
  }

  return payload.state;
};
