export type IntegrationOAuthProviderKey =
  | 'google-gmail'
  | 'quickbooks-online'
  | 'xero'
  | 'microsoft-outlook-calendar'
  | 'microsoft-outlook-mail';

type IntegrationOAuthPendingState = {
  providerKey: IntegrationOAuthProviderKey;
  issuedAtUnixMs: number;
  returnPath: string;
};

type IntegrationOAuthPendingMap = Record<string, IntegrationOAuthPendingState>;

type ProviderSlug = 'gmail' | 'quickbooks' | 'xero' | 'outlook' | 'outlook-mail';

type IntegrationOAuthClientConfig = {
  clientId: string;
  scopes: string;
  tenantId?: string;
};

export type IntegrationOAuthValidationResult = {
  valid: boolean;
  returnPath: string;
  message?: string;
};

const INTEGRATION_OAUTH_STATE_STORAGE_KEY = 'jf.integration.oauth.state.v1';
const INTEGRATION_OAUTH_STATE_MAX_AGE_MS = 10 * 60 * 1000;
const DEFAULT_RETURN_PATH = '/#settings-integrations';

const PROVIDER_SLUGS: Record<IntegrationOAuthProviderKey, ProviderSlug> = {
  'google-gmail': 'gmail',
  'quickbooks-online': 'quickbooks',
  'xero': 'xero',
  'microsoft-outlook-calendar': 'outlook',
  'microsoft-outlook-mail': 'outlook-mail'
};

const hasWindow = (): boolean => typeof window !== 'undefined';

export const isIntegrationOAuthProviderKey = (value: string): value is IntegrationOAuthProviderKey => {
  return value === 'google-gmail'
    || value === 'quickbooks-online'
    || value === 'xero'
    || value === 'microsoft-outlook-calendar'
    || value === 'microsoft-outlook-mail';
};

export const normalizeReturnPath = (value: string | null | undefined): string => {
  if (!value) return DEFAULT_RETURN_PATH;
  const trimmed = value.trim();
  if (!trimmed.startsWith('/') || trimmed.startsWith('//')) {
    return DEFAULT_RETURN_PATH;
  }

  return trimmed;
};

export const getIntegrationOAuthCallbackPath = (providerKey: IntegrationOAuthProviderKey): string => {
  return `/auth/integrations/${PROVIDER_SLUGS[providerKey]}/callback`;
};

export const parseIntegrationOAuthProviderFromPath = (pathname: string): IntegrationOAuthProviderKey | null => {
  const match = pathname.match(/^\/auth\/integrations\/([^/]+)\/callback$/i);
  if (!match) {
    return null;
  }

  const slug = match[1].toLowerCase();
  if (slug === 'gmail') return 'google-gmail';
  if (slug === 'quickbooks') return 'quickbooks-online';
  if (slug === 'xero') return 'xero';
  if (slug === 'outlook') return 'microsoft-outlook-calendar';
  if (slug === 'outlook-mail') return 'microsoft-outlook-mail';
  return null;
};

export const buildIntegrationOAuthAuthorizeUrl = (
  providerKey: IntegrationOAuthProviderKey,
  config: IntegrationOAuthClientConfig,
  redirectUri: string,
  state: string
): string => {
  if (providerKey === 'google-gmail') {
    const params = new URLSearchParams({
      client_id: config.clientId,
      response_type: 'code',
      access_type: 'offline',
      prompt: 'consent',
      scope: config.scopes,
      redirect_uri: redirectUri,
      state
    });
    return `https://accounts.google.com/o/oauth2/v2/auth?${params.toString()}`;
  }

  if (providerKey === 'quickbooks-online') {
    const params = new URLSearchParams({
      client_id: config.clientId,
      response_type: 'code',
      scope: config.scopes,
      redirect_uri: redirectUri,
      state
    });
    return `https://appcenter.intuit.com/connect/oauth2?${params.toString()}`;
  }

  if (providerKey === 'xero') {
    const params = new URLSearchParams({
      response_type: 'code',
      client_id: config.clientId,
      redirect_uri: redirectUri,
      scope: config.scopes,
      state
    });
    return `https://login.xero.com/identity/connect/authorize?${params.toString()}`;
  }

  const tenantId = (config.tenantId || 'common').trim();
  const params = new URLSearchParams({
    client_id: config.clientId,
    response_type: 'code',
    redirect_uri: redirectUri,
    response_mode: 'query',
    scope: config.scopes,
    state
  });
  return `https://login.microsoftonline.com/${encodeURIComponent(tenantId)}/oauth2/v2.0/authorize?${params.toString()}`;
};

export const startIntegrationOAuth = (
  providerKey: IntegrationOAuthProviderKey,
  returnPath: string = DEFAULT_RETURN_PATH
): string => {
  if (!hasWindow()) {
    throw new Error('OAuth can only be started in browser context.');
  }

  const redirectUri = `${window.location.origin}${getIntegrationOAuthCallbackPath(providerKey)}`;
  const state = createStateToken();
  rememberPendingState(state, {
    providerKey,
    issuedAtUnixMs: Date.now(),
    returnPath: normalizeReturnPath(returnPath)
  });

  const config = loadIntegrationOAuthClientConfig(providerKey);
  const authUrl = buildIntegrationOAuthAuthorizeUrl(providerKey, config, redirectUri, state);
  window.location.assign(authUrl);
  return authUrl;
};

export const consumeIntegrationOAuthState = (
  providerKey: IntegrationOAuthProviderKey,
  state: string | null,
  fallbackReturnPath: string = DEFAULT_RETURN_PATH
): IntegrationOAuthValidationResult => {
  const normalizedReturnPath = normalizeReturnPath(fallbackReturnPath);
  if (!state || !state.trim()) {
    return {
      valid: false,
      returnPath: normalizedReturnPath,
      message: 'OAuth state is missing.'
    };
  }

  if (!hasWindow()) {
    return {
      valid: false,
      returnPath: normalizedReturnPath,
      message: 'OAuth state cannot be verified in this environment.'
    };
  }

  const allStates = readPendingStates();
  const pending = allStates[state];
  delete allStates[state];
  writePendingStates(allStates);

  if (!pending) {
    return {
      valid: false,
      returnPath: normalizedReturnPath,
      message: 'OAuth state is invalid or already consumed.'
    };
  }

  if (pending.providerKey !== providerKey) {
    return {
      valid: false,
      returnPath: normalizeReturnPath(pending.returnPath),
      message: 'OAuth provider does not match pending state.'
    };
  }

  const ageMs = Date.now() - pending.issuedAtUnixMs;
  if (ageMs < 0 || ageMs > INTEGRATION_OAUTH_STATE_MAX_AGE_MS) {
    return {
      valid: false,
      returnPath: normalizeReturnPath(pending.returnPath),
      message: 'OAuth state has expired. Please retry.'
    };
  }

  return {
    valid: true,
    returnPath: normalizeReturnPath(pending.returnPath)
  };
};

const loadIntegrationOAuthClientConfig = (providerKey: IntegrationOAuthProviderKey): IntegrationOAuthClientConfig => {
  if (providerKey === 'google-gmail') {
    return {
      clientId: requireClientValue('VITE_GOOGLE_CLIENT_ID', 'Google client id'),
      scopes: readOptionalClientValue('VITE_GOOGLE_GMAIL_SCOPES')
        || 'openid email profile https://www.googleapis.com/auth/gmail.readonly'
    };
  }

  if (providerKey === 'quickbooks-online') {
    return {
      clientId: requireClientValue('VITE_QUICKBOOKS_CLIENT_ID', 'QuickBooks client id'),
      scopes: readOptionalClientValue('VITE_QUICKBOOKS_SCOPES')
        || 'com.intuit.quickbooks.accounting'
    };
  }

  if (providerKey === 'xero') {
    return {
      clientId: requireClientValue('VITE_XERO_CLIENT_ID', 'Xero client id'),
      scopes: readOptionalClientValue('VITE_XERO_SCOPES')
        || 'openid profile email accounting.transactions accounting.settings offline_access'
    };
  }

  const defaultOutlookScopes = providerKey === 'microsoft-outlook-mail'
    ? 'offline_access Mail.Read User.Read'
    : 'offline_access Calendars.Read User.Read';

  return {
    clientId: requireClientValue('VITE_OUTLOOK_CLIENT_ID', 'Outlook client id'),
    scopes: readOptionalClientValue('VITE_OUTLOOK_SCOPES')
      || defaultOutlookScopes,
    tenantId: readOptionalClientValue('VITE_OUTLOOK_TENANT_ID') || 'common'
  };
};

const requireClientValue = (key: string, label: string): string => {
  const value = readOptionalClientValue(key);
  if (!value) {
    throw new Error(`${label} is not configured. Set ${key}.`);
  }
  return value;
};

const readOptionalClientValue = (key: string): string | null => {
  const env = (import.meta as ImportMeta & { env?: Record<string, string | undefined> }).env;
  const value = env?.[key];
  if (!value) return null;
  const trimmed = value.trim();
  return trimmed.length > 0 ? trimmed : null;
};

const createStateToken = (): string => {
  if (typeof crypto !== 'undefined' && typeof crypto.getRandomValues === 'function') {
    const bytes = new Uint8Array(18);
    crypto.getRandomValues(bytes);
    return Array.from(bytes).map(b => b.toString(16).padStart(2, '0')).join('');
  }

  return `${Date.now().toString(16)}${Math.random().toString(16).slice(2)}`;
};

const rememberPendingState = (state: string, payload: IntegrationOAuthPendingState): void => {
  const allStates = readPendingStates();
  allStates[state] = payload;
  writePendingStates(allStates);
};

const readPendingStates = (): IntegrationOAuthPendingMap => {
  if (!hasWindow()) return {};
  const raw = sessionStorage.getItem(INTEGRATION_OAUTH_STATE_STORAGE_KEY);
  if (!raw) return {};

  try {
    const parsed = JSON.parse(raw) as IntegrationOAuthPendingMap;
    if (!parsed || typeof parsed !== 'object') {
      return {};
    }

    return parsed;
  } catch {
    return {};
  }
};

const writePendingStates = (states: IntegrationOAuthPendingMap): void => {
  if (!hasWindow()) return;
  const keys = Object.keys(states);
  if (keys.length === 0) {
    sessionStorage.removeItem(INTEGRATION_OAUTH_STATE_STORAGE_KEY);
    return;
  }

  sessionStorage.setItem(INTEGRATION_OAUTH_STATE_STORAGE_KEY, JSON.stringify(states));
};
