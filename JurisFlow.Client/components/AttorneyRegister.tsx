import React, { useMemo, useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { Scale, Check } from './Icons';

const PLAN_LABELS: Record<string, { name: string; price: string; note: string }> = {
  'starter-39': { name: 'Starter', price: '$39/mo', note: 'Gemini not included' },
  'all-inclusive-59': { name: 'All Inclusive', price: '$59/mo', note: 'Everything included' }
};

const getPlanId = () => {
  if (typeof window === 'undefined') return '';
  const params = new URLSearchParams(window.location.search);
  return params.get('plan') || localStorage.getItem('pending_signup_plan') || '';
};

const getStoredValue = (key: string) => {
  if (typeof window === 'undefined') return '';
  return localStorage.getItem(key) || '';
};

const AttorneyRegister: React.FC = () => {
  const { login } = useAuth();
  const [firmName, setFirmName] = useState(getStoredValue('pending_signup_firm_name'));
  const [firmCode, setFirmCode] = useState(getStoredValue('pending_signup_tenant'));
  const [fullName, setFullName] = useState(getStoredValue('pending_signup_name'));
  const [email, setEmail] = useState(getStoredValue('pending_signup_email'));
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const planId = getPlanId();
  const selectedPlan = useMemo(() => PLAN_LABELS[planId] ?? null, [planId]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setSuccess('');

    if (!firmName.trim() || !firmCode.trim() || !fullName.trim() || !email.trim()) {
      setError('Firm name, firm code, full name, and email are required.');
      return;
    }

    if (password.length < 10) {
      setError('Password must be at least 10 characters.');
      return;
    }

    if (password !== confirmPassword) {
      setError('Passwords do not match.');
      return;
    }

    setLoading(true);

    try {
      const response = await fetch('/api/public/register-attorney', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({
          firmName: firmName.trim(),
          firmCode: firmCode.trim(),
          fullName: fullName.trim(),
          email: email.trim(),
          password,
          planId: planId || undefined
        })
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'Registration could not be completed.');
      }

      if (typeof window !== 'undefined') {
        localStorage.removeItem('pending_signup_plan');
        localStorage.removeItem('pending_signup_tenant');
        localStorage.removeItem('pending_signup_email');
        localStorage.removeItem('pending_signup_name');
        localStorage.removeItem('pending_signup_firm_name');
      }

      const loginResult = await login(email.trim(), password, firmCode.trim());
      if (!loginResult.success) {
        setSuccess('Registration completed. You can now sign in with your new account.');
        setError(loginResult.error || '');
        return;
      }

      window.location.href = '/';
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Registration failed.');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen w-full flex flex-col justify-center items-center bg-white relative overflow-hidden font-sans px-4 py-10">
      <div className="absolute top-0 left-0 w-full h-2 bg-gradient-to-r from-blue-600 via-indigo-600 to-slate-900"></div>
      <div className="absolute top-[-10%] right-[-5%] w-96 h-96 bg-blue-50 rounded-full blur-3xl opacity-60 pointer-events-none"></div>
      <div className="absolute bottom-[-10%] left-[-5%] w-96 h-96 bg-indigo-50 rounded-full blur-3xl opacity-60 pointer-events-none"></div>

      <div className="w-full max-w-2xl z-10">
        <div className="flex flex-col items-center mb-8">
          <div className="w-16 h-16 bg-slate-900 rounded-2xl flex items-center justify-center shadow-xl mb-4 transform rotate-3">
            <Scale className="w-8 h-8 text-white" />
          </div>
          <h1 className="text-4xl font-extrabold text-slate-900 tracking-tight">Juris<span className="text-blue-600">Flow</span></h1>
          <p className="text-gray-500 mt-2 font-medium">Attorney Registration</p>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-[1.3fr_0.7fr] gap-6">
          <div className="bg-white rounded-2xl border border-slate-200 shadow-sm p-6">
            <h2 className="text-xl font-bold text-slate-900 mb-6">Create your account</h2>

            <form className="space-y-4" onSubmit={handleSubmit}>
              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-1.5">Firm Name</label>
                <input
                  type="text"
                  value={firmName}
                  onChange={(e) => setFirmName(e.target.value)}
                  className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:bg-white focus:border-transparent outline-none transition-all text-slate-900 font-medium"
                  placeholder="Fatih Hukuk"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-1.5">Firm Code</label>
                <input
                  type="text"
                  value={firmCode}
                  onChange={(e) => setFirmCode(e.target.value)}
                  className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:bg-white focus:border-transparent outline-none transition-all text-slate-900 font-medium"
                  placeholder="fatih-hukuk"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-1.5">Full Name</label>
                <input
                  type="text"
                  value={fullName}
                  onChange={(e) => setFullName(e.target.value)}
                  className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:bg-white focus:border-transparent outline-none transition-all text-slate-900 font-medium"
                  placeholder="Fatih Alpaslan"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-1.5">Email Address</label>
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:bg-white focus:border-transparent outline-none transition-all text-slate-900 font-medium"
                  placeholder="name@firm.com"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-1.5">Password</label>
                <input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:bg-white focus:border-transparent outline-none transition-all text-slate-900 font-medium"
                  placeholder="At least 10 characters"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-1.5">Confirm Password</label>
                <input
                  type="password"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:bg-white focus:border-transparent outline-none transition-all text-slate-900 font-medium"
                  placeholder="Repeat your password"
                />
              </div>

              {error && (
                <div className="bg-red-50 text-red-700 text-sm p-3 rounded-xl border border-red-100">
                  {error}
                </div>
              )}

              {success && (
                <div className="bg-emerald-50 text-emerald-700 text-sm p-3 rounded-xl border border-emerald-100">
                  {success}
                </div>
              )}

              <button
                type="submit"
                disabled={loading}
                className="w-full py-4 rounded-xl shadow-lg shadow-blue-500/20 text-sm font-bold text-white bg-slate-900 hover:bg-slate-800 transition-all disabled:opacity-70 disabled:cursor-not-allowed"
              >
                {loading ? 'Creating account...' : 'Create account and sign in'}
              </button>
            </form>
          </div>

          <div className="bg-slate-50 rounded-2xl border border-slate-200 shadow-sm p-6">
            <h3 className="text-lg font-bold text-slate-900">Selected plan</h3>
            {selectedPlan ? (
              <div className="mt-4 space-y-3">
                <div>
                  <div className="text-2xl font-extrabold text-slate-900">{selectedPlan.price}</div>
                  <div className="text-sm font-semibold text-slate-900">{selectedPlan.name}</div>
                  <div className="text-sm text-slate-500 mt-1">{selectedPlan.note}</div>
                </div>
                <div className="flex items-start gap-2 text-sm text-slate-700">
                  <span className="w-5 h-5 rounded-full bg-emerald-50 text-emerald-600 inline-flex items-center justify-center mt-0.5">
                    <Check className="w-3 h-3" />
                  </span>
                  <span>Your subscription selection has been captured. Complete registration to access the attorney portal.</span>
                </div>
              </div>
            ) : (
              <p className="text-sm text-slate-500 mt-4">
                Complete registration with your firm information. If you arrived from checkout, the selected plan will be applied after payment confirmation.
              </p>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

export default AttorneyRegister;
