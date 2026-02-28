const CLIENT_API_URL = typeof window !== 'undefined' ? '/api/client' : 'http://localhost:3001/api/client';
const TENANT_STORAGE_KEY = 'tenant_slug';

const getTenantSlug = () => {
    if (typeof window === 'undefined') return null;
    return localStorage.getItem(TENANT_STORAGE_KEY);
};

const refreshClientToken = async () => {
    if (typeof window === 'undefined') return false;
    const refreshToken = localStorage.getItem('client_refresh_token');
    const sessionId = localStorage.getItem('client_session_id');
    if (!refreshToken || !sessionId) return false;

    try {
        const tenantSlug = getTenantSlug();
        const res = await fetch(`${CLIENT_API_URL}/refresh`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(tenantSlug ? { 'X-Tenant-Slug': tenantSlug } : {})
            },
            body: JSON.stringify({ sessionId, refreshToken })
        });
        if (!res.ok) return false;
        const data = await res.json();
        if (data?.token) {
            localStorage.setItem('client_token', data.token);
        }
        if (data?.refreshToken) {
            localStorage.setItem('client_refresh_token', data.refreshToken);
        }
        if (data?.refreshTokenExpiresAt) {
            localStorage.setItem('client_refresh_expires_at', data.refreshTokenExpiresAt);
        }
        if (data?.session?.id && data?.session?.expiresAt) {
            localStorage.setItem('client_session_id', data.session.id);
            localStorage.setItem('client_session_expires_at', data.session.expiresAt);
        }
        if (data?.client) {
            localStorage.setItem('client_user', JSON.stringify(data.client));
        }
        return true;
    } catch {
        return false;
    }
};

const buildHeaders = (options?: RequestInit) => {
    const headers = new Headers(options?.headers || {});
    const token = typeof window !== 'undefined' ? localStorage.getItem('client_token') : null;
    const tenantSlug = getTenantSlug();
    if (token) {
        headers.set('Authorization', `Bearer ${token}`);
    }
    if (tenantSlug) {
        headers.set('X-Tenant-Slug', tenantSlug);
    }
    if (!(options?.body instanceof FormData) && !headers.has('Content-Type')) {
        headers.set('Content-Type', 'application/json');
    }
    return headers;
};

const clientFetch = async (endpoint: string, options: RequestInit = {}, allowRefresh = true): Promise<Response> => {
    const res = await fetch(`${CLIENT_API_URL}${endpoint}`, {
        ...options,
        headers: buildHeaders(options)
    });

    if (res.status === 401) {
        if (allowRefresh) {
            const refreshed = await refreshClientToken();
            if (refreshed) {
                return clientFetch(endpoint, options, false);
            }
        }
        if (typeof window !== 'undefined') {
            window.dispatchEvent(new CustomEvent('client:unauthorized', { detail: { endpoint } }));
        }
    }

    return res;
};

const clientFetchJson = async (endpoint: string, options: RequestInit = {}, allowRefresh = true) => {
    const res = await clientFetch(endpoint, options, allowRefresh);
    if (!res.ok) {
        throw new Error(`API Error: ${res.statusText}`);
    }
    if (res.status === 204) return null;
    return res.json();
};

export const clientApi = {
    fetch: clientFetch,
    fetchJson: clientFetchJson
};
