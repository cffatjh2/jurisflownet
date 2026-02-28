import React, { useState, useEffect } from 'react';
import { useTranslation } from '../contexts/LanguageContext';
import { Scale, ArrowLeft, Lock } from './Icons';
import { api } from '../services/api';
import { passwordRequirementsText, validatePassword } from '../services/passwordPolicy';

const ResetPassword: React.FC = () => {
  const { t } = useTranslation();
  const [token, setToken] = useState<string | null>(null);

  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [validToken, setValidToken] = useState(true);

  useEffect(() => {
    // Get token from URL query params
    const urlParams = new URLSearchParams(window.location.search);
    const urlToken = urlParams.get('token');
    setToken(urlToken);

    if (!urlToken) {
      setValidToken(false);
      setError('Invalid or missing token. Please use the link from your email.');
    }
  }, []);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setMessage('');

    if (password !== confirmPassword) {
      setError('Passwords do not match');
      return;
    }

    const passwordResult = validatePassword(password);
    if (!passwordResult.isValid) {
      setError(passwordResult.message);
      return;
    }

    if (!token) {
      setError('Token not found');
      return;
    }

    setLoading(true);

    try {
      await api.resetPassword(token, password);
      setMessage('Your password has been reset successfully! Redirecting to login...');
      setTimeout(() => {
        window.location.href = '/';
      }, 2000);
    } catch (err: any) {
      setError(err.message || 'An error occurred while resetting password. Token may be invalid or expired.');
      setValidToken(false);
    } finally {
      setLoading(false);
    }
  };

  if (!validToken && !token) {
    return (
      <div className="min-h-screen w-full flex flex-col justify-center items-center bg-white">
        <div className="w-full max-w-md px-6 py-8 text-center">
          <Lock className="w-16 h-16 text-red-500 mx-auto mb-4" />
          <h1 className="text-2xl font-bold text-slate-800 mb-2">Invalid Token</h1>
          <p className="text-gray-600 mb-6">Password reset link is invalid or expired.</p>
          <a
            href="/forgot-password"
            className="inline-flex items-center gap-2 text-sm text-primary-600 hover:text-primary-700"
          >
            <ArrowLeft className="w-4 h-4" />
            Send new password reset request
          </a>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen w-full flex flex-col justify-center items-center bg-white relative overflow-hidden font-sans">
      <div className="absolute top-0 left-0 w-full h-2 bg-gradient-to-r from-blue-600 via-indigo-600 to-slate-900"></div>

      <div className="w-full max-w-md px-6 py-8">
        <div className="mb-8 text-center">
          <div className="flex items-center justify-center gap-3 mb-4">
            <Scale className="w-8 h-8 text-slate-800" />
            <span className="text-2xl font-bold text-slate-800">Juris<span className="text-primary-500">Flow</span></span>
          </div>
          <h1 className="text-2xl font-bold text-slate-800 mb-2">Set New Password</h1>
          <p className="text-gray-600">Enter your new password</p>
        </div>

        <form onSubmit={handleSubmit} className="space-y-6">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">New Password</label>
            <input
              type="password"
              required
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="w-full px-4 py-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-transparent"
              placeholder={passwordRequirementsText}
              minLength={12}
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Confirm Password</label>
            <input
              type="password"
              required
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              className="w-full px-4 py-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-primary-500 focus:border-transparent"
              placeholder="Confirm your password"
              minLength={12}
            />
          </div>

          {error && (
            <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-700 text-sm">
              {error}
            </div>
          )}

          {message && (
            <div className="p-3 bg-green-50 border border-green-200 rounded-lg text-green-700 text-sm">
              {message}
            </div>
          )}

          <button
            type="submit"
            disabled={loading || !validToken}
            className="w-full py-3 bg-slate-800 text-white rounded-lg font-bold hover:bg-slate-900 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
          >
            {loading ? 'Resetting...' : 'Reset Password'}
          </button>

          <div className="text-center">
            <a
              href="/"
              className="inline-flex items-center gap-2 text-sm text-gray-600 hover:text-slate-800"
            >
              <ArrowLeft className="w-4 h-4" />
              Return to login page
            </a>
          </div>
        </form>
      </div>
    </div>
  );
};

export default ResetPassword;

