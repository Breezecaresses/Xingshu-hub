import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import type { Locale, TranslationSchema } from './types';
import { zhCN } from './zh-CN';
import { enUS } from './en-US';
import { api } from '../shared/api/tauri';
import { Globe } from 'lucide-react';

interface I18nContextType {
  locale: Locale;
  setLocale: (locale: Locale) => void;
  t: (path: string) => string;
  translations: TranslationSchema;
}

const translationsMap: Record<Locale, TranslationSchema> = {
  'zh-CN': zhCN,
  'en-US': enUS,
};

function getInitialLocale(): Locale {
  const saved = localStorage.getItem('ui.locale');
  if (saved === 'zh-CN' || saved === 'en-US') {
    return saved;
  }
  const browserLang = typeof navigator !== 'undefined' ? navigator.language.toLowerCase() : '';
  if (browserLang.startsWith('zh')) {
    return 'zh-CN';
  }
  return 'en-US';
}

function resolveNestedKey(obj: any, path: string): string {
  const parts = path.split('.');
  let current = obj;
  for (const part of parts) {
    if (current && typeof current === 'object' && part in current) {
      current = current[part];
    } else {
      return path;
    }
  }
  return typeof current === 'string' ? current : path;
}

const I18nContext = createContext<I18nContextType | null>(null);

export const I18nProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [locale, setLocaleState] = useState<Locale>(getInitialLocale);

  useEffect(() => {
    // Check if backend supplied a startup locale override via CLI
    api.getStartupLocale()
      .then((startupLocale) => {
        if (startupLocale === 'zh-CN' || startupLocale === 'en-US') {
          setLocaleState(startupLocale);
          localStorage.setItem('ui.locale', startupLocale);
        }
      })
      .catch((err) => {
        console.warn('Failed to query startup locale:', err);
      });

    const handleStorage = (e: StorageEvent) => {
      if (e.key === 'ui.locale' && (e.newValue === 'zh-CN' || e.newValue === 'en-US')) {
        setLocaleState(e.newValue);
      }
    };
    window.addEventListener('storage', handleStorage);
    return () => window.removeEventListener('storage', handleStorage);
  }, []);

  const setLocale = (newLocale: Locale) => {
    setLocaleState(newLocale);
    localStorage.setItem('ui.locale', newLocale);
  };

  const currentTranslations = translationsMap[locale] || zhCN;

  const t = (path: string): string => {
    const val = resolveNestedKey(currentTranslations, path);
    if (val !== path) return val;
    const fallback = resolveNestedKey(enUS, path);
    if (fallback !== path) return fallback;
    return path;
  };

  return (
    <I18nContext.Provider value={{ locale, setLocale, t, translations: currentTranslations }}>
      {children}
    </I18nContext.Provider>
  );
};

export function useTranslation() {
  const context = useContext(I18nContext);
  if (!context) {
    throw new Error('useTranslation must be used within an I18nProvider');
  }
  return context;
}

export const LanguageSwitcher: React.FC<{ compact?: boolean }> = ({ compact = false }) => {
  const { locale, setLocale } = useTranslation();

  return (
    <div className={`lang-switcher ${compact ? 'compact' : ''}`}>
      <Globe size={13} className="lang-icon" />
      <button
        type="button"
        className={`lang-option ${locale === 'zh-CN' ? 'active' : ''}`}
        onClick={() => setLocale('zh-CN')}
      >
        简体中文
      </button>
      <span className="lang-divider">/</span>
      <button
        type="button"
        className={`lang-option ${locale === 'en-US' ? 'active' : ''}`}
        onClick={() => setLocale('en-US')}
      >
        English
      </button>
    </div>
  );
};

export * from './types';
