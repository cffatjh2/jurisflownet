import React, { Suspense, useEffect } from 'react';
import Login from './components/Login';
import { ToastProvider } from './components/Toast';
import { ConfirmProvider } from './components/ConfirmDialog';
import { ClientAuthProvider, useClientAuth } from './contexts/ClientAuthContext';
import { LanguageProvider } from './contexts/LanguageContext';
import { AuthProvider, useAuth } from './contexts/AuthContext';
import { ThemeProvider } from './contexts/ThemeContext';

const GoogleAuthCallback = React.lazy(() => import('./components/GoogleAuthCallback'));
const MicrosoftAuthCallback = React.lazy(() => import('./components/MicrosoftAuthCallback'));
const ZoomAuthCallback = React.lazy(() => import('./components/ZoomAuthCallback'));
const IntegrationOAuthCallback = React.lazy(() => import('./components/IntegrationOAuthCallback'));
const ForgotPassword = React.lazy(() => import('./components/ForgotPassword'));
const ResetPassword = React.lazy(() => import('./components/ResetPassword'));
const ClientPortal = React.lazy(() => import('./components/client/ClientPortal'));
const AuthenticatedShell = React.lazy(() => import('./components/AuthenticatedShell'));

const AppFallback = () => (
  <div className="flex items-center justify-center min-h-screen bg-gray-50">
    <div className="text-gray-400">Loading...</div>
  </div>
);

const renderLazy = (Component: React.ComponentType<any>, props: Record<string, unknown> = {}) => (
  <Suspense fallback={<AppFallback />}>
    <Component {...props} />
  </Suspense>
);

const AppContent = () => {
  const { isAuthenticated: isClientAuthenticated, loading: isClientLoading } = useClientAuth();
  const { isAuthenticated } = useAuth();

  useEffect(() => {
    const urlParams = new URLSearchParams(window.location.search);
    if (urlParams.get('code') || urlParams.get('error')) {
      if (window.location.pathname.includes('/auth/google/callback')) {
        return;
      }
      if (window.location.pathname.includes('/auth/microsoft/callback')) {
        return;
      }
      if (window.location.pathname.includes('/auth/zoom/callback')) {
        return;
      }
      if (window.location.pathname.includes('/auth/integrations/') && window.location.pathname.includes('/callback')) {
        return;
      }
    }
  }, []);

  const urlParams = new URLSearchParams(window.location.search);
  if ((urlParams.get('code') || urlParams.get('error')) && window.location.pathname.includes('/auth/google/callback')) {
    return renderLazy(GoogleAuthCallback);
  }
  if ((urlParams.get('code') || urlParams.get('error')) && window.location.pathname.includes('/auth/microsoft/callback')) {
    return renderLazy(MicrosoftAuthCallback);
  }
  if ((urlParams.get('code') || urlParams.get('error')) && window.location.pathname.includes('/auth/zoom/callback')) {
    return renderLazy(ZoomAuthCallback);
  }
  if ((urlParams.get('code') || urlParams.get('error')) && window.location.pathname.includes('/auth/integrations/') && window.location.pathname.includes('/callback')) {
    return renderLazy(IntegrationOAuthCallback);
  }

  const isClientPortal =
    window.location.pathname === '/client' ||
    window.location.pathname.startsWith('/client/') ||
    window.location.hash === '#/client';

  if (isClientPortal) {
    if (isClientLoading) {
      return <AppFallback />;
    }
    return isClientAuthenticated ? renderLazy(ClientPortal) : <Login initialUserType="client" />;
  }

  if (window.location.pathname === '/forgot-password') {
    return renderLazy(ForgotPassword);
  }

  if (window.location.pathname === '/reset-password') {
    return renderLazy(ResetPassword);
  }

  if (isAuthenticated) {
    return (
      <Suspense fallback={<AppFallback />}>
        <AuthenticatedShell />
      </Suspense>
    );
  }

  return <Login />;
};

const App = () => {
  try {
    return (
      <ThemeProvider>
        <LanguageProvider>
          <AuthProvider>
            <ClientAuthProvider>
              <ToastProvider>
                <ConfirmProvider>
                  <AppContent />
                </ConfirmProvider>
              </ToastProvider>
            </ClientAuthProvider>
          </AuthProvider>
        </LanguageProvider>
      </ThemeProvider>
    );
  } catch (error) {
    throw error;
  }
};

export default App;
