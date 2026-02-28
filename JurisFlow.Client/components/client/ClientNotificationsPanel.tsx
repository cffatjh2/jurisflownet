import React, { useEffect, useState } from 'react';
import { X, Bell } from '../Icons';
import { clientApi } from '../../services/clientApi';

interface ClientNotification {
  id: string;
  title: string;
  message: string;
  type?: string;
  read?: boolean;
  createdAt?: string;
  link?: string | null;
}

interface ClientNotificationsPanelProps {
  open: boolean;
  onClose: () => void;
  onUnreadChange?: (count: number) => void;
}

const ClientNotificationsPanel: React.FC<ClientNotificationsPanelProps> = ({ open, onClose, onUnreadChange }) => {
  const [items, setItems] = useState<ClientNotification[]>([]);
  const [loading, setLoading] = useState(false);

  const loadNotifications = async () => {
    setLoading(true);
    try {
      const data = await clientApi.fetchJson('/notifications');
      setItems(Array.isArray(data) ? data : []);
    } catch (error) {
      console.error('Failed to load client notifications', error);
      setItems([]);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (!open) return;
    loadNotifications();
  }, [open]);

  useEffect(() => {
    const unreadCount = items.filter(item => !item.read).length;
    onUnreadChange?.(unreadCount);
  }, [items, onUnreadChange]);

  const markRead = async (id: string) => {
    setItems(prev => prev.map(item => item.id === id ? { ...item, read: true } : item));
    try {
      await clientApi.fetchJson(`/notifications/${id}/read`, { method: 'POST' });
    } catch (error) {
      console.error('Failed to mark notification read', error);
    }
  };

  const markAllRead = async () => {
    setItems(prev => prev.map(item => ({ ...item, read: true })));
    try {
      await clientApi.fetchJson('/notifications/read-all', { method: 'POST' });
    } catch (error) {
      console.error('Failed to mark notifications read', error);
    }
  };

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-40">
      <div className="absolute inset-0 bg-black/20" onClick={onClose} />
      <div className="absolute right-0 top-0 h-full w-full max-w-sm bg-white shadow-2xl border-l border-gray-200 flex flex-col">
        <div className="h-16 px-6 flex items-center justify-between border-b border-gray-200">
          <div className="flex items-center gap-2">
            <Bell className="w-5 h-5 text-blue-600" />
            <h3 className="text-sm font-bold text-slate-900">Notifications</h3>
          </div>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600">
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="p-4 flex items-center justify-between">
          <span className="text-xs text-gray-500">
            {items.filter(i => !i.read).length} unread
          </span>
          <button
            onClick={markAllRead}
            className="text-xs font-semibold text-blue-600 hover:text-blue-700"
          >
            Mark all read
          </button>
        </div>

        <div className="flex-1 overflow-y-auto px-4 pb-6">
          {loading ? (
            <div className="text-sm text-gray-400 py-8 text-center">Loading notifications...</div>
          ) : items.length === 0 ? (
            <div className="text-sm text-gray-400 py-8 text-center">No notifications</div>
          ) : (
            <div className="space-y-3">
              {items.map(item => (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => markRead(item.id)}
                  className={`w-full text-left p-4 rounded-xl border transition-colors ${
                    item.read ? 'border-gray-200 bg-white' : 'border-blue-200 bg-blue-50'
                  }`}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <p className="text-sm font-semibold text-slate-900">{item.title}</p>
                      <p className="text-xs text-gray-600 mt-1">{item.message}</p>
                    </div>
                    {!item.read && <span className="w-2 h-2 rounded-full bg-blue-600 mt-1" />}
                  </div>
                  {item.createdAt && (
                    <div className="text-[11px] text-gray-400 mt-3">
                      {new Date(item.createdAt).toLocaleString()}
                    </div>
                  )}
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default ClientNotificationsPanel;
