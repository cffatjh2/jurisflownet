import React, { useState, useEffect } from 'react';
import { useClientAuth } from '../../contexts/ClientAuthContext';
import { User } from '../Icons';
import { toast } from '../Toast';
import { clientApi } from '../../services/clientApi';

const ClientProfile: React.FC = () => {
  const { client } = useClientAuth();
  const [profile, setProfile] = useState<any>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [editing, setEditing] = useState(false);
  const [formData, setFormData] = useState({
    name: '',
    phone: '',
    mobile: '',
    address: '',
    city: '',
    state: '',
    zipCode: '',
    country: ''
  });

  useEffect(() => {
    const loadProfile = async () => {
      try {
        const data = await clientApi.fetchJson('/profile');
        setProfile(data);
        setFormData({
          name: data.name || '',
          phone: data.phone || '',
          mobile: data.mobile || '',
          address: data.address || '',
          city: data.city || '',
          state: data.state || '',
          zipCode: data.zipCode || '',
          country: data.country || ''
        });
      } catch (error) {
        console.error('Error loading profile:', error);
      } finally {
        setLoading(false);
      }
    };
    
    loadProfile();
  }, []);

  const handleSave = async () => {
    setSaving(true);
    try {
      const updated = await clientApi.fetchJson('/profile', {
        method: 'PUT',
        body: JSON.stringify(formData)
      });
      setProfile(updated);
      setEditing(false);
      toast.success('Profile updated successfully!');
    } catch (error) {
      console.error('Error updating profile:', error);
      toast.error('Failed to update profile. Please try again.');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-gray-400">Loading...</div>
      </div>
    );
  }

  return (
    <div className="p-8 h-full overflow-y-auto">
      <div className="mb-6">
        <h2 className="text-2xl font-bold text-slate-900">Profile</h2>
        <p className="text-gray-600 mt-1">Manage your account information</p>
      </div>

      <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-6 max-w-2xl">
        <div className="flex items-center justify-between mb-6">
          <div className="flex items-center gap-4">
            <div className="w-16 h-16 rounded-full bg-gradient-to-tr from-blue-500 to-indigo-500 text-white font-bold flex items-center justify-center text-xl shadow-lg">
              {profile?.name?.charAt(0) || client?.name?.charAt(0) || 'C'}
            </div>
            <div>
              <h3 className="text-xl font-bold text-slate-900">{profile?.name || client?.name}</h3>
              <p className="text-gray-600">{profile?.email || client?.email}</p>
            </div>
          </div>
          {!editing && (
            <button
              onClick={() => setEditing(true)}
              className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700"
            >
              Edit Profile
            </button>
          )}
        </div>

        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
              {editing ? (
                <input
                  type="text"
                  value={formData.name}
                  onChange={e => setFormData({...formData, name: e.target.value})}
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                />
              ) : (
                <div className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-gray-50 text-gray-600">
                  {profile?.name || 'N/A'}
                </div>
              )}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Email</label>
              <div className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-gray-50 text-gray-600">
                {profile?.email || 'N/A'}
              </div>
              <p className="text-xs text-gray-500 mt-1">Email cannot be changed</p>
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Phone</label>
              {editing ? (
                <input
                  type="tel"
                  value={formData.phone}
                  onChange={e => setFormData({...formData, phone: e.target.value})}
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                />
              ) : (
                <div className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-gray-50 text-gray-600">
                  {profile?.phone || 'N/A'}
                </div>
              )}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Mobile</label>
              {editing ? (
                <input
                  type="tel"
                  value={formData.mobile}
                  onChange={e => setFormData({...formData, mobile: e.target.value})}
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                />
              ) : (
                <div className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-gray-50 text-gray-600">
                  {profile?.mobile || 'N/A'}
                </div>
              )}
            </div>
          </div>

          {profile?.company && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Company</label>
              <div className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-gray-50 text-gray-600">
                {profile.company}
              </div>
            </div>
          )}

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Address</label>
            {editing ? (
              <input
                type="text"
                value={formData.address}
                onChange={e => setFormData({...formData, address: e.target.value})}
                className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
              />
            ) : (
              <div className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-gray-50 text-gray-600">
                {profile?.address || 'N/A'}
              </div>
            )}
          </div>

          <div className="grid grid-cols-3 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">City</label>
              {editing ? (
                <input
                  type="text"
                  value={formData.city}
                  onChange={e => setFormData({...formData, city: e.target.value})}
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                />
              ) : (
                <div className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-gray-50 text-gray-600">
                  {profile?.city || 'N/A'}
                </div>
              )}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">State</label>
              {editing ? (
                <input
                  type="text"
                  value={formData.state}
                  onChange={e => setFormData({...formData, state: e.target.value})}
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                />
              ) : (
                <div className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-gray-50 text-gray-600">
                  {profile?.state || 'N/A'}
                </div>
              )}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">ZIP Code</label>
              {editing ? (
                <input
                  type="text"
                  value={formData.zipCode}
                  onChange={e => setFormData({...formData, zipCode: e.target.value})}
                  className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                />
              ) : (
                <div className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-gray-50 text-gray-600">
                  {profile?.zipCode || 'N/A'}
                </div>
              )}
            </div>
          </div>

          {editing && (
            <div className="pt-4 border-t border-gray-200 flex justify-end gap-3">
              <button
                onClick={() => {
                  setEditing(false);
                  setFormData({
                    name: profile?.name || '',
                    phone: profile?.phone || '',
                    mobile: profile?.mobile || '',
                    address: profile?.address || '',
                    city: profile?.city || '',
                    state: profile?.state || '',
                    zipCode: profile?.zipCode || '',
                    country: profile?.country || ''
                  });
                }}
                className="px-4 py-2 text-gray-600 hover:text-gray-800 text-sm font-medium"
              >
                Cancel
              </button>
              <button
                onClick={handleSave}
                disabled={saving}
                className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 disabled:opacity-50"
              >
                {saving ? 'Saving...' : 'Save Changes'}
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default ClientProfile;

