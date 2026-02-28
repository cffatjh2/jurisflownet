import React, { createContext, useContext, useState, ReactNode } from 'react';
import { translations, Language } from '../translations';

export type Currency = 'USD' | 'EUR' | 'TRY' | 'GBP';

interface LanguageContextType {
  language: Language;
  currency: Currency;
  setLanguage: (lang: Language) => void;
  setCurrency: (curr: Currency) => void;
  t: (key: keyof typeof translations['en']) => string;
  formatCurrency: (amount: number) => string;
  formatDate: (dateString: string) => string;
}

const LanguageContext = createContext<LanguageContextType | undefined>(undefined);

const CURRENCY_LOCALES: Record<Currency, string> = {
  USD: 'en-US',
  EUR: 'de-DE', // General EU formatting
  TRY: 'tr-TR',
  GBP: 'en-GB'
};

export const LanguageProvider = ({ children }: { children: ReactNode }) => {
  const [language, setLanguage] = useState<Language>('en');
  const [currency, setCurrency] = useState<Currency>('USD');

  const t = (key: keyof typeof translations['en']): string => {
    // 3-level fallback: Selected Lang -> English -> Key itself
    // Also handling incomplete translation objects for languages like IT/PL/RU in the mock
    const langObj = translations[language] as any;
    const enObj = translations['en'] as any;
    return langObj?.[key] || enObj?.[key] || key;
  };

  const formatCurrency = (amount: number): string => {
    return new Intl.NumberFormat(CURRENCY_LOCALES[currency], {
      style: 'currency',
      currency: currency,
    }).format(amount);
  };

  const formatDate = (dateString: string): string => {
    const date = new Date(dateString);
    // Use the language for date formatting (e.g. TR uses DD.MM.YYYY)
    return new Intl.DateTimeFormat(language, {
        year: 'numeric',
        month: 'short',
        day: 'numeric'
    }).format(date);
  };
  return (
    <LanguageContext.Provider value={{ language, setLanguage, currency, setCurrency, t, formatCurrency, formatDate }}>
      {children}
    </LanguageContext.Provider>
  );
};

export const useTranslation = () => {
  const context = useContext(LanguageContext);
  if (!context) {
    throw new Error('useTranslation must be used within a LanguageProvider');
  }
  return context;
};