import React, { useEffect, useRef } from 'react';
import { toast } from './Toast';
import { buildOAuthAuthHeaders, setOAuthTokens } from '../services/oauthSecurity';

const normalizeReturnPath = (value: unknown, fallback: string): string => {
  if (typeof value !== 'string') return fallback;
  const trimmed = value.trim();
  if (!trimmed.startsWith('/') || trimmed.startsWith('//')) return fallback;
  return trimmed;
};

const ZoomAuthCallback: React.FC = () => {
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
        toast.error('Zoom authentication failed. Please try again.');
        window.location.href = '/#videocall';
        return;
      }

      if (!code || !state) {
        toast.error('Invalid Zoom OAuth callback. Please try again.');
        window.location.href = '/#videocall';
        return;
      }

      try {
        const response = await fetch('/api/zoom/oauth', {
          method: 'POST',
          headers: buildOAuthAuthHeaders(true),
          body: JSON.stringify({ code, state })
        });

        const data = await response.json().catch(() => ({}));
        if (!response.ok) {
          throw new Error(data?.message || 'Failed to exchange Zoom authorization code.');
        }
        if (!data?.accessToken || typeof data.accessToken !== 'string') {
          throw new Error('No access token received.');
        }

        setOAuthTokens('zoom', data.accessToken, data?.refreshToken);
        toast.success('Zoom successfully connected!');
        const returnPath = normalizeReturnPath(data?.returnPath, '/#videocall');
        window.location.href = returnPath;
      } catch (err) {
        console.error('Token exchange error:', err);
        toast.error('Failed to complete Zoom authentication. Please try again.');
        window.location.href = '/#videocall';
      }
    };

    void run();
  }, []);

  return (
    <div className="flex items-center justify-center h-screen">
      <div className="text-center">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600 mx-auto mb-4"></div>
        <p className="text-gray-600">Completing Zoom authentication...</p>
      </div>
    </div>
  );
};

export default ZoomAuthCallback;

