import React, { useState, useEffect } from 'react';
import { useTranslation } from '../contexts/LanguageContext';
import { Video, Phone, X, Plus } from './Icons';
import { googleMeetService } from '../services/googleMeetService';
import { microsoftTeamsService } from '../services/microsoftTeamsService';
import { zoomService } from '../services/zoomService';
import { toast } from './Toast';
import { getOAuthAccessToken, getPreferredGoogleAccessToken } from '../services/oauthSecurity';

interface VideoCallRoom {
  id: string;
  title: string;
  type: 'google-meet' | 'microsoft-teams' | 'zoom';
  link: string;
  matterId?: string;
  createdAt: string;
}

const VideoCall: React.FC = () => {
  const { t } = useTranslation();
  const [rooms, setRooms] = useState<VideoCallRoom[]>([]);
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [creating, setCreating] = useState(false);
  const [newRoom, setNewRoom] = useState({
    title: '',
    type: 'google-meet' as 'google-meet' | 'microsoft-teams' | 'zoom',
    matterId: '',
    startTime: new Date(Date.now() + 60 * 60 * 1000).toISOString().slice(0, 16), // 1 hour from now
    duration: 60
  });

  // Check for access tokens
  const [googleAccessToken, setGoogleAccessToken] = useState<string | null>(() => getPreferredGoogleAccessToken());
  const [microsoftAccessToken, setMicrosoftAccessToken] = useState<string | null>(() => getOAuthAccessToken('microsoft-teams'));
  const [zoomAccessToken, setZoomAccessToken] = useState<string | null>(() => getOAuthAccessToken('zoom'));

  const getRoomsStorageKey = () => {
    const tenantSlug = typeof window !== 'undefined' ? localStorage.getItem('tenant_slug') : null;
    return `video_call_rooms:${tenantSlug || 'default'}`;
  };

  useEffect(() => {
    // Load saved rooms from localStorage
    const savedRooms = localStorage.getItem(getRoomsStorageKey());
    if (savedRooms) {
      setRooms(JSON.parse(savedRooms));
    }

    // Refresh OAuth token snapshots (migrates legacy localStorage tokens to sessionStorage).
    setGoogleAccessToken(getPreferredGoogleAccessToken());
    setMicrosoftAccessToken(getOAuthAccessToken('microsoft-teams'));
    setZoomAccessToken(getOAuthAccessToken('zoom'));
  }, []);

  const resolveOAuthReturnPath = () => {
    const isClientPortal =
      window.location.pathname === '/client' ||
      window.location.pathname.startsWith('/client/') ||
      window.location.hash === '#/client';

    return isClientPortal ? '/client' : '/#videocall';
  };

  const handleConnect = async (type: 'google-meet' | 'microsoft-teams' | 'zoom') => {
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

      switch (newRoom.type) {
        case 'google-meet':
          if (!googleAccessToken) {
            toast.error('Please connect Google account first');
            setCreating(false);
            return;
          }
          meetingData = await googleMeetService.createMeeting(
            googleAccessToken,
            newRoom.title,
            startTime,
            endTime,
            newRoom.matterId ? `Related to case: ${newRoom.matterId}` : ''
          );
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
    const updatedRooms = rooms.filter(r => r.id !== id);
    setRooms(updatedRooms);
    localStorage.setItem(getRoomsStorageKey(), JSON.stringify(updatedRooms));
  };

  const getTypeIcon = (type: string) => {
    switch (type) {
      case 'google-meet':
        return '🔵';
      case 'microsoft-teams':
        return '🔷';
      case 'zoom':
        return '📹';
      default:
        return '📞';
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

  return (
    <div className="p-8 h-full overflow-y-auto bg-gray-50">
      <div className="mb-6 flex justify-between items-center">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Video Calls</h2>
          <p className="text-gray-600 mt-1">Create and join video meetings with clients</p>
        </div>
        <button
          onClick={() => setShowCreateModal(true)}
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors shadow-sm"
        >
          <Plus className="w-4 h-4" /> Create Meeting
        </button>
      </div>

      {rooms.length === 0 ? (
        <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-12 text-center">
          <Video className="w-16 h-16 text-gray-300 mx-auto mb-4" />
          <p className="text-gray-400 mb-2">No video calls scheduled</p>
          <p className="text-sm text-gray-500">Create a meeting to get started</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {rooms.map(room => (
            <div key={room.id} className="bg-white rounded-xl shadow-sm border border-gray-200 p-6 hover:shadow-md transition-shadow">
              <div className="flex justify-between items-start mb-4">
                <div className="flex items-center gap-3">
                  <div className="w-12 h-12 rounded-lg bg-blue-50 flex items-center justify-center text-2xl">
                    {getTypeIcon(room.type)}
                  </div>
                  <div>
                    <h3 className="font-bold text-slate-900">{room.title}</h3>
                    <p className="text-xs text-gray-500">{getTypeName(room.type)}</p>
                  </div>
                </div>
                <button
                  onClick={() => handleDeleteRoom(room.id)}
                  className="text-gray-400 hover:text-red-600 transition-colors"
                >
                  <X className="w-4 h-4" />
                </button>
              </div>

              <div className="mb-4">
                <p className="text-xs text-gray-500 mb-1">Meeting Link</p>
                <p className="text-sm text-blue-600 truncate font-mono">{room.link}</p>
              </div>

              <div className="flex gap-2">
                <button
                  onClick={() => handleJoinRoom(room)}
                  className="flex-1 flex items-center justify-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors"
                >
                  <Video className="w-4 h-4" /> Join Meeting
                </button>
                <button
                  onClick={() => {
                    navigator.clipboard.writeText(room.link);
                    toast.success('Link copied to clipboard!');
                  }}
                  className="px-4 py-2 bg-gray-100 text-gray-700 rounded-lg text-sm font-medium hover:bg-gray-200 transition-colors"
                  title="Copy Link"
                >
                  📋
                </button>
              </div>

              <p className="text-xs text-gray-400 mt-3">
                Created: {new Date(room.createdAt).toLocaleDateString()}
              </p>
            </div>
          ))}
        </div>
      )}

      {/* Create Meeting Modal */}
      {showCreateModal && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-md">
            <div className="px-6 py-4 border-b border-gray-200 flex justify-between items-center">
              <h3 className="font-bold text-lg text-slate-800">Create Video Meeting</h3>
              <button
                onClick={() => setShowCreateModal(false)}
                className="text-gray-400 hover:text-gray-600"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <div className="p-6 space-y-4">
              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Meeting Title</label>
                <input
                  type="text"
                  placeholder="Client Consultation"
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                  value={newRoom.title}
                  onChange={e => setNewRoom({ ...newRoom, title: e.target.value })}
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Platform</label>
                <select
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                  value={newRoom.type}
                  onChange={e => setNewRoom({ ...newRoom, type: e.target.value as any })}
                >
                  <option value="google-meet">
                    Google Meet {googleAccessToken ? '✓' : '(Connect Required)'}
                  </option>
                  <option value="microsoft-teams">
                    Microsoft Teams {microsoftAccessToken ? '✓' : '(Connect Required)'}
                  </option>
                  <option value="zoom">
                    Zoom {zoomAccessToken ? '✓' : '(Connect Required)'}
                  </option>
                </select>
                {newRoom.type === 'google-meet' && !googleAccessToken && (
                  <button
                    type="button"
                    onClick={() => handleConnect('google-meet')}
                    className="mt-2 w-full px-3 py-1.5 bg-blue-600 text-white rounded text-xs font-medium hover:bg-blue-700"
                  >
                    Connect Google Account
                  </button>
                )}
                {newRoom.type === 'microsoft-teams' && !microsoftAccessToken && (
                  <button
                    type="button"
                    onClick={() => handleConnect('microsoft-teams')}
                    className="mt-2 w-full px-3 py-1.5 bg-blue-600 text-white rounded text-xs font-medium hover:bg-blue-700"
                  >
                    Connect Microsoft Account
                  </button>
                )}
                {newRoom.type === 'zoom' && !zoomAccessToken && (
                  <button
                    type="button"
                    onClick={() => handleConnect('zoom')}
                    className="mt-2 w-full px-3 py-1.5 bg-blue-600 text-white rounded text-xs font-medium hover:bg-blue-700"
                  >
                    Connect Zoom Account
                  </button>
                )}
              </div>

              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Start Time</label>
                <input
                  type="datetime-local"
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                  value={newRoom.startTime}
                  onChange={e => setNewRoom({ ...newRoom, startTime: e.target.value })}
                  min={new Date().toISOString().slice(0, 16)}
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Duration (minutes)</label>
                <input
                  type="number"
                  min="15"
                  max="480"
                  step="15"
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                  value={newRoom.duration}
                  onChange={e => setNewRoom({ ...newRoom, duration: parseInt(e.target.value) || 60 })}
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Related Case (Optional)</label>
                <input
                  type="text"
                  placeholder="Case number or name"
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm outline-none focus:border-blue-400 focus:ring-1 focus:ring-blue-400"
                  value={newRoom.matterId}
                  onChange={e => setNewRoom({ ...newRoom, matterId: e.target.value })}
                />
              </div>
            </div>

            <div className="px-6 py-4 border-t border-gray-200 flex justify-end gap-3">
              <button
                onClick={() => setShowCreateModal(false)}
                className="px-4 py-2 text-sm font-bold text-gray-600 hover:bg-gray-100 rounded-lg"
              >
                Cancel
              </button>
              <button
                onClick={handleCreateRoom}
                disabled={creating}
                className="px-6 py-2 text-sm font-bold text-white bg-blue-600 hover:bg-blue-700 rounded-lg flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                <Video className="w-4 h-4" /> {creating ? 'Creating...' : 'Create Meeting'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default VideoCall;

