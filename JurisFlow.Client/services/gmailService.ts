// Gmail API Service
// Note: This requires OAuth2 setup in Google Cloud Console
// 1. Create a project in Google Cloud Console
// 2. Enable Gmail API
// 3. Create OAuth2 credentials
// 4. Add redirect URI: http://localhost:3000/auth/google/callback

import { getGoogleClientId } from './googleConfig';
import { buildOAuthAuthHeaders, requestOAuthState, type OAuthTarget } from './oauthSecurity';
const GMAIL_API_BASE = 'https://gmail.googleapis.com/gmail/v1';

export interface GmailMessage {
  id: string;
  threadId: string;
  snippet: string;
  payload: {
    headers: Array<{ name: string; value: string }>;
    parts?: Array<{ mimeType: string; body: { data: string } }>;
    body?: { data: string };
  };
}

interface GmailAuthUrlOptions {
  target?: Extract<OAuthTarget, 'gmail' | 'google-docs'>;
  returnPath?: string;
}

const normalizeReturnPath = (value: string | undefined, fallback: string): string => {
  if (!value) return fallback;
  const trimmed = value.trim();
  if (!trimmed.startsWith('/') || trimmed.startsWith('//')) return fallback;
  return trimmed;
};

export const gmailService = {
  // Get OAuth2 authorization URL
  getAuthUrl: async (options: GmailAuthUrlOptions = {}): Promise<string> => {
    const clientId = getGoogleClientId();

    if (!clientId) {
      return '';
    }

    const target = options.target ?? 'gmail';
    const fallbackPath = target === 'google-docs' ? '/#documents' : '/#communications';
    const returnPath = normalizeReturnPath(options.returnPath, fallbackPath);
    const redirectUri = `${window.location.origin}/auth/google/callback`;
    const scope = 'https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.send';
    const responseType = 'code';
    const state = await requestOAuthState('google', target, returnPath);

    return `https://accounts.google.com/o/oauth2/v2/auth?client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&response_type=${responseType}&scope=${encodeURIComponent(scope)}&access_type=offline&prompt=consent&state=${encodeURIComponent(state)}`;
  },

  // Exchange authorization code for tokens (server-side)
  exchangeCodeForTokens: async (code: string, state: string): Promise<{ accessToken: string; refreshToken: string }> => {
    const response = await fetch('/api/google/oauth', {
      method: 'POST',
      headers: buildOAuthAuthHeaders(true),
      body: JSON.stringify({ code, state })
    });
    return response.json();
  },

  // Get messages list
  getMessages: async (accessToken: string, maxResults: number = 50): Promise<GmailMessage[]> => {
    const response = await fetch(`${GMAIL_API_BASE}/users/me/messages?maxResults=${maxResults}`, {
      headers: {
        'Authorization': `Bearer ${accessToken}`
      }
    });
    const data = await response.json();

    if (!data.messages) return [];

    // Fetch full message details
    const messagePromises = data.messages.map((msg: { id: string }) =>
      fetch(`${GMAIL_API_BASE}/users/me/messages/${msg.id}`, {
        headers: { 'Authorization': `Bearer ${accessToken}` }
      }).then(r => r.json())
    );

    return Promise.all(messagePromises);
  },

  // Send email
  sendEmail: async (accessToken: string, to: string, subject: string, body: string): Promise<void> => {
    const email = [
      `To: ${to}`,
      `Subject: ${subject}`,
      'Content-Type: text/html; charset=utf-8',
      '',
      body
    ].join('\n');

    const encodedEmail = btoa(email).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

    await fetch(`${GMAIL_API_BASE}/users/me/messages/send`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${accessToken}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        raw: encodedEmail
      })
    });
  },

  // Parse Gmail message to our Message format
  parseMessage: (gmailMsg: GmailMessage): { from: string; subject: string; preview: string; date: string } => {
    const headers = gmailMsg.payload.headers;
    const from = headers.find(h => h.name === 'From')?.value || 'Unknown';
    const subject = headers.find(h => h.name === 'Subject')?.value || '(No Subject)';
    const date = headers.find(h => h.name === 'Date')?.value || new Date().toISOString();

    return {
      from: from.replace(/<.*>/, '').trim(),
      subject,
      preview: gmailMsg.snippet,
      date: new Date(date).toLocaleString()
    };
  }
};

