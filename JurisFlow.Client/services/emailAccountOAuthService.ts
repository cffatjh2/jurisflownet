import { api } from './api';

export type EmailAccountOAuthProvider = 'gmail' | 'outlook';

type PendingEmailAccountOAuth = {
  provider: EmailAccountOAuthProvider;
  state: string;
  codeVerifier: string;
  redirectUri: string;
  returnPath: string;
  issuedAtUnixMs: number;
};

type PendingEmailAccountOAuthMap = Record<string, PendingEmailAccountOAuth>;

export type EmailAccountOAuthValidationResult = {
  valid: boolean;
  returnPath: string;
  pending?: PendingEmailAccountOAuth;
  message?: string;
};

const STORAGE_KEY = 'jf.email-account.oauth.state.v1';
const MAX_AGE_MS = 10 * 60 * 1000;
const DEFAULT_RETURN_PATH = '/#communications';

const hasWindow = (): boolean => typeof window !== 'undefined';

const normalizeReturnPath = (value: string | null | undefined, fallback: string = DEFAULT_RETURN_PATH): string => {
  if (!value) return fallback;
  const trimmed = value.trim();
  if (!trimmed.startsWith('/') || trimmed.startsWith('//')) {
    return fallback;
  }

  return trimmed;
};

const readPendingStates = (): PendingEmailAccountOAuthMap => {
  if (!hasWindow()) return {};

  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw) as PendingEmailAccountOAuthMap;
    return parsed && typeof parsed === 'object' ? parsed : {};
  } catch {
    return {};
  }
};

const writePendingStates = (value: PendingEmailAccountOAuthMap): void => {
  if (!hasWindow()) return;
  sessionStorage.setItem(STORAGE_KEY, JSON.stringify(value));
};

const rememberPendingState = (pending: PendingEmailAccountOAuth): void => {
  const states = readPendingStates();
  states[pending.state] = pending;
  writePendingStates(states);
};

export const inferEmailAccountOAuthProviderFromPath = (pathname: string): EmailAccountOAuthProvider | null => {
  if (pathname.includes('/auth/google/callback')) return 'gmail';
  if (pathname.includes('/auth/outlook/callback')) return 'outlook';
  return null;
};

export const hasPendingEmailAccountOAuth = (state: string | null | undefined): boolean => {
  if (!state) return false;
  return !!readPendingStates()[state];
};

export const startEmailAccountOAuth = async (
  provider: EmailAccountOAuthProvider,
  returnPath: string = DEFAULT_RETURN_PATH
): Promise<void> => {
  if (!hasWindow()) {
    throw new Error('OAuth can only be started in browser context.');
  }

  const authorization = provider === 'gmail'
    ? await api.emails.accounts.getGmailAuthorization()
    : await api.emails.accounts.getOutlookAuthorization();

  if (!authorization?.authorizationUrl || !authorization?.state || !authorization?.codeVerifier || !authorization?.redirectUri) {
    throw new Error('Unable to initialize mailbox OAuth.');
  }

  rememberPendingState({
    provider,
    state: authorization.state,
    codeVerifier: authorization.codeVerifier,
    redirectUri: authorization.redirectUri,
    returnPath: normalizeReturnPath(returnPath),
    issuedAtUnixMs: Date.now()
  });

  window.location.assign(authorization.authorizationUrl);
};

export const consumeEmailAccountOAuth = (
  provider: EmailAccountOAuthProvider,
  state: string | null | undefined,
  fallbackReturnPath: string = DEFAULT_RETURN_PATH
): EmailAccountOAuthValidationResult => {
  const normalizedFallback = normalizeReturnPath(fallbackReturnPath);
  if (!state) {
    return {
      valid: false,
      returnPath: normalizedFallback,
      message: 'OAuth state is missing.'
    };
  }

  const states = readPendingStates();
  const pending = states[state];
  delete states[state];
  writePendingStates(states);

  if (!pending) {
    return {
      valid: false,
      returnPath: normalizedFallback,
      message: 'OAuth state is invalid or already consumed.'
    };
  }

  if (pending.provider !== provider) {
    return {
      valid: false,
      returnPath: normalizeReturnPath(pending.returnPath, normalizedFallback),
      message: 'OAuth provider does not match pending state.'
    };
  }

  const ageMs = Date.now() - pending.issuedAtUnixMs;
  if (ageMs < 0 || ageMs > MAX_AGE_MS) {
    return {
      valid: false,
      returnPath: normalizeReturnPath(pending.returnPath, normalizedFallback),
      message: 'OAuth state expired. Start the connection again.'
    };
  }

  return {
    valid: true,
    returnPath: normalizeReturnPath(pending.returnPath, normalizedFallback),
    pending
  };
};
