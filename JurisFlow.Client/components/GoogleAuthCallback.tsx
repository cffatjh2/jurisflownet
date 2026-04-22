import React, { useEffect, useRef } from 'react';
import { toast } from './Toast';
import { buildOAuthAuthHeaders, setOAuthTokens, type OAuthTarget } from '../services/oauthSecurity';

const TARGET_RETURN_PATH: Record<OAuthTarget, string> = {
  'gmail': '/#communications',
  'google-docs': '/#documents',
  'google-meet': '/#videocall',
  'zoom': '/#videocall',
  'microsoft-teams': '/#videocall'
};

const normalizeReturnPath = (value: unknown, fallback: string): string => {
  if (typeof value !== 'string') return fallback;
  const trimmed = value.trim();
  if (!trimmed.startsWith('/') || trimmed.startsWith('//')) return fallback;
  return trimmed;
};

const normalizeGoogleTarget = (value: unknown): OAuthTarget => {
  if (value === 'google-docs' || value === 'google-meet') {
    return value;
  }
  return 'gmail';
};

const getSuccessMessage = (target: OAuthTarget): string => {
  if (target === 'google-docs') return 'Google Docs successfully connected!';
  if (target === 'google-meet') return 'Google Meet successfully connected!';
  return 'Gmail successfully connected!';
};

// This component handles the OAuth callback from Google.
const GoogleAuthCallback: React.FC = () => {
  const handledRef = useRef(false);

  useEffect(() => {
    if (handledRef.current) return;
    handledRef.current = true;

    const urlParams = new URLSearchParams(window.location.search);
    const code = urlParams.get('code');
    const state = urlParams.get('state');
    const error = urlParams.get('error');

    const run = async () => {
      if (error) {
        console.error('OAuth error:', error);
        toast.error('Authentication failed. Please try again.');
        window.location.href = '/';
        return;
      }

      if (!code || !state) {
        toast.error('Invalid Google OAuth callback. Please try again.');
        window.location.href = '/';
        return;
      }

      try {
        const response = await fetch('/api/google/oauth', {
          method: 'POST',
          headers: buildOAuthAuthHeaders(true),
          body: JSON.stringify({ code, state })
        });

        const data = await response.json().catch(() => ({}));
        if (!response.ok) {
          throw new Error(data?.message || 'Failed to exchange Google authorization code.');
        }
        if (!data?.accessToken || typeof data.accessToken !== 'string') {
          throw new Error('No access token received.');
        }

        const target = normalizeGoogleTarget(data?.target);
        setOAuthTokens(target, data.accessToken);

        toast.success(getSuccessMessage(target));
        const returnPath = normalizeReturnPath(data?.returnPath, TARGET_RETURN_PATH[target]);
        window.location.href = returnPath;
      } catch (err) {
        console.error('Token exchange error:', err);
        toast.error('Failed to complete authentication. Please try again.');
        window.location.href = '/';
      }
    };

    void run();
  }, []);

  return (
    <div className="flex items-center justify-center h-screen">
      <div className="text-center">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary-600 mx-auto mb-4"></div>
        <p className="text-gray-600">Completing authentication...</p>
      </div>
    </div>
  );
};

export default GoogleAuthCallback;

