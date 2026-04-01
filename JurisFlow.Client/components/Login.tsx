import React, { useState, useEffect } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { useClientAuth } from '../contexts/ClientAuthContext';
import { useTranslation } from '../contexts/LanguageContext';
import { Scale, Users, CheckSquare, Briefcase, X, Check } from './Icons';

interface LoginProps {
  initialUserType?: 'attorney' | 'client';
}

const signupPlans = [
  {
    id: 'starter-39',
    name: 'Starter',
    price: '$39',
    period: '/mo',
    note: 'Gemini not included',
    features: [
      'Core modules',
      'No Gemini AI access',
      'Standard support'
    ]
  },
  {
    id: 'all-inclusive-59',
    name: 'All Inclusive',
    price: '$59',
    period: '/mo',
    note: 'Everything included',
    features: [
      'All modules',
      'Gemini AI included',
      'Priority support'
    ]
  }
] as const;

const Login: React.FC<LoginProps> = ({ initialUserType = 'attorney' }) => {
  const [userType, setUserType] = useState<'attorney' | 'client'>(initialUserType);
  const [tenantSlug, setTenantSlug] = useState(() => {
    if (typeof window === 'undefined') return '';
    return localStorage.getItem('tenant_slug') || '';
  });
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [mfaRequired, setMfaRequired] = useState(false);
  const [mfaChallengeId, setMfaChallengeId] = useState('');
  const [mfaCode, setMfaCode] = useState('');
  const [mfaExpiresAt, setMfaExpiresAt] = useState('');
  const [showSignupModal, setShowSignupModal] = useState(false);
  const [signupError, setSignupError] = useState('');
  const [checkoutPlanId, setCheckoutPlanId] = useState<string | null>(null);

  const { login, verifyMfa, isAuthenticated } = useAuth();
  const { login: clientLogin, isAuthenticated: isClientAuthenticated } = useClientAuth();
  const { t } = useTranslation();

  // Clear form state when user logs out (isAuthenticated becomes false)
  useEffect(() => {
    if (!isAuthenticated && !isClientAuthenticated) {
      setEmail('');
      setPassword('');
      setError('');
      setMfaRequired(false);
      setMfaChallengeId('');
      setMfaCode('');
      setMfaExpiresAt('');
      setLoading(false);
    }
  }, [isAuthenticated, isClientAuthenticated]);

  useEffect(() => {
    setUserType(initialUserType);
  }, [initialUserType]);

  const getErrorMessage = (err: unknown, fallback: string) => {
    if (err instanceof Error && err.message) {
      return err.message;
    }

    return fallback;
  };

  const handleOpenSignup = () => {
    setSignupError('');
    setShowSignupModal(true);
  };

  const handleStartSignup = async (planId: string) => {
    setSignupError('');
    setCheckoutPlanId(planId);

    try {
      const trimmedTenant = tenantSlug.trim();
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      if (trimmedTenant) {
        headers['X-Tenant-Slug'] = trimmedTenant;
      }

      const response = await fetch('/api/public/subscriptions/checkout', {
        method: 'POST',
        headers,
        body: JSON.stringify({
          planId,
          email: email.trim() || undefined,
          firmCode: trimmedTenant || undefined
        })
      });

      const payload = await response.json().catch(() => null);
      if (!response.ok) {
        throw new Error(payload?.message || 'Checkout could not be started.');
      }

      const checkoutUrl = typeof payload?.checkoutUrl === 'string' ? payload.checkoutUrl : '';
      if (!checkoutUrl) {
        throw new Error('Checkout URL was not returned.');
      }

      if (typeof window !== 'undefined') {
        if (trimmedTenant) {
          localStorage.setItem('tenant_slug', trimmedTenant);
        }
        window.location.href = checkoutUrl;
      }
    } catch (err) {
      setSignupError(getErrorMessage(err, 'Plan redirect failed.'));
    } finally {
      setCheckoutPlanId(null);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      if (mfaRequired && !mfaCode.trim()) {
        setError('Enter the authentication code');
        return;
      }
      if (!mfaRequired && !tenantSlug.trim()) {
        setError('Firm code is required');
        return;
      }
      if (!email || !password) {
        throw new Error();
      }

      if (userType === 'attorney') {
        if (mfaRequired) {
          const result = await verifyMfa(mfaChallengeId, mfaCode);
          if (!result.success) {
            setError(result.error || 'Invalid authentication code');
          }
        } else {
          const result = await login(email, password, tenantSlug.trim());
          if (result.mfaRequired) {
            setMfaRequired(true);
            setMfaChallengeId(result.challengeId || '');
            setMfaExpiresAt(result.challengeExpiresAt || '');
            setError('');
          } else if (!result.success) {
            setError(t('error_login'));
          }
        }
      } else {
        const success = await clientLogin(email, password, tenantSlug.trim());
        if (!success) {
          setError('Invalid email or password');
        } else {
          // Redirect to client portal
          window.location.href = '/client';
        }
      }
    } catch (err) {
      setError(userType === 'attorney' ? t('error_login') : 'Invalid email or password');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen w-full flex flex-col justify-center items-center bg-white relative overflow-hidden font-sans">

      {/* Background Decor */}
      <div className="absolute top-0 left-0 w-full h-2 bg-gradient-to-r from-blue-600 via-indigo-600 to-slate-900"></div>
      <div className="absolute top-[-10%] right-[-5%] w-96 h-96 bg-blue-50 rounded-full blur-3xl opacity-60 pointer-events-none"></div>
      <div className="absolute bottom-[-10%] left-[-5%] w-96 h-96 bg-indigo-50 rounded-full blur-3xl opacity-60 pointer-events-none"></div>
      <button
        type="button"
        onClick={handleOpenSignup}
        className="absolute top-6 right-6 z-20 px-4 py-2 rounded-xl border border-slate-200 bg-white/95 text-slate-700 text-sm font-semibold hover:bg-slate-100 transition-colors shadow-sm"
      >
        Sign Up
      </button>

      <div className="w-full max-w-md p-8 z-10">

        {/* Logo Section */}
        <div className="flex flex-col items-center mb-10">
          <div className="w-16 h-16 bg-slate-900 rounded-2xl flex items-center justify-center shadow-xl mb-4 transform rotate-3">
            <Scale className="w-8 h-8 text-white" />
          </div>
          <h1 className="text-4xl font-extrabold text-slate-900 tracking-tight">Juris<span className="text-blue-600">Flow</span></h1>
          <p className="text-gray-500 mt-2 font-medium">NextGen Legal Practice Management</p>
        </div>

        {/* Form Section */}
        <div className="bg-white p-2">
          <h2 className="text-xl font-bold text-slate-800 mb-6 text-center">{t('login_title')}</h2>

          {/* User Type Selection */}
          {!mfaRequired && (
            <div className="mb-6 flex gap-2 bg-gray-100 p-1 rounded-xl">
              <button
                type="button"
                onClick={() => {
                  setUserType('attorney');
                  setError('');
                }}
                className={`flex-1 py-2.5 px-4 rounded-lg text-sm font-bold transition-all ${userType === 'attorney'
                  ? 'bg-white text-slate-900 shadow-sm'
                  : 'text-gray-600 hover:text-gray-900'
                  }`}
              >
                <div className="flex items-center justify-center gap-2">
                  <Scale className="w-4 h-4" />
                  Attorney Login
                </div>
              </button>
              <button
                type="button"
                onClick={() => {
                  setUserType('client');
                  setError('');
                }}
                className={`flex-1 py-2.5 px-4 rounded-lg text-sm font-bold transition-all ${userType === 'client'
                  ? 'bg-white text-slate-900 shadow-sm'
                  : 'text-gray-600 hover:text-gray-900'
                  }`}
              >
                <div className="flex items-center justify-center gap-2">
                  <Briefcase className="w-4 h-4" />
                  Client Login
                </div>
              </button>
            </div>
          )}

          <form className="space-y-5" onSubmit={handleSubmit}>
            {!mfaRequired ? (
              <>
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-1.5">Firm Code</label>
                  <input
                    id="tenant"
                    name="tenant"
                    type="text"
                    required
                    value={tenantSlug}
                    onChange={(e) => setTenantSlug(e.target.value)}
                    className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:bg-white focus:border-transparent outline-none transition-all text-slate-900 font-medium placeholder-gray-400"
                    placeholder="firm-code"
                  />
                </div>
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-1.5">{t('email')}</label>
                  <input
                    id="email"
                    name="email"
                    type="email"
                    required
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:bg-white focus:border-transparent outline-none transition-all text-slate-900 font-medium placeholder-gray-400"
                    placeholder="name@firm.com"
                  />
                </div>
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-1.5">{t('password')}</label>
                  <input
                    id="password"
                    name="password"
                    type="password"
                    required
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:bg-white focus:border-transparent outline-none transition-all text-slate-900 font-medium placeholder-gray-400"
                    placeholder="********"
                  />
                </div>
              </>
            ) : (
              <div className="space-y-3">
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase tracking-wide mb-1.5">Authentication Code</label>
                  <input
                    id="mfa-code"
                    name="mfa-code"
                    type="text"
                    inputMode="numeric"
                    pattern="[0-9]*"
                    required
                    value={mfaCode}
                    onChange={(e) => setMfaCode(e.target.value)}
                    className="w-full px-4 py-3 bg-gray-50 border border-gray-200 rounded-xl focus:ring-2 focus:ring-blue-500 focus:bg-white focus:border-transparent outline-none transition-all text-slate-900 font-medium placeholder-gray-400"
                    placeholder="123456"
                  />
                </div>
                {mfaExpiresAt && (
                  <p className="text-xs text-gray-500">
                    Verification expires {new Date(mfaExpiresAt).toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}
                  </p>
                )}
              </div>
            )}

            {error && (
              <div className="bg-red-50 text-red-600 text-sm p-3 rounded-lg flex items-center gap-2 animate-in fade-in">
                <svg className="w-4 h-4 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" /></svg>
                {error}
              </div>
            )}

            {!mfaRequired && (
              <div className="flex justify-end">
                <a
                  href="/forgot-password"
                  className="text-xs text-gray-500 hover:text-slate-800 transition-colors"
                >
                  Forgot Password?
                </a>
              </div>
            )}

            <button
              type="submit"
              disabled={loading}
              className={`w-full py-4 rounded-xl shadow-lg shadow-blue-500/20 text-sm font-bold text-white bg-slate-900 hover:bg-slate-800 transition-all transform active:scale-[0.98] ${loading ? 'opacity-70 cursor-not-allowed' : ''}`}
            >
              {loading ? (
                <div className="flex items-center justify-center gap-2">
                  <svg className="animate-spin h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                  </svg>
                  Signing In...
                </div>
              ) : mfaRequired ? 'Verify Code' : t('sign_in')}
            </button>
          </form>
        </div>

        {/* Footer Features */}
        <div className="mt-12 pt-8 border-t border-gray-100 grid grid-cols-3 gap-4 text-center">
          <div className="flex flex-col items-center">
            <div className="w-8 h-8 rounded-full bg-blue-50 text-blue-600 flex items-center justify-center mb-2"><CheckSquare className="w-4 h-4" /></div>
            <span className="text-[10px] font-bold text-gray-400 uppercase">Task Mgmt</span>
          </div>
          <div className="flex flex-col items-center">
            <div className="w-8 h-8 rounded-full bg-purple-50 text-purple-600 flex items-center justify-center mb-2"><Scale className="w-4 h-4" /></div>
            <span className="text-[10px] font-bold text-gray-400 uppercase">AI Drafter</span>
          </div>
          <div className="flex flex-col items-center">
            <div className="w-8 h-8 rounded-full bg-emerald-50 text-emerald-600 flex items-center justify-center mb-2"><Users className="w-4 h-4" /></div>
            <span className="text-[10px] font-bold text-gray-400 uppercase">CRM</span>
          </div>
        </div>

        <p className="text-center text-xs text-gray-300 mt-8">
          &copy; 2025 JurisFlow Inc. All rights reserved.
        </p>
      </div>

      {showSignupModal && (
        <div className="fixed inset-0 z-50 bg-slate-950/50 px-4 py-8 overflow-y-auto">
          <div className="mx-auto w-full max-w-3xl bg-white rounded-2xl shadow-2xl border border-slate-100">
            <div className="flex items-start justify-between gap-4 p-6 border-b border-slate-100">
              <div>
                <h3 className="text-xl font-bold text-slate-900">Choose a plan</h3>
                <p className="text-sm text-slate-500 mt-1">Select a plan before sign-up and continue to checkout.</p>
              </div>
              <button
                type="button"
                onClick={() => {
                  if (!checkoutPlanId) {
                    setShowSignupModal(false);
                  }
                }}
                className="p-2 rounded-lg text-slate-500 hover:bg-slate-100 transition-colors disabled:opacity-50"
                disabled={!!checkoutPlanId}
                aria-label="Close plan modal"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <div className="p-6 grid grid-cols-1 md:grid-cols-2 gap-4">
              {signupPlans.map((plan) => {
                const isLoadingPlan = checkoutPlanId === plan.id;
                return (
                  <div
                    key={plan.id}
                    className={`rounded-2xl border p-5 transition-all ${plan.id === 'all-inclusive-59'
                      ? 'border-slate-900 bg-slate-50 shadow-md'
                      : 'border-slate-200 bg-white'
                      }`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <h4 className="text-lg font-bold text-slate-900">{plan.name}</h4>
                        <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mt-1">{plan.note}</p>
                      </div>
                      <div className="text-right">
                        <div className="text-3xl font-extrabold text-slate-900 leading-none">{plan.price}</div>
                        <div className="text-xs font-semibold text-slate-500">{plan.period}</div>
                      </div>
                    </div>

                    <div className="mt-4 space-y-2">
                      {plan.features.map((feature) => (
                        <div key={feature} className="flex items-center gap-2 text-sm text-slate-700">
                          <span className="w-5 h-5 rounded-full bg-emerald-50 text-emerald-600 inline-flex items-center justify-center">
                            <Check className="w-3 h-3" />
                          </span>
                          <span>{feature}</span>
                        </div>
                      ))}
                    </div>

                    <button
                      type="button"
                      onClick={() => handleStartSignup(plan.id)}
                      disabled={!!checkoutPlanId}
                      className={`mt-5 w-full py-3 rounded-xl text-sm font-bold transition-colors ${plan.id === 'all-inclusive-59'
                        ? 'bg-slate-900 text-white hover:bg-slate-800'
                        : 'bg-blue-600 text-white hover:bg-blue-700'
                        } disabled:opacity-70 disabled:cursor-not-allowed`}
                    >
                      {isLoadingPlan ? 'Redirecting...' : 'Sign up with this plan'}
                    </button>
                  </div>
                );
              })}
            </div>

            {signupError && (
              <div className="px-6 pb-6">
                <div className="bg-red-50 text-red-700 text-sm p-3 rounded-xl border border-red-100">
                  {signupError}
                </div>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
};

export default Login;
