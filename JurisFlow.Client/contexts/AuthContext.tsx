import React, { createContext, useContext, useState, ReactNode } from 'react';
import { Permission, ROLE_PERMISSIONS, EmployeeRole } from '../types';

export interface User {
  id: string;
  name: string;
  email: string;
  role: string;
  initials: string;
}

interface AuthContextType {
  user: User | null;
  isAuthenticated: boolean;
  login: (email: string, password: string, tenantSlug?: string) => Promise<LoginResult>;
  verifyMfa: (challengeId: string, code: string) => Promise<LoginResult>;
  logout: () => void;
  can: (permission: Permission) => boolean;
}

interface LoginResult {
  success: boolean;
  mfaRequired?: boolean;
  challengeId?: string;
  challengeExpiresAt?: string;
  error?: string;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);
const ROLE_ALIASES: Record<string, EmployeeRole> = {
  Manager: EmployeeRole.OFFICE_MANAGER,
  Staff: EmployeeRole.PARALEGAL,
  Attorney: EmployeeRole.ASSOCIATE
};

// Mock Users Database
const MOCK_USERS: Record<string, User> = {
  'harvey@lexos.com': { id: 'u1', name: 'Harvey Specter', email: 'harvey@lexos.com', role: 'Partner', initials: 'HS' },
  'mike@lexos.com': { id: 'u2', name: 'Mike Ross', email: 'mike@lexos.com', role: 'Associate', initials: 'MR' },
  'jessica@lexos.com': { id: 'u3', name: 'Jessica Pearson', email: 'jessica@lexos.com', role: 'Partner', initials: 'JP' },
  'louis@lexos.com': { id: 'u4', name: 'Louis Litt', email: 'louis@lexos.com', role: 'Partner', initials: 'LL' },
};

export const AuthProvider = ({ children }: { children: ReactNode }) => {
  const [user, setUser] = useState<User | null>(null);
  const [sessionExpiresAt, setSessionExpiresAt] = useState<string | null>(null);

  const storeSession = (session?: { id?: string; expiresAt?: string }) => {
    if (typeof window === 'undefined') return;
    if (!session?.id || !session.expiresAt) return;
    localStorage.setItem('auth_session_id', session.id);
    localStorage.setItem('auth_session_expires_at', session.expiresAt);
    setSessionExpiresAt(session.expiresAt);
  };

  const storeRefreshToken = (refreshToken?: string, refreshTokenExpiresAt?: string) => {
    if (typeof window === 'undefined' || !refreshToken) return;
    localStorage.setItem('auth_refresh_token', refreshToken);
    if (refreshTokenExpiresAt) {
      localStorage.setItem('auth_refresh_expires_at', refreshTokenExpiresAt);
    }
  };

  const can = (permission: Permission): boolean => {
    if (!user) return false;

    // Admin always has access
    if (user.role === 'Admin') return true;

    // Check role permissions
    // Note: user.role comes from server as effectiveRole (e.g. 'PARALEGAL' or 'Partner')
    // We need to cast it to EmployeeRole if it matches, or handle string keys
    const normalizedRole = ROLE_ALIASES[user.role] || user.role;
    const permissions = ROLE_PERMISSIONS[normalizedRole as EmployeeRole] || ROLE_PERMISSIONS[user.role as EmployeeRole] || [];

    return permissions.includes(permission);
  };

  const login = async (email: string, password: string, tenantSlug?: string): Promise<LoginResult> => {
    try {
      // Use Vite proxy instead of direct localhost:3001 to avoid CORS issues
      const API_URL = typeof window !== 'undefined' ? '/api' : 'http://localhost:3001/api';
      const normalizedTenant = tenantSlug?.trim().toLowerCase();
      if (typeof window !== 'undefined' && normalizedTenant) {
        localStorage.setItem('tenant_slug', normalizedTenant);
      }
      const tenantHeader = normalizedTenant || (typeof window !== 'undefined' ? localStorage.getItem('tenant_slug') : null);
      const res = await fetch(`${API_URL}/login`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(tenantHeader ? { 'X-Tenant-Slug': tenantHeader } : {})
        },
        body: JSON.stringify({ email, password })
      });
      if (!res.ok) {
        return { success: false, error: 'Login failed' };
      }
      const data = await res.json();
      if (data?.mfaRequired) {
        return {
          success: false,
          mfaRequired: true,
          challengeId: data.challengeId,
          challengeExpiresAt: data.challengeExpiresAt
        };
      }

      setUser(data.user);
      if (typeof window !== 'undefined') {
        localStorage.setItem('auth_token', data.token);
        localStorage.setItem('auth_user', JSON.stringify(data.user));
        storeSession(data.session);
        storeRefreshToken(data.refreshToken, data.refreshTokenExpiresAt);
      }
      return { success: true };
    } catch (e) {
      console.error('Login failed', e);
      return { success: false, error: 'Login failed' };
    }
  };

  const verifyMfa = async (challengeId: string, code: string): Promise<LoginResult> => {
    try {
      const API_URL = typeof window !== 'undefined' ? '/api' : 'http://localhost:3001/api';
      const tenantHeader = typeof window !== 'undefined' ? localStorage.getItem('tenant_slug') : null;
      const res = await fetch(`${API_URL}/mfa/verify`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(tenantHeader ? { 'X-Tenant-Slug': tenantHeader } : {})
        },
        body: JSON.stringify({ challengeId, code })
      });
      if (!res.ok) {
        return { success: false, error: 'Invalid code' };
      }
      const data = await res.json();
      setUser(data.user);
      if (typeof window !== 'undefined') {
        localStorage.setItem('auth_token', data.token);
        localStorage.setItem('auth_user', JSON.stringify(data.user));
        storeSession(data.session);
        storeRefreshToken(data.refreshToken, data.refreshTokenExpiresAt);
      }
      return { success: true };
    } catch (e) {
      console.error('MFA verify failed', e);
      return { success: false, error: 'MFA verify failed' };
    }
  };

  const logout = async () => {
    try {
      if (typeof window !== 'undefined' && localStorage.getItem('auth_token')) {
        const API_URL = '/api';
        await fetch(`${API_URL}/security/sessions/revoke-current`, {
          method: 'POST',
          headers: { Authorization: `Bearer ${localStorage.getItem('auth_token')}` }
        });
      }
    } catch {
      // Ignore logout revoke errors
    }
    setUser(null);
    if (typeof window !== 'undefined') {
      localStorage.removeItem('auth_token');
      localStorage.removeItem('auth_user');
      localStorage.removeItem('auth_session_id');
      localStorage.removeItem('auth_session_expires_at');
      localStorage.removeItem('auth_refresh_token');
      localStorage.removeItem('auth_refresh_expires_at');
      setSessionExpiresAt(null);
    }
  };

  React.useEffect(() => {
    if (typeof window !== 'undefined') {
      const token = localStorage.getItem('auth_token');
      const storedUser = localStorage.getItem('auth_user');
      if (token && storedUser) {
        try {
          setUser(JSON.parse(storedUser));
          const storedSessionExpiresAt = localStorage.getItem('auth_session_expires_at');
          if (storedSessionExpiresAt) {
            setSessionExpiresAt(storedSessionExpiresAt);
          }
        } catch (error) {
          logout();
        }
      }
    }
  }, []);

  React.useEffect(() => {
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

  React.useEffect(() => {
    if (typeof window === 'undefined') return;
    const handleUnauthorized = () => {
      logout();
    };
    window.addEventListener('auth:unauthorized', handleUnauthorized as EventListener);
    return () => window.removeEventListener('auth:unauthorized', handleUnauthorized as EventListener);
  }, []);
  return (
    <AuthContext.Provider value={{ user, isAuthenticated: !!user, login, verifyMfa, logout, can }}>
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
