import React, { createContext, useContext, useState, ReactNode, useEffect } from 'react';

export interface Client {
  id: string;
  name: string;
  email: string;
  phone?: string;
  mobile?: string;
  company?: string;
  type: 'Individual' | 'Corporate';
  status: 'Active' | 'Inactive';
}

interface ClientLoginResult {
  success: boolean;
  error?: string;
}

interface ClientAuthContextType {
  client: Client | null;
  isAuthenticated: boolean;
  login: (email: string, password: string, tenantSlug?: string) => Promise<ClientLoginResult>;
  logout: () => void;
  loading: boolean;
}

const ClientAuthContext = createContext<ClientAuthContextType | undefined>(undefined);

export const ClientAuthProvider = ({ children }: { children: ReactNode }) => {
  const [client, setClient] = useState<Client | null>(null);
  const [loading, setLoading] = useState(true);
  const [sessionExpiresAt, setSessionExpiresAt] = useState<string | null>(null);

  const login = async (email: string, password: string, tenantSlug?: string): Promise<ClientLoginResult> => {
    try {
      const normalizedTenant = tenantSlug?.trim().toLowerCase();
      if (typeof window !== 'undefined' && normalizedTenant) {
        localStorage.setItem('tenant_slug', normalizedTenant);
      }
      const tenantHeader = normalizedTenant || (typeof window !== 'undefined' ? localStorage.getItem('tenant_slug') : null);
      const res = await fetch('/api/client/login', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(tenantHeader ? { 'X-Tenant-Slug': tenantHeader } : {})
        },
        body: JSON.stringify({ email, password })
      });
      if (!res.ok) {
        let message = 'Client login failed';
        try {
          const payload = await res.json();
          if (typeof payload?.message === 'string' && payload.message.trim()) {
            message = payload.message.trim();
          }
        } catch {
          if (res.status === 401) {
            message = 'Invalid email or password';
          }
        }

        return {
          success: false,
          error: message
        };
      }

      const data = await res.json();
      setClient(data.client);
      if (typeof window !== 'undefined') {
        localStorage.setItem('client_token', data.token);
        localStorage.setItem('client_user', JSON.stringify(data.client));
        if (data.refreshToken) {
          localStorage.setItem('client_refresh_token', data.refreshToken);
        }
        if (data.refreshTokenExpiresAt) {
          localStorage.setItem('client_refresh_expires_at', data.refreshTokenExpiresAt);
        }
        if (data.session?.id && data.session?.expiresAt) {
          localStorage.setItem('client_session_id', data.session.id);
          localStorage.setItem('client_session_expires_at', data.session.expiresAt);
          setSessionExpiresAt(data.session.expiresAt);
        }
      }
      return { success: true };
    } catch (e) {
      console.error('Client login failed', e);
      return {
        success: false,
        error: e instanceof Error && e.message ? e.message : 'Client login failed'
      };
    }
  };

  const logout = () => {
    setClient(null);
    if (typeof window !== 'undefined') {
      localStorage.removeItem('client_token');
      localStorage.removeItem('client_user');
      localStorage.removeItem('client_session_id');
      localStorage.removeItem('client_session_expires_at');
      localStorage.removeItem('client_refresh_token');
      localStorage.removeItem('client_refresh_expires_at');
      setSessionExpiresAt(null);
    }
  };

  useEffect(() => {
    if (typeof window !== 'undefined') {
      const token = localStorage.getItem('client_token');
      const storedClient = localStorage.getItem('client_user');
      if (token && storedClient) {
        try {
          setClient(JSON.parse(storedClient));
          const storedSessionExpiresAt = localStorage.getItem('client_session_expires_at');
          if (storedSessionExpiresAt) {
            setSessionExpiresAt(storedSessionExpiresAt);
          }
        } catch {
          logout();
        }
      }
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (!sessionExpiresAt) return;
    const expiresAt = new Date(sessionExpiresAt).getTime();
    if (Number.isNaN(expiresAt)) return;
    const delay = Math.max(0, expiresAt - Date.now());
    if (delay === 0) {
      logout();
      return;
    }
    const timer = window.setTimeout(() => logout(), delay);
    return () => window.clearTimeout(timer);
  }, [sessionExpiresAt]);

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const handleUnauthorized = () => {
      logout();
    };
    window.addEventListener('client:unauthorized', handleUnauthorized as EventListener);
    return () => window.removeEventListener('client:unauthorized', handleUnauthorized as EventListener);
  }, []);

  return (
    <ClientAuthContext.Provider value={{ client, isAuthenticated: !!client, login, logout, loading }}>
      {children}
    </ClientAuthContext.Provider>
  );
};

export const useClientAuth = () => {
  const context = useContext(ClientAuthContext);
  if (!context) {
    throw new Error('useClientAuth must be used within a ClientAuthProvider');
  }
  return context;
};

