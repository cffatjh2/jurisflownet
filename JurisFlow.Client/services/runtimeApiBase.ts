const rawConfiguredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim() || '';
const configuredApiBaseUrl = rawConfiguredApiBaseUrl.replace(/\/+$/, '');

const shouldRewritePath = (pathname: string) =>
  pathname.startsWith('/api') || pathname.startsWith('/uploads');

const rewriteRequestUrl = (input: string) => {
  if (!configuredApiBaseUrl || typeof window === 'undefined') {
    return input;
  }

  try {
    const resolvedUrl = new URL(input, window.location.origin);
    if (resolvedUrl.origin !== window.location.origin || !shouldRewritePath(resolvedUrl.pathname)) {
      return input;
    }

    return `${configuredApiBaseUrl}${resolvedUrl.pathname}${resolvedUrl.search}${resolvedUrl.hash}`;
  } catch {
    return input;
  }
};

let isInstalled = false;

export const installApiFetchRewriter = () => {
  if (isInstalled || !configuredApiBaseUrl || typeof window === 'undefined') {
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
