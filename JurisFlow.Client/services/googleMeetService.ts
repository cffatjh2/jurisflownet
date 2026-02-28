// Google Meet Service - Creates real Google Meet meetings via Calendar API
import { toast } from '../components/Toast';
import { getGoogleClientId } from './googleConfig';
import { requestOAuthState } from './oauthSecurity';
const CALENDAR_API_BASE = 'https://www.googleapis.com/calendar/v3';

export interface GoogleMeetMeeting {
  id: string;
  title: string;
  startTime: string;
  endTime: string;
  meetLink: string;
  hangoutLink?: string;
  conferenceId?: string;
}

export const googleMeetService = {
  // Create a Google Meet meeting via Calendar API
  createMeeting: async (
    accessToken: string,
    title: string,
    startTime: Date,
    endTime: Date,
    description?: string
  ): Promise<GoogleMeetMeeting> => {
    const event = {
      summary: title,
      description: description || '',
      start: {
        dateTime: startTime.toISOString(),
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone
      },
      end: {
        dateTime: endTime.toISOString(),
        timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone
      },
      conferenceData: {
        createRequest: {
          requestId: `meet-${Date.now()}`,
          conferenceSolutionKey: {
            type: 'hangoutsMeet'
          }
        }
      }
    };

    const response = await fetch(`${CALENDAR_API_BASE}/calendars/primary/events?conferenceDataVersion=1`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${accessToken}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(event)
    });

    if (!response.ok) {
      const error = await response.text();
      throw new Error(`Failed to create meeting: ${error}`);
    }

    const data = await response.json();

    return {
      id: data.id,
      title: data.summary,
      startTime: data.start.dateTime,
      endTime: data.end.dateTime,
      meetLink: data.hangoutLink || data.conferenceData?.entryPoints?.[0]?.uri || '',
      hangoutLink: data.hangoutLink,
      conferenceId: data.conferenceData?.conferenceId
    };
  },

  // Get OAuth2 authorization URL for Calendar API
  getAuthUrl: async (returnPath: string = '/#videocall'): Promise<string> => {
    const clientId = getGoogleClientId();

    if (!clientId) {
      throw new Error('VITE_GOOGLE_CLIENT_ID is not set in environment variables');
    }

    const normalizedReturnPath = returnPath.startsWith('/') && !returnPath.startsWith('//')
      ? returnPath
      : '/#videocall';
    const redirectUri = `${window.location.origin}/auth/google/callback`;
    const scope = 'https://www.googleapis.com/auth/calendar https://www.googleapis.com/auth/calendar.events';
    const responseType = 'code';
    const state = await requestOAuthState('google', 'google-meet', normalizedReturnPath);

    return `https://accounts.google.com/o/oauth2/v2/auth?client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&response_type=${responseType}&scope=${encodeURIComponent(scope)}&access_type=offline&prompt=consent&state=${encodeURIComponent(state)}`;
  }
};

