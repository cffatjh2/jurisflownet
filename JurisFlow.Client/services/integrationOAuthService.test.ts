import { describe, expect, it } from 'vitest';
import {
  buildIntegrationOAuthAuthorizeUrl,
  getIntegrationOAuthCallbackPath,
  normalizeReturnPath,
  parseIntegrationOAuthProviderFromPath
} from './integrationOAuthService';

describe('integrationOAuthService', () => {
  it('resolves callback paths per provider', () => {
    expect(getIntegrationOAuthCallbackPath('quickbooks-online')).toBe('/auth/integrations/quickbooks/callback');
    expect(getIntegrationOAuthCallbackPath('xero')).toBe('/auth/integrations/xero/callback');
    expect(getIntegrationOAuthCallbackPath('microsoft-outlook-calendar')).toBe('/auth/integrations/outlook/callback');
  });

  it('parses provider key from callback path', () => {
    expect(parseIntegrationOAuthProviderFromPath('/auth/integrations/quickbooks/callback')).toBe('quickbooks-online');
    expect(parseIntegrationOAuthProviderFromPath('/auth/integrations/xero/callback')).toBe('xero');
    expect(parseIntegrationOAuthProviderFromPath('/auth/integrations/outlook/callback')).toBe('microsoft-outlook-calendar');
    expect(parseIntegrationOAuthProviderFromPath('/auth/integrations/unknown/callback')).toBeNull();
  });

  it('normalizes return paths', () => {
    expect(normalizeReturnPath('/#settings-integrations')).toBe('/#settings-integrations');
    expect(normalizeReturnPath(' //evil.com')).toBe('/#settings-integrations');
    expect(normalizeReturnPath('http://example.com')).toBe('/#settings-integrations');
  });

  it('builds quickbooks authorization url', () => {
    const url = buildIntegrationOAuthAuthorizeUrl(
      'quickbooks-online',
      {
        clientId: 'qb-client',
        scopes: 'com.intuit.quickbooks.accounting'
      },
      'https://app.example.com/auth/integrations/quickbooks/callback',
      'state-123'
    );

    const parsed = new URL(url);
    expect(parsed.origin).toBe('https://appcenter.intuit.com');
    expect(parsed.pathname).toBe('/connect/oauth2');
    expect(parsed.searchParams.get('client_id')).toBe('qb-client');
    expect(parsed.searchParams.get('state')).toBe('state-123');
  });

  it('builds xero authorization url', () => {
    const url = buildIntegrationOAuthAuthorizeUrl(
      'xero',
      {
        clientId: 'xero-client',
        scopes: 'openid profile email'
      },
      'https://app.example.com/auth/integrations/xero/callback',
      'state-456'
    );

    const parsed = new URL(url);
    expect(parsed.origin).toBe('https://login.xero.com');
    expect(parsed.pathname).toBe('/identity/connect/authorize');
    expect(parsed.searchParams.get('client_id')).toBe('xero-client');
    expect(parsed.searchParams.get('state')).toBe('state-456');
  });

  it('builds outlook authorization url', () => {
    const url = buildIntegrationOAuthAuthorizeUrl(
      'microsoft-outlook-calendar',
      {
        clientId: 'outlook-client',
        scopes: 'offline_access Calendars.Read',
        tenantId: 'common'
      },
      'https://app.example.com/auth/integrations/outlook/callback',
      'state-789'
    );

    const parsed = new URL(url);
    expect(parsed.origin).toBe('https://login.microsoftonline.com');
    expect(parsed.pathname).toContain('/oauth2/v2.0/authorize');
    expect(parsed.searchParams.get('client_id')).toBe('outlook-client');
    expect(parsed.searchParams.get('state')).toBe('state-789');
  });
});
