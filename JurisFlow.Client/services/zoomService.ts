import { requestOAuthState } from './oauthSecurity';

// Zoom Service - Creates real Zoom meetings via Zoom API
const ZOOM_API_BASE = 'https://api.zoom.us/v2';

export interface ZoomMeeting {
  id: string;
  topic: string;
  startTime: string;
  duration: number;
  joinUrl: string;
  startUrl: string;
  password?: string;
}

export const zoomService = {
  // Create a Zoom meeting via Zoom API
  createMeeting: async (
    accessToken: string,
    topic: string,
    startTime: Date,
    duration: number = 60,
    password?: string
  ): Promise<ZoomMeeting> => {
    const meeting = {
      topic: topic,
      type: 2, // Scheduled meeting
      start_time: startTime.toISOString().replace(/\.\d{3}Z$/, 'Z'),
      duration: duration,
      timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      password: password || Math.random().toString(36).substring(2, 10),
      settings: {
        host_video: true,
        participant_video: true,
        join_before_host: false,
        mute_upon_entry: false,
        waiting_room: false
      }
    };

    const response = await fetch(`${ZOOM_API_BASE}/users/me/meetings`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${accessToken}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(meeting)
    });

    if (!response.ok) {
      const error = await response.text();
      throw new Error(`Failed to create Zoom meeting: ${error}`);
    }

    const data = await response.json();
    
    return {
      id: String(data.id),
      topic: data.topic,
      startTime: data.start_time,
      duration: data.duration,
      joinUrl: data.join_url,
      startUrl: data.start_url,
      password: data.password
    };
  },

  // Get OAuth2 authorization URL for Zoom API
  getAuthUrl: async (returnPath: string = '/#videocall'): Promise<string> => {
    const clientId = import.meta.env.VITE_ZOOM_CLIENT_ID || '';
    const redirectUri = `${window.location.origin}/auth/zoom/callback`;
    const responseType = 'code';
    const normalizedReturnPath = returnPath.startsWith('/') && !returnPath.startsWith('//')
      ? returnPath
      : '/#videocall';
    const state = await requestOAuthState('zoom', 'zoom', normalizedReturnPath);

    return `https://zoom.us/oauth/authorize?response_type=${responseType}&client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&state=${encodeURIComponent(state)}`;
  }
};

