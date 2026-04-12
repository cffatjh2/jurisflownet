import React, { useEffect, useState } from 'react';
import { Calendar, Clock, Copy, ExternalLink, Link2, Plus, Trash2, Video, X } from './Icons';
import { googleMeetService, isGoogleMeetAuthExpiredError } from '../services/googleMeetService';
import { microsoftTeamsService } from '../services/microsoftTeamsService';
import { zoomService } from '../services/zoomService';
import { toast } from './Toast';
import { clearOAuthTokens, getOAuthAccessToken, refreshGoogleOAuthAccessToken } from '../services/oauthSecurity';
import { getCurrentAppReturnPath } from '../services/returnPath';

interface VideoCallRoom {
  id: string;
  title: string;
  type: 'google-meet' | 'microsoft-teams' | 'zoom';
  link: string;
  matterId?: string;
  createdAt: string;
}

type VideoPlatform = VideoCallRoom['type'];

const VideoCall: React.FC = () => {
  const [rooms, setRooms] = useState<VideoCallRoom[]>([]);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [creating, setCreating] = useState(false);
  const [newRoom, setNewRoom] = useState({
    title: '',
    type: 'google-meet' as VideoPlatform,
    matterId: '',
    startTime: new Date(Date.now() + 60 * 60 * 1000).toISOString().slice(0, 16),
    duration: 60
  });

  const [googleAccessToken, setGoogleAccessToken] = useState<string | null>(() => getOAuthAccessToken('google-meet'));
  const [microsoftAccessToken, setMicrosoftAccessToken] = useState<string | null>(() => getOAuthAccessToken('microsoft-teams'));
  const [zoomAccessToken, setZoomAccessToken] = useState<string | null>(() => getOAuthAccessToken('zoom'));

  const getRoomsStorageKey = () => {
    const tenantSlug = typeof window !== 'undefined' ? localStorage.getItem('tenant_slug') : null;
    return `video_call_rooms:${tenantSlug || 'default'}`;
  };

  useEffect(() => {
    const savedRooms = localStorage.getItem(getRoomsStorageKey());
    if (savedRooms) {
      try {
        const parsed = JSON.parse(savedRooms);
        if (Array.isArray(parsed)) {
          setRooms(parsed);
        }
      } catch (error) {
        console.error('Failed to parse saved video rooms:', error);
      }
    }

    setGoogleAccessToken(getOAuthAccessToken('google-meet'));
    setMicrosoftAccessToken(getOAuthAccessToken('microsoft-teams'));
    setZoomAccessToken(getOAuthAccessToken('zoom'));
  }, []);

  const resolveOAuthReturnPath = () => {
    const isClientPortal =
      window.location.pathname === '/client' ||
      window.location.pathname.startsWith('/client/') ||
      window.location.hash === '#/client';

    return getCurrentAppReturnPath(isClientPortal ? '/client' : '/#videocall');
  };

  const handleConnect = async (type: VideoPlatform) => {
    try {
      const returnPath = resolveOAuthReturnPath();
      switch (type) {
        case 'google-meet':
          window.location.href = await googleMeetService.getAuthUrl(returnPath);
          break;
        case 'microsoft-teams':
          window.location.href = microsoftTeamsService.getAuthUrl();
          break;
        case 'zoom':
          window.location.href = await zoomService.getAuthUrl(returnPath);
          break;
      }
    } catch (error) {
      console.error('OAuth connection init failed:', error);
      toast.error('Failed to start account connection. Please try again.');
    }
  };

  const handleCreateRoom = async () => {
    if (!newRoom.title.trim()) {
      toast.error('Please enter a meeting title');
      return;
    }

    setCreating(true);

    try {
      const startTime = new Date(newRoom.startTime);
      const endTime = new Date(startTime.getTime() + (newRoom.duration * 60 * 1000));

      let link = '';
      let meetingData: any = {};

      const createGoogleMeeting = async (accessToken: string) => googleMeetService.createMeeting(
        accessToken,
        newRoom.title,
        startTime,
        endTime,
        newRoom.matterId ? `Related to case: ${newRoom.matterId}` : ''
      );

      switch (newRoom.type) {
        case 'google-meet':
          if (!googleAccessToken) {
            toast.error('Please connect Google Meet first');
            setCreating(false);
            return;
          }
          try {
            meetingData = await createGoogleMeeting(googleAccessToken);
          } catch (error) {
            if (!isGoogleMeetAuthExpiredError(error)) {
              throw error;
            }

            const refreshedToken = await refreshGoogleOAuthAccessToken('google-meet').catch(() => null);
            if (!refreshedToken) {
              clearOAuthTokens('google-meet');
              setGoogleAccessToken(null);
              throw new Error('Google Meet connection expired. Please reconnect Google Meet and try again.');
            }

            setGoogleAccessToken(refreshedToken);
            meetingData = await createGoogleMeeting(refreshedToken);
          }
          link = meetingData.meetLink || meetingData.hangoutLink || '';
          break;

        case 'microsoft-teams':
          if (!microsoftAccessToken) {
            toast.error('Please connect Microsoft account first');
            setCreating(false);
            return;
          }
          meetingData = await microsoftTeamsService.createMeeting(
            microsoftAccessToken,
            newRoom.title,
            startTime,
            endTime,
            newRoom.matterId ? `<p>Related to case: ${newRoom.matterId}</p>` : ''
          );
          link = meetingData.joinUrl || meetingData.meetingUrl || '';
          break;

        case 'zoom':
          if (!zoomAccessToken) {
            toast.error('Please connect Zoom account first');
            setCreating(false);
            return;
          }
          meetingData = await zoomService.createMeeting(
            zoomAccessToken,
            newRoom.title,
            startTime,
            newRoom.duration
          );
          link = meetingData.joinUrl || '';
          break;
      }

      if (!link) {
        throw new Error('No meeting link received');
      }

      const room: VideoCallRoom = {
        id: meetingData.id || `room-${Date.now()}`,
        title: newRoom.title,
        type: newRoom.type,
        link,
        matterId: newRoom.matterId || undefined,
        createdAt: new Date().toISOString()
      };

      const updatedRooms = [room, ...rooms];
      setRooms(updatedRooms);
      localStorage.setItem(getRoomsStorageKey(), JSON.stringify(updatedRooms));

      setShowCreateModal(false);
      setNewRoom({
        title: '',
        type: 'google-meet',
        matterId: '',
        startTime: new Date(Date.now() + 60 * 60 * 1000).toISOString().slice(0, 16),
        duration: 60
      });

      toast.success('Meeting created successfully!');
    } catch (error: any) {
      console.error('Error creating meeting:', error);
      toast.error(error.message || 'Failed to create meeting. Please try again.');
    } finally {
      setCreating(false);
    }
  };

  const handleJoinRoom = (room: VideoCallRoom) => {
    window.open(room.link, '_blank');
  };

  const handleDeleteRoom = (id: string) => {
    const updatedRooms = rooms.filter(room => room.id !== id);
    setRooms(updatedRooms);
    localStorage.setItem(getRoomsStorageKey(), JSON.stringify(updatedRooms));
  };

  const handleCopyRoomLink = async (link: string) => {
    try {
      await navigator.clipboard.writeText(link);
      toast.success('Meeting link copied.');
    } catch (error) {
      console.error('Failed to copy meeting link:', error);
      toast.error('Failed to copy meeting link.');
    }
  };

  const getTypeName = (type: string) => {
    switch (type) {
      case 'google-meet':
        return 'Google Meet';
      case 'microsoft-teams':
        return 'Microsoft Teams';
      case 'zoom':
        return 'Zoom';
      default:
        return 'Video Call';
    }
  };

  const getTypeTheme = (type: string) => {
    switch (type) {
      case 'google-meet':
        return {
          iconClass: 'bg-blue-100 text-blue-700',
          badgeClass: 'border-blue-100 bg-blue-50 text-blue-700',
          cardClass: 'from-blue-50 via-white to-cyan-50'
        };
      case 'microsoft-teams':
        return {
          iconClass: 'bg-indigo-100 text-indigo-700',
          badgeClass: 'border-indigo-100 bg-indigo-50 text-indigo-700',
          cardClass: 'from-indigo-50 via-white to-violet-50'
        };
      case 'zoom':
        return {
          iconClass: 'bg-sky-100 text-sky-700',
          badgeClass: 'border-sky-100 bg-sky-50 text-sky-700',
          cardClass: 'from-sky-50 via-white to-cyan-50'
        };
      default:
        return {
          iconClass: 'bg-slate-100 text-slate-700',
          badgeClass: 'border-slate-200 bg-slate-50 text-slate-700',
          cardClass: 'from-slate-50 via-white to-slate-100'
        };
    }
  };

  const formatRoomDate = (value: string) => {
    const parsed = new Date(value);
    if (Number.isNaN(parsed.getTime())) {
      return value;
    }

    return parsed.toLocaleDateString('en-US', {
      month: 'short',
      day: '2-digit',
      year: 'numeric'
    });
  };

  const getPlatformOptionLabel = (type: VideoPlatform) => {
    const isConnected = type === 'google-meet'
      ? Boolean(googleAccessToken)
      : type === 'microsoft-teams'
        ? Boolean(microsoftAccessToken)
        : Boolean(zoomAccessToken);

    return `${getTypeName(type)} ${isConnected ? '(Connected)' : '(Connect Required)'}`;
  };

  return (
    <div className="h-full overflow-y-auto bg-gray-50 p-8">
      <div className="mb-6 flex items-center justify-between gap-4">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Video Calls</h2>
          <p className="mt-1 text-gray-600">Create and join video meetings with clients</p>
        </div>
        <button
          onClick={() => setShowCreateModal(true)}
          className="flex items-center gap-2 rounded-xl bg-blue-600 px-4 py-2.5 text-sm font-medium text-white shadow-sm transition-colors hover:bg-blue-700"
        >
          <Plus className="h-4 w-4" />
          Create Meeting
        </button>
      </div>

      {rooms.length === 0 ? (
        <div className="rounded-2xl border border-gray-200 bg-white p-12 text-center shadow-sm">
          <Video className="mx-auto mb-4 h-16 w-16 text-gray-300" />
          <p className="mb-2 font-medium text-slate-700">No video calls scheduled</p>
          <p className="text-sm text-gray-500">Create a meeting to get started</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-5 xl:grid-cols-2 2xl:grid-cols-3">
          {rooms.map(room => {
            const theme = getTypeTheme(room.type);

            return (
              <div
                key={room.id}
                className={`overflow-hidden rounded-2xl border border-gray-200 bg-gradient-to-br ${theme.cardClass} p-6 shadow-sm transition-shadow hover:shadow-md`}
              >
                <div className="flex items-start justify-between gap-4">
                  <div className="flex items-start gap-3">
                    <div className={`flex h-12 w-12 items-center justify-center rounded-2xl ${theme.iconClass}`}>
                      <Video className="h-5 w-5" />
                    </div>
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <h3 className="text-lg font-semibold text-slate-900">{room.title}</h3>
                        <span className={`rounded-full border px-2.5 py-1 text-[11px] font-semibold uppercase tracking-[0.16em] ${theme.badgeClass}`}>
                          {getTypeName(room.type)}
                        </span>
                      </div>
                      {room.matterId && (
                        <p className="mt-1 text-sm text-slate-500">Matter: {room.matterId}</p>
                      )}
                    </div>
                  </div>

                  <button
                    onClick={() => handleDeleteRoom(room.id)}
                    className="rounded-xl border border-transparent p-2 text-slate-400 transition hover:border-red-100 hover:bg-red-50 hover:text-red-600"
                    title="Delete meeting"
                    aria-label="Delete meeting"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>

                <div className="mt-5 rounded-2xl border border-gray-200 bg-white/85 p-4">
                  <div className="flex items-center gap-2 text-[11px] font-semibold uppercase tracking-[0.16em] text-slate-500">
                    <Link2 className="h-3.5 w-3.5" />
                    Meeting Link
                  </div>
                  <a
                    href={room.link}
                    target="_blank"
                    rel="noreferrer"
                    className="mt-3 block truncate text-sm font-medium text-blue-700 hover:text-blue-800"
                    title={room.link}
                  >
                    {room.link}
                  </a>
                </div>

                <div className="mt-4 flex flex-wrap items-center gap-2 text-xs text-slate-500">
                  <span className="inline-flex items-center gap-1.5 rounded-full bg-white/80 px-3 py-1.5">
                    <Calendar className="h-3.5 w-3.5" />
                    Created {formatRoomDate(room.createdAt)}
                  </span>
                  <span className="inline-flex items-center gap-1.5 rounded-full bg-white/80 px-3 py-1.5">
                    <Clock className="h-3.5 w-3.5" />
                    Ready to join
                  </span>
                </div>

                <div className="mt-5 flex flex-wrap gap-2">
                  <button
                    onClick={() => handleJoinRoom(room)}
                    className="inline-flex flex-1 items-center justify-center gap-2 rounded-xl bg-blue-600 px-4 py-3 text-sm font-medium text-white transition-colors hover:bg-blue-700"
                  >
                    <Video className="h-4 w-4" />
                    Join Meeting
                  </button>
                  <button
                    onClick={() => handleCopyRoomLink(room.link)}
                    className="inline-flex items-center justify-center gap-2 rounded-xl border border-gray-200 bg-white px-4 py-3 text-sm font-medium text-slate-700 transition-colors hover:bg-gray-50"
                    title="Copy meeting link"
                  >
                    <Copy className="h-4 w-4" />
                    Copy Link
                  </button>
                  <a
                    href={room.link}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex items-center justify-center rounded-xl border border-gray-200 bg-white px-3 py-3 text-slate-600 transition-colors hover:bg-gray-50"
                    title="Open meeting in a new tab"
                    aria-label="Open meeting in a new tab"
                  >
                    <ExternalLink className="h-4 w-4" />
                  </a>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {showCreateModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4">
          <div className="w-full max-w-md rounded-xl bg-white shadow-2xl">
            <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
              <h3 className="text-lg font-bold text-slate-800">Create Video Meeting</h3>
              <button
                onClick={() => setShowCreateModal(false)}
                className="text-gray-400 hover:text-gray-600"
              >
                <X className="h-5 w-5" />
              </button>
            </div>

            <div className="space-y-4 p-6">
              <div>
                <label className="mb-1 block text-xs font-bold uppercase text-gray-500">Meeting Title</label>
                <input
                  type="text"
                  placeholder="Client Consultation"
                  className="w-full rounded-lg border border-gray-300 p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                  value={newRoom.title}
                  onChange={event => setNewRoom({ ...newRoom, title: event.target.value })}
                />
              </div>

              <div>
                <label className="mb-1 block text-xs font-bold uppercase text-gray-500">Platform</label>
                <select
                  className="w-full rounded-lg border border-gray-300 p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                  value={newRoom.type}
                  onChange={event => setNewRoom({ ...newRoom, type: event.target.value as VideoPlatform })}
                >
                  <option value="google-meet">{getPlatformOptionLabel('google-meet')}</option>
                  <option value="microsoft-teams">{getPlatformOptionLabel('microsoft-teams')}</option>
                  <option value="zoom">{getPlatformOptionLabel('zoom')}</option>
                </select>
                {newRoom.type === 'google-meet' && !googleAccessToken && (
                  <button
                    type="button"
                    onClick={() => handleConnect('google-meet')}
                    className="mt-2 w-full rounded bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700"
                  >
                    Connect Google Account
                  </button>
                )}
                {newRoom.type === 'microsoft-teams' && !microsoftAccessToken && (
                  <button
                    type="button"
                    onClick={() => handleConnect('microsoft-teams')}
                    className="mt-2 w-full rounded bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700"
                  >
                    Connect Microsoft Account
                  </button>
                )}
                {newRoom.type === 'zoom' && !zoomAccessToken && (
                  <button
                    type="button"
                    onClick={() => handleConnect('zoom')}
                    className="mt-2 w-full rounded bg-blue-600 px-3 py-1.5 text-xs font-medium text-white hover:bg-blue-700"
                  >
                    Connect Zoom Account
                  </button>
                )}
              </div>

              <div>
                <label className="mb-1 block text-xs font-bold uppercase text-gray-500">Start Time</label>
                <input
                  type="datetime-local"
                  className="w-full rounded-lg border border-gray-300 p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                  value={newRoom.startTime}
                  onChange={event => setNewRoom({ ...newRoom, startTime: event.target.value })}
                  min={new Date().toISOString().slice(0, 16)}
                />
              </div>

              <div>
                <label className="mb-1 block text-xs font-bold uppercase text-gray-500">Duration (minutes)</label>
                <input
                  type="number"
                  min="15"
                  max="480"
                  step="15"
                  className="w-full rounded-lg border border-gray-300 p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                  value={newRoom.duration}
                  onChange={event => setNewRoom({ ...newRoom, duration: parseInt(event.target.value, 10) || 60 })}
                />
              </div>

              <div>
                <label className="mb-1 block text-xs font-bold uppercase text-gray-500">Related Case (Optional)</label>
                <input
                  type="text"
                  placeholder="Case number or name"
                  className="w-full rounded-lg border border-gray-300 p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                  value={newRoom.matterId}
                  onChange={event => setNewRoom({ ...newRoom, matterId: event.target.value })}
                />
              </div>
            </div>

            <div className="flex justify-end gap-3 border-t border-gray-200 px-6 py-4">
              <button
                onClick={() => setShowCreateModal(false)}
                className="rounded-lg px-4 py-2 text-sm font-bold text-gray-600 hover:bg-gray-100"
              >
                Cancel
              </button>
              <button
                onClick={handleCreateRoom}
                disabled={creating}
                className="flex items-center gap-2 rounded-lg bg-blue-600 px-6 py-2 text-sm font-bold text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                <Video className="h-4 w-4" />
                {creating ? 'Creating...' : 'Create Meeting'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default VideoCall;
