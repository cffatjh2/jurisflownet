import React, { useEffect } from 'react';
import { toast } from './Toast';
import { buildOAuthAuthHeaders, setOAuthTokens } from '../services/oauthSecurity';

const MicrosoftAuthCallback: React.FC = () => {
  const urlParams = new URLSearchParams(window.location.search);
  const code = urlParams.get('code');
  const error = urlParams.get('error');

  useEffect(() => {
    if (error) {
      console.error('OAuth error:', error);
      toast.error('Microsoft authentication failed. Please try again.');
      window.location.href = '/#videocall';
      return;
    }

    if (code) {
      // Exchange code for tokens via backend
      fetch('/api/microsoft/oauth', {
        method: 'POST',
        headers: buildOAuthAuthHeaders(true),
        body: JSON.stringify({ code })
      })
        .then(res => res.json())
        .then(data => {
          if (data.accessToken) {
            setOAuthTokens('microsoft-teams', data.accessToken, data.refreshToken);
            toast.success('Microsoft Teams successfully connected!');
            window.location.href = '/#videocall';
          } else {
            throw new Error('No access token received');
          }
        })
        .catch(err => {
          console.error('Token exchange error:', err);
          toast.error('Failed to complete Microsoft authentication. Please try again.');
          window.location.href = '/#videocall';
        });
    }
  }, [code, error]);

  return (
    <div className="flex items-center justify-center h-screen">
      <div className="text-center">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600 mx-auto mb-4"></div>
        <p className="text-gray-600">Completing Microsoft authentication...</p>
      </div>
    </div>
  );
};

export default MicrosoftAuthCallback;

