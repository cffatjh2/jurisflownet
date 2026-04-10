const DEFAULT_RETURN_PATH = '/#dashboard';

export const normalizeAppReturnPath = (value: unknown, fallback: string = DEFAULT_RETURN_PATH): string => {
  if (typeof value !== 'string') {
    return fallback;
  }

  const trimmed = value.trim();
  if (!trimmed.startsWith('/') || trimmed.startsWith('//')) {
    return fallback;
  }

  return trimmed;
};

export const getCurrentAppReturnPath = (fallback: string = DEFAULT_RETURN_PATH): string => {
  if (typeof window === 'undefined') {
    return fallback;
  }

  return normalizeAppReturnPath(
    `${window.location.pathname}${window.location.search}${window.location.hash}`,
    fallback
  );
};
