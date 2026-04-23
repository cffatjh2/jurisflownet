import React, { useEffect, useRef } from 'react';
import { toast } from './Toast';
import { api } from '../services/api';
import {
  consumeEmailAccountOAuth,
  inferEmailAccountOAuthProviderFromPath
} from '../services/emailAccountOAuthService';

const EmailAccountOAuthCallback: React.FC = () => {
  const handledRef = useRef(false);

  useEffect(() => {
    if (handledRef.current) return;
    handledRef.current = true;

    const params = new URLSearchParams(window.location.search);
    const code = params.get('code');
    const state = params.get('state');
    const error = params.get('error');
    const provider = inferEmailAccountOAuthProviderFromPath(window.location.pathname);

    const run = async () => {
      if (!provider) {
        toast.error('Unknown mailbox OAuth callback.');
        window.location.href = '/#communications';
        return;
      }

      const validation = consumeEmailAccountOAuth(provider, state, '/#communications');

      if (error) {
        toast.error('Mailbox connection failed. Please try again.');
        window.location.href = validation.returnPath;
        return;
      }

      if (!validation.valid || !validation.pending) {
        toast.error(validation.message || 'Mailbox OAuth state is invalid.');
        window.location.href = validation.returnPath;
        return;
      }

      if (!code) {
        toast.error('Authorization code is missing.');
        window.location.href = validation.returnPath;
        return;
      }

      try {
        if (provider === 'gmail') {
          await api.emails.accounts.connectGmail({
            authorizationCode: code,
            state: validation.pending.state,
            codeVerifier: validation.pending.codeVerifier,
            redirectUri: validation.pending.redirectUri
          });
        } else {
          await api.emails.accounts.connectOutlook({
            authorizationCode: code,
            state: validation.pending.state,
            codeVerifier: validation.pending.codeVerifier,
            redirectUri: validation.pending.redirectUri
          });
        }

        toast.success(provider === 'gmail' ? 'Gmail mailbox connected.' : 'Outlook mailbox connected.');
        window.location.href = validation.returnPath;
      } catch (err) {
        console.error('Failed to finalize mailbox OAuth', err);
        toast.error(err instanceof Error ? err.message : 'Failed to connect mailbox.');
        window.location.href = validation.returnPath;
      }
    };

    void run();
  }, []);

  return (
    <div className="flex items-center justify-center h-screen">
      <div className="text-center">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-slate-700 mx-auto mb-4"></div>
        <p className="text-gray-600">Connecting mailbox...</p>
      </div>
    </div>
  );
};

export default EmailAccountOAuthCallback;
