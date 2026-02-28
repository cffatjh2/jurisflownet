import React, { useEffect, useRef } from 'react';
import { toast } from './Toast';
import { api } from '../services/api';
import {
  consumeIntegrationOAuthState,
  parseIntegrationOAuthProviderFromPath
} from '../services/integrationOAuthService';

const PROVIDER_LABELS: Record<string, string> = {
  'google-gmail': 'Google Gmail',
  'quickbooks-online': 'QuickBooks Online',
  'xero': 'Xero',
  'microsoft-outlook-calendar': 'Microsoft Outlook Calendar',
  'microsoft-outlook-mail': 'Microsoft Outlook Mail'
};

const DEFAULT_RETURN_PATH = '/#settings-integrations';

const IntegrationOAuthCallback: React.FC = () => {
  const handledRef = useRef(false);

  useEffect(() => {
    if (handledRef.current) return;
    handledRef.current = true;

    const searchParams = new URLSearchParams(window.location.search);
    const providerKey = parseIntegrationOAuthProviderFromPath(window.location.pathname);
    const code = searchParams.get('code');
    const state = searchParams.get('state');
    const error = searchParams.get('error');
    const realmId = searchParams.get('realmId') || searchParams.get('realmID');
    const tenantId = searchParams.get('tenant') || searchParams.get('tenantId');

    const redirectTo = (path: string) => {
      window.location.assign(path || DEFAULT_RETURN_PATH);
    };

    const run = async () => {
      if (!providerKey) {
        toast.error('Unsupported integration callback route.');
        redirectTo(DEFAULT_RETURN_PATH);
        return;
      }

      const providerLabel = PROVIDER_LABELS[providerKey] || 'Integration';
      const stateValidation = consumeIntegrationOAuthState(providerKey, state, DEFAULT_RETURN_PATH);
      if (!stateValidation.valid) {
        toast.error(stateValidation.message || 'OAuth state validation failed.');
        redirectTo(stateValidation.returnPath);
        return;
      }

      if (error) {
        toast.error(`${providerLabel} authorization was cancelled or denied.`);
        redirectTo(stateValidation.returnPath);
        return;
      }

      if (!code) {
        toast.error(`${providerLabel} authorization code was not received.`);
        redirectTo(stateValidation.returnPath);
        return;
      }

      try {
        const redirectUri = `${window.location.origin}${window.location.pathname}`;
        await api.settings.connectIntegration(providerKey, {
          authorizationCode: code,
          redirectUri,
          realmId: realmId || undefined,
          tenantId: tenantId || undefined,
          syncEnabled: true
        });

        toast.success(`${providerLabel} connected successfully.`);
      } catch (error: any) {
        const message = error?.message || `${providerLabel} connection failed.`;
        toast.error(message);
      } finally {
        redirectTo(stateValidation.returnPath);
      }
    };

    void run();
  }, []);

  return (
    <div className="flex items-center justify-center h-screen">
      <div className="text-center">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-slate-700 mx-auto mb-4" />
        <p className="text-gray-600">Completing integration authorization...</p>
      </div>
    </div>
  );
};

export default IntegrationOAuthCallback;
