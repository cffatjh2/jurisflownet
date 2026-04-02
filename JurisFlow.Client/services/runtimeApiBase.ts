const rawConfiguredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim() || '';
const configuredApiBaseUrl = rawConfiguredApiBaseUrl.replace(/\/+$/, '');

const shouldRewritePath = (pathname: string) =>
  pathname.startsWith('/api') || pathname.startsWith('/uploads');

const normalizeOrigin = (value: string) => {
  try {
    return new URL(value).origin.replace(/\/+$/, '');
  } catch {
    return '';
  }
};

const shouldUseConfiguredApiBase = () => {
  if (!configuredApiBaseUrl || typeof window === 'undefined') {
    return false;
  }

  const targetOrigin = normalizeOrigin(configuredApiBaseUrl);
  if (!targetOrigin) {
    return false;
  }

  const currentOrigin = normalizeOrigin(window.location.origin);
  if (currentOrigin && currentOrigin !== targetOrigin) {
    console.info(`Using configured API base ${targetOrigin} from ${currentOrigin}.`);
  }

  return true;
};

const activeApiBaseUrl = shouldUseConfiguredApiBase() ? configuredApiBaseUrl : '';

const rewriteRequestUrl = (input: string) => {
  if (!activeApiBaseUrl || typeof window === 'undefined') {
    return input;
  }

  try {
    const resolvedUrl = new URL(input, window.location.origin);
    if (resolvedUrl.origin !== window.location.origin || !shouldRewritePath(resolvedUrl.pathname)) {
      return input;
    }

    return `${activeApiBaseUrl}${resolvedUrl.pathname}${resolvedUrl.search}${resolvedUrl.hash}`;
  } catch {
    return input;
  }
};

let isInstalled = false;

export const installApiFetchRewriter = () => {
  if (isInstalled || !activeApiBaseUrl || typeof window === 'undefined') {
    return;
  }

  const originalFetch = window.fetch.bind(window);

  window.fetch = ((input: RequestInfo | URL, init?: RequestInit) => {
    if (typeof input === 'string') {
      return originalFetch(rewriteRequestUrl(input), init);
    }

    if (input instanceof URL) {
      return originalFetch(rewriteRequestUrl(input.toString()), init);
    }

    const rewrittenUrl = rewriteRequestUrl(input.url);
    if (rewrittenUrl === input.url) {
      return originalFetch(input, init);
    }

    return originalFetch(new Request(rewrittenUrl, input), init);
  }) as typeof fetch;

  isInstalled = true;
};
