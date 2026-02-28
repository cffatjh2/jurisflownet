export const passwordRequirementsText =
  'At least 12 characters with uppercase, lowercase, number, and symbol. No spaces.';

export const validatePassword = (
  password: string,
  options?: { email?: string; name?: string }
) => {
  if (!password) {
    return { isValid: false, message: 'Password is required.' };
  }

  if (password.length < 12) {
    return { isValid: false, message: 'Password must be at least 12 characters.' };
  }

  if (/\s/.test(password)) {
    return { isValid: false, message: 'Password cannot contain spaces.' };
  }

  if (!/[A-Z]/.test(password)) {
    return { isValid: false, message: 'Password must include at least one uppercase letter.' };
  }

  if (!/[a-z]/.test(password)) {
    return { isValid: false, message: 'Password must include at least one lowercase letter.' };
  }

  if (!/[0-9]/.test(password)) {
    return { isValid: false, message: 'Password must include at least one number.' };
  }

  if (!/[^A-Za-z0-9]/.test(password)) {
    return { isValid: false, message: 'Password must include at least one symbol.' };
  }

  const normalized = password.trim().toLowerCase();
  if (options?.email) {
    const local = options.email.split('@')[0].trim().toLowerCase();
    if (local.length >= 3 && normalized.includes(local)) {
      return { isValid: false, message: 'Password should not include your email.' };
    }
  }

  if (options?.name) {
    const tokens = options.name
      .split(' ')
      .map(token => token.trim().toLowerCase())
      .filter(token => token.length >= 3);
    if (tokens.some(token => normalized.includes(token))) {
      return { isValid: false, message: 'Password should not include your name.' };
    }
  }

  return { isValid: true, message: '' };
};
