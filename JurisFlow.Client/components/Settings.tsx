import React, { useState, useEffect, useRef } from 'react';
import { Settings as SettingsIcon, User, Globe, DollarSign, Bell, Lock, X, Shield, Moon, Sun, Briefcase, CreditCard, RefreshCw, Copy, CheckCircle, AlertTriangle, Link, Users } from './Icons';
import { useTranslation, Currency } from '../contexts/LanguageContext';
import { useAuth } from '../contexts/AuthContext';
import { useData } from '../contexts/DataContext';
import { useTheme } from '../contexts/ThemeContext';
import { Language } from '../translations';
import { BillingSettings, FirmSettings, SecuritySettings, IntegrationItem, IntegrationCatalogItem, FirmEntity, Office } from '../types';
import AdminPanel from './AdminPanel';
import AppDirectoryPanel from './AppDirectoryPanel';
import IntegrationOpsPanel from './IntegrationOpsPanel';
import JurisdictionRulesOpsPanel from './JurisdictionRulesOpsPanel';
import LegalBillingOpsPanel from './LegalBillingOpsPanel';
import EfilingWorkflowPanel from './EfilingWorkflowPanel';
import TrustRiskRadarPanel from './TrustRiskRadarPanel';
import { toast } from './Toast';
import { api } from '../services/api';
import {
  isIntegrationOAuthProviderKey,
  startIntegrationOAuth
} from '../services/integrationOAuthService';
import { startEmailAccountOAuth } from '../services/emailAccountOAuthService';
import { getCurrentAppReturnPath } from '../services/returnPath';

const SETTINGS_TR_TEXT_MAP: Record<string, string> = {
  'Settings': 'Ayarlar',
  'Manage your account settings and preferences': 'Hesap ayarlarınızı ve tercihlerinizi yönetin',
  'Profile': 'Profil',
  'Preferences': 'Tercihler',
  'Notifications': 'Bildirimler',
  'Security': 'Güvenlik',
  'Firm Settings': 'Firma Ayarları',
  'Firm Info': 'Firma Bilgileri',
  'Offices & Entities': 'Ofisler ve Tüzel Kişiler',
  'Billing': 'Muhasebe',
  'Integrations': 'Entegrasyonlar',
  'App Directory': 'Uygulama Dizini',
  'Admin Panel': 'Yönetici Paneli',
  'Profile Information': 'Profil Bilgileri',
  'Full Name': 'Ad Soyad',
  'Email': 'E-posta',
  'Phone': 'Telefon',
  'Mobile': 'Cep Telefonu',
  'Address': 'Adres',
  'City': 'Şehir',
  'State': 'Eyalet / İl',
  'ZIP Code': 'Posta Kodu',
  'Country': 'Ülke',
  'Bar Number': 'Baro Sicil No',
  'Bio': 'Biyografi',
  'Save Changes': 'Değişiklikleri Kaydet',
  'Language': 'Dil',
  'Currency': 'Para Birimi',
  'Theme': 'Tema',
  'Light': 'Açık',
  'Dark': 'Koyu',
  'System': 'Sistem',
  'Notification Preferences': 'Bildirim Tercihleri',
  'Email Notifications': 'E-posta Bildirimleri',
  'Receive email notifications for important updates': 'Önemli güncellemeler için e-posta bildirimi al',
  'Task Reminders': 'Görev Hatırlatıcıları',
  'Get reminders for upcoming tasks': 'Yaklaşan görevler için hatırlatma al',
  'Calendar Events': 'Takvim Etkinlikleri',
  'Notifications for calendar events': 'Takvim etkinlikleri için bildirimler',
  'Security Settings': 'Güvenlik Ayarları',
  'Change Password': 'Şifre Değiştir',
  'Current Password': 'Mevcut Şifre',
  'New Password': 'Yeni Şifre',
  'Confirm Password': 'Şifreyi Onayla',
  'Update Password': 'Şifreyi Güncelle',
  'Two-Factor Authentication': 'İki Faktörlü Kimlik Doğrulama',
  'Add an extra layer of security to your account': 'Hesabınıza ek bir güvenlik katmanı ekleyin',
  'Enabled': 'Etkin',
  'Not enabled': 'Etkin değil',
  'MFA enforcement is paused': 'MFA zorunluluğu duraklatıldı',
  'Login will not require verification codes until enforcement is re-enabled.': 'Zorunluluk yeniden etkinleştirilene kadar girişte doğrulama kodu istenmez.',
  'MFA is active': 'MFA etkin',
  'MFA is not enabled': 'MFA etkin değil',
  'Use an authenticator app to generate verification codes.': 'Doğrulama kodu üretmek için bir doğrulayıcı uygulama kullanın.',
  'Start MFA Setup': 'MFA Kurulumunu Başlat',
  'Secret key': 'Gizli anahtar',
  'otpauth URI': 'otpauth URI',
  'Enter code to enable': 'Etkinleştirmek için kod girin',
  'Disable MFA (enter code)': 'MFA\'yı Kapat (kod girin)',
  'Disable MFA': 'MFA\'yı Kapat',
  'Enable MFA': 'MFA\'yı Etkinleştir',
  'Generate Backup Codes': 'Yedek Kodları Oluştur',
  'Regenerate Backup Codes': 'Yedek Kodları Yeniden Oluştur',
  'Backup Codes': 'Yedek Kodlar',
  'Active Sessions': 'Aktif Oturumlar',
  'Current session': 'Mevcut oturum',
  'Revoke': 'İptal Et',
  'Revoke All Other Sessions': 'Diğer Tüm Oturumları Sonlandır',
  'Save Security Settings': 'Güvenlik Ayarlarını Kaydet',
  'Session timeout (minutes)': 'Oturum zaman aşımı (dakika)',
  'Idle timeout (minutes)': 'Boşta kalma zaman aşımı (dakika)',
  'Require MFA on sign-in': 'Girişte MFA zorunlu',
  'Firm information used for invoices, trust accounting, and compliance exports.': 'Faturalar, emanet hesabı ve uyumluluk dışa aktarımları için kullanılan firma bilgileri.',
  'Save Firm Settings': 'Firma Ayarlarını Kaydet',
  'Billing defaults for rates, invoice generation, and trust safeguards.': 'Ücretler, fatura üretimi ve emanet güvenlikleri için muhasebe varsayılanları.',
  'Save Billing Settings': 'Muhasebe Ayarlarını Kaydet',
  'Offices & Entities Tab - Admin Only': 'Ofisler ve Tüzel Kişiler Sekmesi - Yalnızca Yönetici',
  'Select an entity to manage offices, billing defaults, and scheduling metadata.': 'Ofisleri, fatura varsayılanlarını ve planlama meta verilerini yönetmek için bir tüzel kişi seçin.',
  '+ Add Entity': '+ Tüzel Kişi Ekle',
  '+ Add Office': '+ Ofis Ekle',
  'No entities found yet. Add your first legal entity to begin.': 'Henüz tüzel kişi yok. Başlamak için ilk tüzel kişiyi ekleyin.',
  'Default': 'Varsayılan',
  'Active': 'Aktif',
  'Inactive': 'Pasif',
  'Edit': 'Düzenle',
  'Set Default': 'Varsayılan Yap',
  'Deactivate': 'Pasifleştir',
  'Activate': 'Etkinleştir',
  'Remove': 'Kaldır',
  'No offices yet. Add an office to support calendaring and compliance notices.': 'Henüz ofis yok. Takvim ve uyumluluk bildirimleri için bir ofis ekleyin.',
  'Add Entity': 'Tüzel Kişi Ekle',
  'Edit Entity': 'Tüzel Kişiyi Düzenle',
  'Use legal entity details for billing and tax compliance.': 'Faturalama ve vergi uyumluluğu için tüzel kişi bilgilerini kullanın.',
  'Entity Name *': 'Tüzel Kişi Adı *',
  'Legal Name': 'Yasal Unvan',
  'Tax ID (EIN)': 'Vergi No (EIN)',
  'Website': 'Web Sitesi',
  'Zip Code': 'Posta Kodu',
  'Set as default entity': 'Varsayılan tüzel kişi yap',
  'Used for new matters and invoices.': 'Yeni dosyalar ve faturalar için kullanılır.',
  'Entity status': 'Tüzel kişi durumu',
  'Inactive entities will not appear in selections.': 'Pasif tüzel kişiler seçimlerde görünmez.',
  'Save Entity': 'Tüzel Kişiyi Kaydet',
  'Edit Office': 'Ofisi Düzenle',
  'Add Office': 'Ofis Ekle',
  'Office details power scheduling, conflicts, and compliance notices.': 'Ofis bilgileri planlama, conflict ve uyumluluk bildirimlerinde kullanılır.',
  'Office Name *': 'Ofis Adı *',
  'Office Code': 'Ofis Kodu',
  'Time Zone': 'Saat Dilimi',
  'Set as default office': 'Varsayılan ofis yap',
  'Used for new matters when this entity is selected.': 'Bu tüzel kişi seçildiğinde yeni dosyalarda kullanılır.',
  'Office status': 'Ofis durumu',
  'Inactive offices will not appear in selections.': 'Pasif ofisler seçimlerde görünmez.',
  'Save Office': 'Ofisi Kaydet',
  'Connect': 'Bağlan',
  'Connect Gmail': 'Gmail Bağla',
  'Connect Outlook': 'Outlook Bağla',
  'Add an email inbox for matter-linked communications.': 'Dosya ile ilişkilendirilmiş iletişim için bir e-posta kutusu ekleyin.',
  'Email Address': 'E-posta Adresi',
  'Display Name': 'Görünen Ad',
  'Account Label': 'Hesap Etiketi',
  'Account Email (optional)': 'Hesap E-postası (opsiyonel)',
  'API Key': 'API Anahtarı',
  'OAuth Connection': 'OAuth Bağlantısı',
  'Use secure redirect flow. Authorization code entry is automatic after callback.': 'Güvenli yönlendirme akışını kullanın. Callback sonrası yetkilendirme kodu otomatik işlenir.',
  'Start OAuth': 'OAuth Başlat',
  'Authorization Code (optional)': 'Yetkilendirme Kodu (opsiyonel)',
  'Access Token (optional)': 'Access Token (opsiyonel)',
  'Refresh Token (optional)': 'Refresh Token (opsiyonel)',
  'Sync Enabled': 'Senkronizasyon Etkin',
  'Cancel': 'İptal',
  'Saving...': 'Kaydediliyor...',
  'Create': 'Oluştur'
};

const SETTINGS_TR_PLACEHOLDER_MAP: Record<string, string> = {
  'Bar association registration number': 'Baro kayıt numarası',
  'Professional bio or description': 'Profesyonel biyografi veya açıklama',
  'Enter current password': 'Mevcut şifreyi girin',
  'Enter new password': 'Yeni şifreyi girin',
  'Confirm new password': 'Yeni şifreyi onaylayın',
  'XX-XXXXXXX': 'XX-XXXXXXX',
  'https://': 'https://',
  '123456': '123456',
  'Acme Holdings - Operating': 'Örnek Firma - Operasyon',
  'finance@yourfirm.com': 'finance@firma.com',
  'Paste provider API key': 'Sağlayıcı API anahtarını yapıştırın',
  'Paste OAuth authorization code': 'OAuth yetkilendirme kodunu yapıştırın',
  'attorney@yourfirm.com': 'avukat@firma.com',
  'Primary Inbox': 'Birincil Gelen Kutusu'
};

Object.assign(SETTINGS_TR_TEXT_MAP, {
  'Integration Runtime Ops': 'Entegrasyon Operasyonları',
  'Canonical action runner, mappings, conflict/review queues, inbox/outbox replay.': 'Kanonik aksiyon çalıştırıcı, eşlemeler, çatışma/inceleme kuyrukları ve gelen/giden yeniden oynatma.',
  'Capability Matrix': 'Yetenek Matrisi',
  'Contract': 'Sözleşme',
  'Sync Status': 'Senkronizasyon Durumu',
  'Webhook Monitor': 'Webhook İzleme',
  'Reconciliation': 'Mutabakat',
  'Secret Store': 'Secret Deposu',
  'Canonical Action Runner': 'Kanonik Aksiyon Çalıştırıcı',
  'Mapping Profiles': 'Eşleme Profilleri',
  'Conflict Queue': 'Çatışma Kuyruğu',
  'Review Queue': 'İnceleme Kuyruğu',
  'Inbox Events': 'Gelen Olaylar',
  'Outbox Events': 'Giden Olaylar',
  'Inbox Status': 'Gelen Durumu',
  'Outbox Status': 'Giden Durumu',
  'Inbox failed': 'Gelen başarısız',
  'Replayed': 'Yeniden oynatıldı',
  'Outbox pending': 'Giden beklemede',
  'Outbox dead-letter': 'Giden dead-letter',
  'Refresh': 'Yenile',
  'Refreshing...': 'Yenileniyor...',
  'Rotate': 'Döndür',
  'Rotating...': 'Döndürülüyor...',
  'Replay': 'Yeniden Oynat',
  'Replaying...': 'Yeniden Oynatılıyor...',
  'Requeue': 'Tekrar Kuyruğa Al',
  'Requeueing...': 'Tekrar Kuyruğa Alınıyor...',
  'Resolve': 'Çöz',
  'Ignore': 'Yoksay',
  'Approve': 'Onayla',
  'Retry': 'Tekrar Dene',
  'In Review': 'İncelemede',
  'Reject': 'Reddet',
  'Dry run': 'Deneme Çalıştırması',
  'Review on fail': 'Hata durumunda incelemeye gönder',
  'Default profile': 'Varsayılan profil',
  'Invoice preset': 'Fatura hazır ayarı',
  'Payment preset': 'Ödeme hazır ayarı',
  'Customer preset': 'Müşteri hazır ayarı',
  'No inbox events.': 'Gelen olay yok.',
  'No outbox events.': 'Giden olay yok.',
  'Action required': 'İşlem gerekli',
  'Connected': 'Bağlı',
  'Pending': 'Bekliyor',
  'Disabled': 'Devre dışı',
  'Not connected': 'Bağlı değil'
});

const SETTINGS_TR_REGEX_REPLACERS: Array<{ pattern: RegExp; replace: (match: RegExpExecArray) => string }> = [
  {
    pattern: /^Currently using:\s*(.+?)\s*mode$/i,
    replace: (m) => `Şu anda kullanılan mod: ${m[1]}`
  },
  {
    pattern: /^Backup codes remaining:\s*(\d+)$/i,
    replace: (m) => `Kalan yedek kod: ${m[1]}`
  },
  {
    pattern: /^Connect\s+(.+)$/i,
    replace: (m) => `${m[1]} Bağla`
  }
];

const SETTINGS_TR_FRAGMENT_REPLACERS: Array<[string, string]> = [
  ['Save ', 'Kaydet '],
  ['Add ', 'Ekle '],
  ['Edit ', 'Düzenle '],
  ['Remove', 'Kaldır'],
  ['Default', 'Varsayılan'],
  ['Active', 'Aktif'],
  ['Inactive', 'Pasif'],
  ['Status', 'Durum'],
  ['Name', 'Ad'],
  ['Phone', 'Telefon'],
  ['Address', 'Adres'],
  ['City', 'Şehir'],
  ['State', 'Eyalet / İl'],
  ['Country', 'Ülke'],
  ['Website', 'Web Sitesi'],
  ['Language', 'Dil'],
  ['Currency', 'Para Birimi'],
  ['Theme', 'Tema'],
  ['Security', 'Güvenlik'],
  ['Notifications', 'Bildirimler'],
  ['Profile', 'Profil'],
  ['Preferences', 'Tercihler'],
  ['Billing', 'Muhasebe'],
  ['Integrations', 'Entegrasyonlar']
];

SETTINGS_TR_FRAGMENT_REPLACERS.push(
  ['Queue', 'Kuyruk'],
  ['Monitor', 'İzleme'],
  ['Events', 'Olaylar'],
  ['Contract', 'Sözleşme'],
  ['Matrix', 'Matrisi'],
  ['Replay', 'Yeniden Oynat'],
  ['Requeue', 'Tekrar Kuyruğa Al'],
  ['Resolve', 'Çöz'],
  ['Ignore', 'Yoksay'],
  ['Approve', 'Onayla'],
  ['Retry', 'Tekrar Dene'],
  ['Reject', 'Reddet'],
  ['Rows', 'Satır'],
  ['shown', 'gösteriliyor']
);

const settingsOriginalTextMap = new WeakMap<Text, string>();
const settingsOriginalAttrMap = new WeakMap<Element, Record<string, string>>();

function translateSettingsUiText(original: string, language: Language): string {
  if (language !== 'tr') return original;

  const exact = SETTINGS_TR_TEXT_MAP[original];
  if (exact) return exact;

  for (const { pattern, replace } of SETTINGS_TR_REGEX_REPLACERS) {
    const match = pattern.exec(original);
    if (match) return replace(match);
  }

  let next = original;
  for (const [from, to] of SETTINGS_TR_FRAGMENT_REPLACERS) {
    if (next.includes(from)) {
      next = next.split(from).join(to);
    }
  }
  return next;
}

function localizeSettingsDom(root: HTMLElement | null, language: Language) {
  if (!root) return;

  const textWalker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode: (node) => {
      const parent = node.parentElement;
      if (!parent) return NodeFilter.FILTER_REJECT;
      if (['SCRIPT', 'STYLE'].includes(parent.tagName)) return NodeFilter.FILTER_REJECT;
      if (parent.closest('code') && !parent.classList.contains('notranslate')) return NodeFilter.FILTER_SKIP;
      const raw = node.textContent ?? '';
      if (!raw.trim()) return NodeFilter.FILTER_SKIP;
      return NodeFilter.FILTER_ACCEPT;
    }
  });

  let textNode: Text | null;
  while ((textNode = textWalker.nextNode() as Text | null)) {
    if (!settingsOriginalTextMap.has(textNode)) {
      settingsOriginalTextMap.set(textNode, textNode.textContent ?? '');
    }
    const original = settingsOriginalTextMap.get(textNode) ?? (textNode.textContent ?? '');
    const trimmed = original.trim();
    const translatedTrimmed = translateSettingsUiText(trimmed, language);
    if (trimmed !== translatedTrimmed) {
      const leading = original.match(/^\s*/)?.[0] ?? '';
      const trailing = original.match(/\s*$/)?.[0] ?? '';
      textNode.textContent = `${leading}${translatedTrimmed}${trailing}`;
    } else {
      textNode.textContent = original;
    }
  }

  root.querySelectorAll('input[placeholder], textarea[placeholder]').forEach((el) => {
    const key = 'placeholder';
    if (!settingsOriginalAttrMap.has(el)) {
      settingsOriginalAttrMap.set(el, {});
    }
    const attrBag = settingsOriginalAttrMap.get(el)!;
    if (!(key in attrBag)) {
      attrBag[key] = el.getAttribute('placeholder') || '';
    }
    const original = attrBag[key];
    const localized = language === 'tr'
      ? (SETTINGS_TR_PLACEHOLDER_MAP[original] ?? translateSettingsUiText(original, language))
      : original;
    if (localized !== original || language !== 'tr') {
      el.setAttribute('placeholder', localized);
    }
  });
}

const Settings: React.FC = () => {
  const { t, language, setLanguage, currency, setCurrency } = useTranslation();
  const { user } = useAuth();
  const { updateUserProfile } = useData();
  const { theme, setTheme, resolvedTheme } = useTheme();

  const [activeTab, setActiveTab] = useState<'profile' | 'preferences' | 'notifications' | 'security' | 'firm' | 'billing' | 'organization' | 'integrations' | 'appDirectory' | 'admin'>('profile');
  const [profileData, setProfileData] = useState({
    name: '',
    email: '',
    phone: '',
    mobile: '',
    address: '',
    city: '',
    state: '',
    zipCode: '',
    country: '',
    barNumber: '',
    bio: ''
  });
  const [saving, setSaving] = useState(false);

  // Firm Settings State
  const [firmSettings, setFirmSettings] = useState<FirmSettings>({
    firmName: 'Your Law Firm',
    taxId: '',
    ledesFirmId: '',
    address: '',
    city: '',
    state: '',
    zipCode: '',
    phone: '',
    website: ''
  });

  // Billing Settings State
  const [billingSettings, setBillingSettings] = useState<BillingSettings>({
    defaultHourlyRate: 350,
    partnerRate: 500,
    associateRate: 300,
    paralegalRate: 150,
    billingIncrement: 6,
    minimumTimeEntry: 6,
    roundingRule: 'up',
    defaultPaymentTerms: 30,
    invoicePrefix: 'INV-',
    defaultTaxRate: 0,
    ledesEnabled: false,
    utbmsCodesRequired: false,
    evergreenRetainerMinimum: 5000,
    trustBalanceAlerts: true
  });
  const [billingSaving, setBillingSaving] = useState(false);
  const [billingLoading, setBillingLoading] = useState(false);

  // Security Settings State
  const [securitySettings, setSecuritySettings] = useState<SecuritySettings>({
    minPasswordLength: 8,
    requireUppercase: true,
    requireNumbers: true,
    requireSpecialChars: false,
    passwordExpiryDays: 90,
    mfaEnabled: false,
    sessionTimeoutMinutes: 60,
    auditLoggingEnabled: true
  });
  const [firmSaving, setFirmSaving] = useState(false);
  const [firmLoading, setFirmLoading] = useState(false);
  const [securityConfig, setSecurityConfig] = useState({
    sessionTimeoutMinutes: 480,
    idleTimeoutMinutes: 60,
    mfaEnforced: true
  });
  const [mfaStatus, setMfaStatus] = useState({ enabled: false, hasSecret: false, backupCodesRemaining: 0 });
  const [mfaSetup, setMfaSetup] = useState<{ secret: string; otpauthUri: string; issuer: string; accountName: string } | null>(null);
  const [mfaCode, setMfaCode] = useState('');
  const [backupCodes, setBackupCodes] = useState<string[]>([]);
  const [sessions, setSessions] = useState<any[]>([]);
  const [securityLoading, setSecurityLoading] = useState(false);
  const [revokingSessionId, setRevokingSessionId] = useState<string | null>(null);

  // Integrations State
  const createIntegrationDraft = () => ({
    accountLabel: '',
    accountEmail: '',
    syncEnabled: true,
    apiKey: '',
    accessToken: '',
    refreshToken: '',
    authorizationCode: '',
    realmId: '',
    tenantId: ''
  });
  const [integrations, setIntegrations] = useState<IntegrationItem[]>([]);
  const [integrationCatalog, setIntegrationCatalog] = useState<IntegrationCatalogItem[]>([]);
  const [integrationsLoading, setIntegrationsLoading] = useState(false);
  const [integrationsSaving, setIntegrationsSaving] = useState(false);
  const [integrationModalOpen, setIntegrationModalOpen] = useState(false);
  const [integrationDraft, setIntegrationDraft] = useState(createIntegrationDraft);
  const [activeIntegration, setActiveIntegration] = useState<IntegrationCatalogItem | null>(null);

  const [emailAccounts, setEmailAccounts] = useState<any[]>([]);
  const [emailAccountsLoading, setEmailAccountsLoading] = useState(false);
  const [emailConnectOpen, setEmailConnectOpen] = useState(false);
  const [emailConnectProvider, setEmailConnectProvider] = useState<'Gmail' | 'Outlook'>('Gmail');

  // Organization (entities/offices)
  const [entities, setEntities] = useState<FirmEntity[]>([]);
  const [entitiesLoading, setEntitiesLoading] = useState(false);
  const [entitiesSaving, setEntitiesSaving] = useState(false);
  const [selectedEntity, setSelectedEntity] = useState<FirmEntity | null>(null);
  const [offices, setOffices] = useState<Office[]>([]);
  const [officesLoading, setOfficesLoading] = useState(false);
  const [entityModalOpen, setEntityModalOpen] = useState(false);
  const [officeModalOpen, setOfficeModalOpen] = useState(false);
  const [editingEntity, setEditingEntity] = useState<FirmEntity | null>(null);
  const [editingOffice, setEditingOffice] = useState<Office | null>(null);
  const [entityForm, setEntityForm] = useState({
    name: '',
    legalName: '',
    taxId: '',
    email: '',
    phone: '',
    website: '',
    address: '',
    city: '',
    state: '',
    zipCode: '',
    country: 'United States',
    isDefault: false,
    isActive: true
  });
  const [officeForm, setOfficeForm] = useState({
    name: '',
    code: '',
    email: '',
    phone: '',
    address: '',
    city: '',
    state: '',
    zipCode: '',
    country: 'United States',
    timeZone: 'America/New_York',
    isDefault: false,
    isActive: true
  });
  const settingsRootRef = useRef<HTMLDivElement | null>(null);

  const timeZoneOptions = [
    { value: 'America/New_York', label: 'Eastern (US & Canada)' },
    { value: 'America/Chicago', label: 'Central (US & Canada)' },
    { value: 'America/Denver', label: 'Mountain (US & Canada)' },
    { value: 'America/Los_Angeles', label: 'Pacific (US & Canada)' },
    { value: 'America/Phoenix', label: 'Arizona' },
    { value: 'America/Anchorage', label: 'Alaska' },
    { value: 'Pacific/Honolulu', label: 'Hawaii' }
  ];

  const resolveIntegrationKey = (providerKey?: string, provider?: string, category?: string) => {
    if (providerKey?.trim()) {
      return providerKey.trim().toLowerCase();
    }

    return `${(provider || '').trim().toLowerCase()}::${(category || '').trim().toLowerCase()}`;
  };

  const inferConnectionMode = (providerKey?: string) => {
    const normalized = (providerKey || '').trim().toLowerCase();
    if (normalized === 'stripe' || normalized.startsWith('courtlistener-')) {
      return 'api_key';
    }

    return 'oauth';
  };

  const supportsOAuthRedirectStart = (providerKey?: string) => {
    const normalized = (providerKey || '').trim().toLowerCase();
    return normalized === 'google-gmail'
      || normalized === 'microsoft-outlook-mail'
      || normalized === 'quickbooks-online'
      || normalized === 'xero'
      || normalized === 'microsoft-outlook-calendar';
  };

  const findIntegration = (provider: string, category: string, providerKey?: string) => {
    const key = resolveIntegrationKey(providerKey, provider, category);
    return integrations.find(i => resolveIntegrationKey(i.providerKey, i.provider, i.category) === key);
  };

  useEffect(() => {
    if (user) {
      setProfileData({
        name: user.name || '',
        email: user.email || '',
        phone: '',
        mobile: '',
        address: '',
        city: '',
        state: '',
        zipCode: '',
        country: '',
        barNumber: '',
        bio: ''
      });
    }
  }, [user]);

  useEffect(() => {
    const root = settingsRootRef.current;
    if (!root) return;

    let raf = 0;
    const run = () => {
      cancelAnimationFrame(raf);
      raf = requestAnimationFrame(() => localizeSettingsDom(root, language));
    };

    run();

    const observer = new MutationObserver(() => run());
    observer.observe(root, {
      childList: true,
      subtree: true,
      characterData: true,
      attributes: true,
      attributeFilter: ['placeholder']
    });

    return () => {
      cancelAnimationFrame(raf);
      observer.disconnect();
    };
  }, [language, activeTab, entityModalOpen, officeModalOpen, integrationModalOpen, emailConnectOpen]);

  useEffect(() => {
    if (window.location.hash === '#settings-integrations') {
      setActiveTab('integrations');
      return;
    }
    if (window.location.hash === '#settings-app-directory') {
      setActiveTab('appDirectory');
    }
  }, []);

  const refreshSecurity = async () => {
    setSecurityLoading(true);
    try {
      const [config, status, activeSessions] = await Promise.all([
        api.security.getConfig(),
        api.mfa.status(),
        api.security.getSessions()
      ]);
      if (config) {
        setSecurityConfig({
          sessionTimeoutMinutes: config.sessionTimeoutMinutes || 480,
          idleTimeoutMinutes: config.idleTimeoutMinutes || 60,
          mfaEnforced: config.mfaEnforced ?? true
        });
      }
      if (status) {
        setMfaStatus({
          enabled: !!status.enabled,
          hasSecret: !!status.hasSecret,
          backupCodesRemaining: Number(status.backupCodesRemaining || 0)
        });
        setSecuritySettings(prev => ({ ...prev, mfaEnabled: !!status.enabled }));
      }
      if (activeSessions) {
        setSessions(activeSessions);
      }
    } catch (error) {
      console.error('Failed to load security data', error);
    } finally {
      setSecurityLoading(false);
    }
  };

  const loadBillingSettings = async () => {
    setBillingLoading(true);
    try {
      const data = await api.settings.getBilling();
      if (data) {
        setBillingSettings({
          defaultHourlyRate: data.defaultHourlyRate ?? 350,
          partnerRate: data.partnerRate ?? 500,
          associateRate: data.associateRate ?? 300,
          paralegalRate: data.paralegalRate ?? 150,
          billingIncrement: data.billingIncrement ?? 6,
          minimumTimeEntry: data.minimumTimeEntry ?? 6,
          roundingRule: data.roundingRule ?? 'up',
          defaultPaymentTerms: data.defaultPaymentTerms ?? 30,
          invoicePrefix: data.invoicePrefix ?? 'INV-',
          defaultTaxRate: data.defaultTaxRate ?? 0,
          ledesEnabled: !!data.ledesEnabled,
          utbmsCodesRequired: !!data.utbmsCodesRequired,
          evergreenRetainerMinimum: data.evergreenRetainerMinimum ?? 5000,
          trustBalanceAlerts: data.trustBalanceAlerts !== false
        });
      }
    } catch (error) {
      console.error('Failed to load billing settings', error);
      toast.error('Failed to load billing settings.');
    } finally {
      setBillingLoading(false);
    }
  };

  const loadFirmSettings = async () => {
    setFirmLoading(true);
    try {
      const data = await api.settings.getFirm();
      if (data) {
        setFirmSettings({
          firmName: data.firmName ?? 'Your Law Firm',
          taxId: data.taxId ?? '',
          ledesFirmId: data.ledesFirmId ?? '',
          address: data.address ?? '',
          city: data.city ?? '',
          state: data.state ?? '',
          zipCode: data.zipCode ?? '',
          phone: data.phone ?? '',
          website: data.website ?? ''
        });
      }
    } catch (error) {
      console.error('Failed to load firm settings', error);
      toast.error('Failed to load firm settings.');
    } finally {
      setFirmLoading(false);
    }
  };

  const loadIntegrations = async () => {
    setIntegrationsLoading(true);
    try {
      const [itemsResult, catalogResult] = await Promise.allSettled([
        api.settings.getIntegrations(),
        api.settings.getIntegrationCatalog()
      ]);

      const loadedIntegrations = itemsResult.status === 'fulfilled' && Array.isArray(itemsResult.value)
        ? itemsResult.value
        : [];

      if (itemsResult.status === 'fulfilled') {
        setIntegrations(loadedIntegrations);
      } else {
        throw itemsResult.reason;
      }

      if (catalogResult.status === 'fulfilled' && Array.isArray(catalogResult.value)) {
        if (catalogResult.value.length > 0) {
          setIntegrationCatalog(catalogResult.value);
        } else {
          setIntegrationCatalog(
            loadedIntegrations.map(item => ({
              providerKey: item.providerKey || resolveIntegrationKey(undefined, item.provider, item.category),
              provider: item.provider,
              category: item.category,
              description: item.notes || `${item.provider} integration`,
              connectionMode: inferConnectionMode(item.providerKey)
            }))
          );
        }
      } else if (catalogResult.status === 'fulfilled') {
        setIntegrationCatalog(
          loadedIntegrations.map(item => ({
            providerKey: item.providerKey || resolveIntegrationKey(undefined, item.provider, item.category),
            provider: item.provider,
            category: item.category,
            description: item.notes || `${item.provider} integration`,
            connectionMode: inferConnectionMode(item.providerKey)
          }))
        );
      }
    } catch (error) {
      console.error('Failed to load integrations', error);
      toast.error('Failed to load integrations.');
    } finally {
      setIntegrationsLoading(false);
    }
  };

  const saveIntegrations = async (nextItems: IntegrationItem[], previousItems: IntegrationItem[]) => {
    setIntegrationsSaving(true);
    try {
      const updated = await api.settings.updateIntegrations(nextItems);
      setIntegrations(Array.isArray(updated) ? updated : nextItems);
      toast.success('Integrations updated.');
    } catch (error) {
      console.error('Failed to update integrations', error);
      setIntegrations(previousItems);
      toast.error('Failed to update integrations.');
    } finally {
      setIntegrationsSaving(false);
    }
  };

  const loadEmailAccounts = async () => {
    setEmailAccountsLoading(true);
    try {
      const data = await api.emails.accounts.list();
      setEmailAccounts(Array.isArray(data) ? data : []);
    } catch (error) {
      console.error('Failed to load email accounts', error);
      toast.error('Failed to load email accounts.');
    } finally {
      setEmailAccountsLoading(false);
    }
  };

  const handleConnectIntegration = (integration: IntegrationCatalogItem) => {
    setActiveIntegration(integration);
    setIntegrationDraft(createIntegrationDraft());
    setIntegrationModalOpen(true);
  };

  const handleSubmitIntegration = async () => {
    if (!activeIntegration) return;

    const providerKey = activeIntegration.providerKey || resolveIntegrationKey(undefined, activeIntegration.provider, activeIntegration.category);
    const connectionMode = (activeIntegration.connectionMode || '').toLowerCase();
    const hasApiKey = !!integrationDraft.apiKey.trim();
    const hasOAuthCredential = !!integrationDraft.authorizationCode.trim() || !!integrationDraft.accessToken.trim();
    const hasRedirectOAuth = supportsOAuthRedirectStart(providerKey);

    if (connectionMode === 'api_key' && !hasApiKey) {
      toast.error('API key is required for this provider.');
      return;
    }

    if (connectionMode === 'oauth' && hasRedirectOAuth) {
      toast.error('Use Start OAuth to connect this provider.');
      return;
    }

    if (connectionMode === 'oauth' && !hasOAuthCredential) {
      toast.error('Authorization code or access token is required for this provider.');
      return;
    }

    try {
      setIntegrationsSaving(true);
      const connected = await api.settings.connectIntegration(providerKey, {
        accountLabel: integrationDraft.accountLabel.trim() || undefined,
        accountEmail: integrationDraft.accountEmail.trim() || undefined,
        syncEnabled: integrationDraft.syncEnabled,
        apiKey: integrationDraft.apiKey.trim() || undefined,
        accessToken: integrationDraft.accessToken.trim() || undefined,
        refreshToken: integrationDraft.refreshToken.trim() || undefined,
        authorizationCode: integrationDraft.authorizationCode.trim() || undefined,
        realmId: integrationDraft.realmId.trim() || undefined,
        tenantId: integrationDraft.tenantId.trim() || undefined
      });

      if (connected) {
        const integrationKey = resolveIntegrationKey(connected.providerKey, connected.provider, connected.category);
        setIntegrations(prev => [
          ...prev.filter(i => resolveIntegrationKey(i.providerKey, i.provider, i.category) !== integrationKey),
          connected
        ]);
      } else {
        await loadIntegrations();
      }

      toast.success(`${activeIntegration.provider} connected.`);
      closeIntegrationModal();
    } catch (error: any) {
      const message = error?.message || 'Failed to connect integration.';
      console.error('Failed to connect integration', error);
      toast.error(message);
    } finally {
      setIntegrationsSaving(false);
    }
  };

  const handleStartIntegrationOAuth = () => {
    if (!activeIntegration) return;

    const providerKey = activeIntegration.providerKey || resolveIntegrationKey(undefined, activeIntegration.provider, activeIntegration.category);
    if (!isIntegrationOAuthProviderKey(providerKey)) {
      toast.error('OAuth flow is not available for this provider.');
      return;
    }

    try {
      startIntegrationOAuth(providerKey, getCurrentAppReturnPath('/#settings-integrations'));
    } catch (error: any) {
      console.error('Failed to start OAuth flow', error);
      toast.error(error?.message || 'Failed to start OAuth flow.');
    }
  };

  const closeIntegrationModal = () => {
    setIntegrationModalOpen(false);
    setActiveIntegration(null);
    setIntegrationDraft(createIntegrationDraft());
  };

  const closeEmailModal = () => {
    setEmailConnectOpen(false);
  };

  const handleDisconnectIntegration = async (integration: IntegrationItem) => {
    const providerKey = integration.providerKey || resolveIntegrationKey(undefined, integration.provider, integration.category);
    try {
      setIntegrationsSaving(true);
      await api.settings.disconnectIntegration(providerKey);
      setIntegrations(prev => prev.filter(item => item.id !== integration.id));
      toast.success(`${integration.provider} disconnected.`);
    } catch (error: any) {
      console.error('Failed to disconnect integration', error);
      toast.error(error?.message || 'Failed to disconnect integration.');
    } finally {
      setIntegrationsSaving(false);
    }
  };

  const handleSyncIntegration = async (integration: IntegrationItem) => {
    const providerKey = integration.providerKey || resolveIntegrationKey(undefined, integration.provider, integration.category);
    try {
      setIntegrationsSaving(true);
      await api.settings.syncIntegration(providerKey);
      await loadIntegrations();
      toast.success(`${integration.provider} sync completed.`);
    } catch (error: any) {
      console.error('Failed to sync integration', error);
      toast.error(error?.message || 'Failed to sync integration.');
    } finally {
      setIntegrationsSaving(false);
    }
  };

  const handleToggleIntegrationSync = async (integration: IntegrationItem) => {
    const nextItems = integrations.map(item => item.id === integration.id
      ? { ...item, syncEnabled: !item.syncEnabled }
      : item);
    await saveIntegrations(nextItems, integrations);
  };

  const handleConnectEmailAccount = async () => {
    try {
      await startEmailAccountOAuth(
        emailConnectProvider === 'Gmail' ? 'gmail' : 'outlook',
        getCurrentAppReturnPath('/#settings-integrations')
      );
      closeEmailModal();
    } catch (error) {
      console.error('Failed to connect email account', error);
      toast.error(error instanceof Error ? error.message : 'Failed to start email account connection.');
    }
  };

  const handleSyncEmailAccount = async (accountId: string) => {
    try {
      await api.emails.accounts.sync(accountId);
      await loadEmailAccounts();
      toast.success('Email sync started.');
    } catch (error) {
      console.error('Failed to sync email account', error);
      toast.error('Failed to sync email account.');
    }
  };

  const handleDisconnectEmailAccount = async (accountId: string) => {
    try {
      await api.emails.accounts.disconnect(accountId);
      await loadEmailAccounts();
      toast.success('Email account disconnected.');
    } catch (error) {
      console.error('Failed to disconnect email account', error);
      toast.error('Failed to disconnect email account.');
    }
  };

  const resetEntityForm = (entity?: FirmEntity | null) => {
    setEntityForm({
      name: entity?.name || '',
      legalName: entity?.legalName || '',
      taxId: entity?.taxId || '',
      email: entity?.email || '',
      phone: entity?.phone || '',
      website: entity?.website || '',
      address: entity?.address || '',
      city: entity?.city || '',
      state: entity?.state || '',
      zipCode: entity?.zipCode || '',
      country: entity?.country || 'United States',
      isDefault: entity?.isDefault ?? false,
      isActive: entity?.isActive ?? true
    });
  };

  const resetOfficeForm = (office?: Office | null) => {
    setOfficeForm({
      name: office?.name || '',
      code: office?.code || '',
      email: office?.email || '',
      phone: office?.phone || '',
      address: office?.address || '',
      city: office?.city || '',
      state: office?.state || '',
      zipCode: office?.zipCode || '',
      country: office?.country || 'United States',
      timeZone: office?.timeZone || 'America/New_York',
      isDefault: office?.isDefault ?? false,
      isActive: office?.isActive ?? true
    });
  };

  const loadEntities = async (preferredId?: string) => {
    setEntitiesLoading(true);
    try {
      const data = await api.entities.list();
      const items = Array.isArray(data) ? data : [];
      setEntities(items);

      const nextSelected = preferredId
        ? items.find(item => item.id === preferredId)
        : selectedEntity ? items.find(item => item.id === selectedEntity.id) : null;
      const fallback = nextSelected || items.find(item => item.isDefault) || items[0] || null;
      setSelectedEntity(fallback || null);
      if (fallback) {
        await loadOffices(fallback.id);
      } else {
        setOffices([]);
      }
    } catch (error) {
      console.error('Failed to load entities', error);
      toast.error('Failed to load entities.');
    } finally {
      setEntitiesLoading(false);
    }
  };

  const loadOffices = async (entityId: string) => {
    setOfficesLoading(true);
    try {
      const data = await api.entities.offices.list(entityId);
      setOffices(Array.isArray(data) ? data : []);
    } catch (error) {
      console.error('Failed to load offices', error);
      toast.error('Failed to load offices.');
    } finally {
      setOfficesLoading(false);
    }
  };

  const openCreateEntity = () => {
    setEditingEntity(null);
    resetEntityForm();
    setEntityModalOpen(true);
  };

  const openEditEntity = (entity: FirmEntity) => {
    setEditingEntity(entity);
    resetEntityForm(entity);
    setEntityModalOpen(true);
  };

  const closeEntityModal = () => {
    setEntityModalOpen(false);
    setEditingEntity(null);
    resetEntityForm();
  };

  const openCreateOffice = () => {
    setEditingOffice(null);
    resetOfficeForm();
    setOfficeModalOpen(true);
  };

  const openEditOffice = (office: Office) => {
    setEditingOffice(office);
    resetOfficeForm(office);
    setOfficeModalOpen(true);
  };

  const closeOfficeModal = () => {
    setOfficeModalOpen(false);
    setEditingOffice(null);
    resetOfficeForm();
  };

  const handleSaveEntity = async () => {
    if (!entityForm.name.trim()) {
      toast.error('Entity name is required.');
      return;
    }
    setEntitiesSaving(true);
    try {
      const payload = {
        ...entityForm,
        name: entityForm.name.trim()
      };
      if (editingEntity) {
        await api.entities.update(editingEntity.id, payload);
        await loadEntities(editingEntity.id);
        toast.success('Entity updated.');
      } else {
        const created = await api.entities.create(payload);
        await loadEntities(created?.id);
        toast.success('Entity created.');
      }
      closeEntityModal();
    } catch (error) {
      console.error('Failed to save entity', error);
      toast.error('Failed to save entity.');
    } finally {
      setEntitiesSaving(false);
    }
  };

  const handleSetDefaultEntity = async (entity: FirmEntity) => {
    try {
      await api.entities.setDefault(entity.id);
      await loadEntities(entity.id);
      toast.success('Default entity updated.');
    } catch (error) {
      console.error('Failed to set default entity', error);
      toast.error('Failed to set default entity.');
    }
  };

  const handleToggleEntityActive = async (entity: FirmEntity) => {
    try {
      await api.entities.update(entity.id, { isActive: !entity.isActive });
      await loadEntities(entity.id);
      toast.success('Entity status updated.');
    } catch (error) {
      console.error('Failed to update entity status', error);
      toast.error('Failed to update entity status.');
    }
  };

  const handleDeleteEntity = async (entity: FirmEntity) => {
    try {
      await api.entities.remove(entity.id);
      await loadEntities();
      toast.success('Entity removed.');
    } catch (error) {
      console.error('Failed to remove entity', error);
      toast.error('Failed to remove entity.');
    }
  };

  const handleSaveOffice = async () => {
    if (!selectedEntity) return;
    if (!officeForm.name.trim()) {
      toast.error('Office name is required.');
      return;
    }
    setEntitiesSaving(true);
    try {
      const payload = {
        ...officeForm,
        name: officeForm.name.trim()
      };
      if (editingOffice) {
        await api.entities.offices.update(selectedEntity.id, editingOffice.id, payload);
        await loadOffices(selectedEntity.id);
        toast.success('Office updated.');
      } else {
        await api.entities.offices.create(selectedEntity.id, payload);
        await loadOffices(selectedEntity.id);
        toast.success('Office created.');
      }
      closeOfficeModal();
    } catch (error) {
      console.error('Failed to save office', error);
      toast.error('Failed to save office.');
    } finally {
      setEntitiesSaving(false);
    }
  };

  const handleSetDefaultOffice = async (office: Office) => {
    if (!selectedEntity) return;
    try {
      await api.entities.offices.setDefault(selectedEntity.id, office.id);
      await loadOffices(selectedEntity.id);
      toast.success('Default office updated.');
    } catch (error) {
      console.error('Failed to set default office', error);
      toast.error('Failed to set default office.');
    }
  };

  const handleToggleOfficeActive = async (office: Office) => {
    if (!selectedEntity) return;
    try {
      await api.entities.offices.update(selectedEntity.id, office.id, { isActive: !office.isActive });
      await loadOffices(selectedEntity.id);
      toast.success('Office status updated.');
    } catch (error) {
      console.error('Failed to update office status', error);
      toast.error('Failed to update office status.');
    }
  };

  const handleDeleteOffice = async (office: Office) => {
    if (!selectedEntity) return;
    try {
      await api.entities.offices.remove(selectedEntity.id, office.id);
      await loadOffices(selectedEntity.id);
      toast.success('Office removed.');
    } catch (error) {
      console.error('Failed to remove office', error);
      toast.error('Failed to remove office.');
    }
  };

  const handleSaveBillingSettings = async () => {
    setBillingSaving(true);
    try {
      const saved = await api.settings.updateBilling(billingSettings);
      if (saved) {
        setBillingSettings({
          defaultHourlyRate: saved.defaultHourlyRate ?? billingSettings.defaultHourlyRate,
          partnerRate: saved.partnerRate ?? billingSettings.partnerRate,
          associateRate: saved.associateRate ?? billingSettings.associateRate,
          paralegalRate: saved.paralegalRate ?? billingSettings.paralegalRate,
          billingIncrement: saved.billingIncrement ?? billingSettings.billingIncrement,
          minimumTimeEntry: saved.minimumTimeEntry ?? billingSettings.minimumTimeEntry,
          roundingRule: saved.roundingRule ?? billingSettings.roundingRule,
          defaultPaymentTerms: saved.defaultPaymentTerms ?? billingSettings.defaultPaymentTerms,
          invoicePrefix: saved.invoicePrefix ?? billingSettings.invoicePrefix,
          defaultTaxRate: saved.defaultTaxRate ?? billingSettings.defaultTaxRate,
          ledesEnabled: !!saved.ledesEnabled,
          utbmsCodesRequired: !!saved.utbmsCodesRequired,
          evergreenRetainerMinimum: saved.evergreenRetainerMinimum ?? billingSettings.evergreenRetainerMinimum,
          trustBalanceAlerts: saved.trustBalanceAlerts !== false
        });
      }
      toast.success('Billing settings saved.');
    } catch (error) {
      console.error('Failed to save billing settings', error);
      toast.error('Failed to save billing settings.');
    } finally {
      setBillingSaving(false);
    }
  };

  const handleSaveFirmSettings = async () => {
    setFirmSaving(true);
    try {
      const saved = await api.settings.updateFirm(firmSettings);
      if (saved) {
        setFirmSettings({
          firmName: saved.firmName ?? firmSettings.firmName,
          taxId: saved.taxId ?? firmSettings.taxId,
          ledesFirmId: saved.ledesFirmId ?? firmSettings.ledesFirmId,
          address: saved.address ?? firmSettings.address,
          city: saved.city ?? firmSettings.city,
          state: saved.state ?? firmSettings.state,
          zipCode: saved.zipCode ?? firmSettings.zipCode,
          phone: saved.phone ?? firmSettings.phone,
          website: saved.website ?? firmSettings.website
        });
      }
      toast.success('Firm settings saved.');
    } catch (error) {
      console.error('Failed to save firm settings', error);
      toast.error('Failed to save firm settings.');
    } finally {
      setFirmSaving(false);
    }
  };

  useEffect(() => {
    if (activeTab !== 'security') return;
    refreshSecurity();
  }, [activeTab]);

  useEffect(() => {
    if (activeTab !== 'billing') return;
    loadBillingSettings();
  }, [activeTab]);

  useEffect(() => {
    if (activeTab !== 'firm') return;
    loadFirmSettings();
  }, [activeTab]);

  useEffect(() => {
    if (activeTab !== 'organization') return;
    loadEntities();
  }, [activeTab]);

  useEffect(() => {
    if (activeTab !== 'integrations') return;
    loadIntegrations();
    loadEmailAccounts();
  }, [activeTab]);

  const handleSetupMfa = async () => {
    try {
      setSecurityLoading(true);
      const data = await api.mfa.setup();
      if (data) {
        setMfaSetup({
          secret: data.secret,
          otpauthUri: data.otpauthUri,
          issuer: data.issuer,
          accountName: data.accountName
        });
        setBackupCodes([]);
        setMfaCode('');
      }
    } catch (error) {
      console.error('MFA setup failed', error);
      toast.error('Failed to start MFA setup.');
    } finally {
      setSecurityLoading(false);
    }
  };

  const handleEnableMfa = async () => {
    if (!mfaCode.trim()) {
      toast.error('Enter the authentication code.');
      return;
    }
    try {
      setSecurityLoading(true);
      const data = await api.mfa.enable(mfaCode.trim());
      if (data?.backupCodes) {
        setBackupCodes(data.backupCodes);
      }
      const status = await api.mfa.status();
      if (status) {
        setMfaStatus({
          enabled: !!status.enabled,
          hasSecret: !!status.hasSecret,
          backupCodesRemaining: Number(status.backupCodesRemaining || 0)
        });
        setSecuritySettings(prev => ({ ...prev, mfaEnabled: !!status.enabled }));
      }
      setMfaSetup(null);
      setMfaCode('');
      toast.success('MFA enabled.');
    } catch (error) {
      console.error('MFA enable failed', error);
      toast.error('Failed to enable MFA.');
    } finally {
      setSecurityLoading(false);
    }
  };

  const handleDisableMfa = async () => {
    if (!mfaCode.trim()) {
      toast.error('Enter the authentication code.');
      return;
    }
    try {
      setSecurityLoading(true);
      await api.mfa.disable(mfaCode.trim());
      const status = await api.mfa.status();
      if (status) {
        setMfaStatus({
          enabled: !!status.enabled,
          hasSecret: !!status.hasSecret,
          backupCodesRemaining: Number(status.backupCodesRemaining || 0)
        });
        setSecuritySettings(prev => ({ ...prev, mfaEnabled: !!status.enabled }));
      }
      setMfaCode('');
      setBackupCodes([]);
      toast.success('MFA disabled.');
    } catch (error) {
      console.error('MFA disable failed', error);
      toast.error('Failed to disable MFA.');
    } finally {
      setSecurityLoading(false);
    }
  };

  const handleRevokeSession = async (id: string) => {
    try {
      setRevokingSessionId(id);
      await api.security.revokeSession(id);
      const refreshed = await api.security.getSessions();
      if (refreshed) setSessions(refreshed);
      toast.success('Session revoked.');
    } catch (error) {
      console.error('Failed to revoke session', error);
      toast.error('Unable to revoke session.');
    } finally {
      setRevokingSessionId(null);
    }
  };

  const handleCopy = async (value: string) => {
    try {
      await navigator.clipboard.writeText(value);
      toast.success(language === 'tr' ? 'Panoya kopyalandı.' : 'Copied to clipboard.');
    } catch {
      toast.error(language === 'tr' ? 'Kopyalama başarısız oldu.' : 'Copy failed.');
    }
  };

  const FLAGS: Partial<Record<Language, string>> = {
    en: 'EN',
    tr: 'TR'
  };

  const handleSaveProfile = async (e: React.FormEvent) => {
    e.preventDefault();
    setSaving(true);
    try {
      await updateUserProfile(profileData);
      toast.success(language === 'tr' ? 'Profil başarıyla güncellendi' : 'Profile updated successfully');
    } catch (error: any) {
      const baseMessage = typeof error?.message === 'string' ? error.message : '';
      const msg = baseMessage ? `: ${baseMessage}` : '';
      toast.error(language === 'tr' ? `Profil güncellenemedi${msg}` : `Failed to update profile${msg}`);
    } finally {
      setSaving(false);
    }
  };

  const getIntegrationBadge = (status?: string) => {
    const normalized = status || 'not_connected';
    if (normalized === 'connected') return { label: 'Connected', className: 'bg-emerald-100 text-emerald-700' };
    if (normalized === 'pending') return { label: 'Pending', className: 'bg-amber-100 text-amber-700' };
    if (normalized === 'error') return { label: 'Action required', className: 'bg-red-100 text-red-700' };
    if (normalized === 'disabled') return { label: 'Disabled', className: 'bg-gray-100 text-gray-500' };
    return { label: 'Not connected', className: 'bg-gray-100 text-gray-500' };
  };

  return (
    <div ref={settingsRootRef} className="h-full flex flex-col bg-gray-50/50">
      <div className="px-6 py-4 border-b border-gray-200 bg-white">
        <div className="flex items-center gap-3 mb-2">
          <SettingsIcon className="w-6 h-6 text-slate-800" />
          <h1 className="text-2xl font-bold text-slate-800">Settings</h1>
        </div>
        <p className="text-sm text-gray-500">Manage your account settings and preferences</p>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* Sidebar */}
        <div className="w-64 bg-white border-r border-gray-200 p-4">
          <nav className="space-y-1">
            <button
              onClick={() => setActiveTab('profile')}
              className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'profile'
                ? 'bg-slate-100 text-slate-900'
                : 'text-gray-600 hover:bg-gray-50'
                }`}
            >
              <User className="w-5 h-5" />
              Profile
            </button>
            <button
              onClick={() => setActiveTab('preferences')}
              className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'preferences'
                ? 'bg-slate-100 text-slate-900'
                : 'text-gray-600 hover:bg-gray-50'
                }`}
            >
              <Globe className="w-5 h-5" />
              Preferences
            </button>
            <button
              onClick={() => setActiveTab('notifications')}
              className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'notifications'
                ? 'bg-slate-100 text-slate-900'
                : 'text-gray-600 hover:bg-gray-50'
                }`}
            >
              <Bell className="w-5 h-5" />
              Notifications
            </button>
            <button
              onClick={() => setActiveTab('security')}
              className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'security'
                ? 'bg-slate-100 text-slate-900'
                : 'text-gray-600 hover:bg-gray-50'
                }`}
            >
              <Lock className="w-5 h-5" />
              Security
            </button>

            {/* Admin-only sections */}
            {user?.role === 'Admin' && (
              <>
                <div className="pt-4 mt-4 border-t border-gray-200">
                  <p className="text-xs font-semibold text-gray-400 uppercase tracking-wider px-4 mb-2">Firm Settings</p>
                </div>
                <button
                  onClick={() => setActiveTab('firm')}
                  className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'firm'
                    ? 'bg-slate-100 text-slate-900'
                    : 'text-gray-600 hover:bg-gray-50'
                    }`}
                >
                  <Briefcase className="w-5 h-5" />
                  Firm Info
                </button>
                <button
                  onClick={() => setActiveTab('organization')}
                  className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'organization'
                    ? 'bg-slate-100 text-slate-900'
                    : 'text-gray-600 hover:bg-gray-50'
                    }`}
                >
                  <Users className="w-5 h-5" />
                  Offices & Entities
                </button>
                <button
                  onClick={() => setActiveTab('billing')}
                  className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'billing'
                    ? 'bg-slate-100 text-slate-900'
                    : 'text-gray-600 hover:bg-gray-50'
                    }`}
                >
                  <CreditCard className="w-5 h-5" />
                  Billing
                </button>
                <button
                  onClick={() => setActiveTab('integrations')}
                  className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'integrations'
                    ? 'bg-slate-100 text-slate-900'
                    : 'text-gray-600 hover:bg-gray-50'
                    }`}
                >
                  <Link className="w-5 h-5" />
                  Integrations
                </button>
                <button
                  onClick={() => setActiveTab('appDirectory')}
                  className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'appDirectory'
                    ? 'bg-slate-100 text-slate-900'
                    : 'text-gray-600 hover:bg-gray-50'
                    }`}
                >
                  <CheckCircle className="w-5 h-5" />
                  App Directory
                </button>
                <button
                  onClick={() => setActiveTab('admin')}
                  className={`w-full flex items-center gap-3 px-4 py-2.5 rounded-lg text-sm font-medium transition-colors ${activeTab === 'admin'
                    ? 'bg-slate-100 text-slate-900'
                    : 'text-gray-600 hover:bg-gray-50'
                    }`}
                >
                  <Shield className="w-5 h-5" />
                  Admin Panel
                </button>
              </>
            )}
          </nav>
        </div>

        {/* Content */}
        <div className="flex-1 overflow-y-auto p-8">
          {activeTab === 'profile' && (
            <div className="max-w-2xl">
              <h2 className="text-xl font-bold text-slate-800 mb-6">Profile Information</h2>
              <form onSubmit={handleSaveProfile} className="space-y-6">
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Full Name</label>
                    <input
                      type="text"
                      required
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={profileData.name}
                      onChange={e => setProfileData({ ...profileData, name: e.target.value })}
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Email</label>
                    <input
                      type="email"
                      required
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={profileData.email}
                      onChange={e => setProfileData({ ...profileData, email: e.target.value })}
                    />
                  </div>
                </div>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Phone</label>
                    <input
                      type="tel"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={profileData.phone}
                      onChange={e => setProfileData({ ...profileData, phone: e.target.value })}
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Mobile</label>
                    <input
                      type="tel"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={profileData.mobile}
                      onChange={e => setProfileData({ ...profileData, mobile: e.target.value })}
                    />
                  </div>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Address</label>
                  <input
                    type="text"
                    className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                    value={profileData.address}
                    onChange={e => setProfileData({ ...profileData, address: e.target.value })}
                  />
                </div>
                <div className="grid grid-cols-3 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">City</label>
                    <input
                      type="text"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={profileData.city}
                      onChange={e => setProfileData({ ...profileData, city: e.target.value })}
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">State</label>
                    <input
                      type="text"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={profileData.state}
                      onChange={e => setProfileData({ ...profileData, state: e.target.value })}
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">ZIP Code</label>
                    <input
                      type="text"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={profileData.zipCode}
                      onChange={e => setProfileData({ ...profileData, zipCode: e.target.value })}
                    />
                  </div>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Country</label>
                  <input
                    type="text"
                    className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                    value={profileData.country}
                    onChange={e => setProfileData({ ...profileData, country: e.target.value })}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Bar Number</label>
                  <input
                    type="text"
                    className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                    value={profileData.barNumber}
                    onChange={e => setProfileData({ ...profileData, barNumber: e.target.value })}
                    placeholder="Bar association registration number"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Bio</label>
                  <textarea
                    rows={4}
                    className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                    value={profileData.bio}
                    onChange={e => setProfileData({ ...profileData, bio: e.target.value })}
                    placeholder="Professional bio or description"
                  />
                </div>
                <div className="flex justify-end gap-3 pt-4">
                  <button
                    type="submit"
                    disabled={saving}
                    className="px-6 py-2.5 bg-slate-800 text-white rounded-lg text-sm font-bold hover:bg-slate-900 disabled:opacity-50"
                  >
                    {saving ? 'Saving...' : 'Save Changes'}
                  </button>
                </div>
              </form>
            </div>
          )}

          {activeTab === 'preferences' && (
            <div className="max-w-2xl">
              <h2 className="text-xl font-bold text-slate-800 mb-6">Preferences</h2>
              <div className="space-y-6">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-3">Language</label>
                  <div className="grid grid-cols-2 gap-3">
                    {Object.entries(FLAGS).map(([lang, flag]) => (
                      <button
                        key={lang}
                        onClick={() => setLanguage(lang as Language)}
                        className={`p-4 border-2 rounded-lg text-center transition-all ${language === lang
                          ? 'border-primary-500 bg-primary-50'
                          : 'border-gray-200 hover:border-gray-300'
                          }`}
                      >
                        <div className="text-2xl mb-2">{flag}</div>
                        <div className="text-xs font-bold uppercase">{lang}</div>
                      </button>
                    ))}
                  </div>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-3">Currency</label>
                  <div className="grid grid-cols-4 gap-3">
                    {['USD', 'EUR', 'TRY', 'GBP'].map(curr => (
                      <button
                        key={curr}
                        onClick={() => setCurrency(curr as Currency)}
                        className={`p-4 border-2 rounded-lg text-center font-bold transition-all ${currency === curr
                          ? 'border-primary-500 bg-primary-50'
                          : 'border-gray-200 hover:border-gray-300'
                          }`}
                      >
                        {curr}
                      </button>
                    ))}
                  </div>
                </div>

                {/* Theme Selection */}
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-3">Theme</label>
                  <div className="grid grid-cols-3 gap-3">
                    <button
                      onClick={() => setTheme('light')}
                      className={`p-4 border-2 rounded-lg text-center transition-all ${theme === 'light'
                        ? 'border-primary-500 bg-primary-50 dark:bg-primary-900/20'
                        : 'border-gray-200 dark:border-gray-700 hover:border-gray-300'
                        }`}
                    >
                      <Sun className="w-6 h-6 mx-auto mb-2 text-amber-500" />
                      <div className="text-xs font-bold uppercase">Light</div>
                    </button>
                    <button
                      onClick={() => setTheme('dark')}
                      className={`p-4 border-2 rounded-lg text-center transition-all ${theme === 'dark'
                        ? 'border-primary-500 bg-primary-50 dark:bg-primary-900/20'
                        : 'border-gray-200 dark:border-gray-700 hover:border-gray-300'
                        }`}
                    >
                      <Moon className="w-6 h-6 mx-auto mb-2 text-indigo-500" />
                      <div className="text-xs font-bold uppercase">Dark</div>
                    </button>
                    <button
                      onClick={() => setTheme('system')}
                      className={`p-4 border-2 rounded-lg text-center transition-all ${theme === 'system'
                        ? 'border-primary-500 bg-primary-50 dark:bg-primary-900/20'
                        : 'border-gray-200 dark:border-gray-700 hover:border-gray-300'
                        }`}
                    >
                      <div className="flex justify-center gap-1 mb-2">
                        <Sun className="w-4 h-4 text-amber-500" />
                        <Moon className="w-4 h-4 text-indigo-500" />
                      </div>
                      <div className="text-xs font-bold uppercase">System</div>
                    </button>
                  </div>
                  <p className="text-xs text-gray-500 dark:text-gray-400 mt-2">
                    Currently using: {resolvedTheme} mode
                  </p>
                </div>
              </div>
            </div>
          )}

          {activeTab === 'notifications' && (
            <div className="max-w-2xl">
              <h2 className="text-xl font-bold text-slate-800 mb-6">Notification Preferences</h2>
              <div className="space-y-4">
                <div className="flex items-center justify-between p-4 bg-white border border-gray-200 rounded-lg">
                  <div>
                    <h3 className="font-semibold text-slate-800">Email Notifications</h3>
                    <p className="text-sm text-gray-500">Receive email notifications for important updates</p>
                  </div>
                  <input type="checkbox" defaultChecked className="w-5 h-5" />
                </div>
                <div className="flex items-center justify-between p-4 bg-white border border-gray-200 rounded-lg">
                  <div>
                    <h3 className="font-semibold text-slate-800">Task Reminders</h3>
                    <p className="text-sm text-gray-500">Get reminders for upcoming tasks</p>
                  </div>
                  <input type="checkbox" defaultChecked className="w-5 h-5" />
                </div>
                <div className="flex items-center justify-between p-4 bg-white border border-gray-200 rounded-lg">
                  <div>
                    <h3 className="font-semibold text-slate-800">Calendar Events</h3>
                    <p className="text-sm text-gray-500">Notifications for calendar events</p>
                  </div>
                  <input type="checkbox" defaultChecked className="w-5 h-5" />
                </div>
              </div>
            </div>
          )}

          {activeTab === 'security' && (
            <div className="max-w-2xl">
              <h2 className="text-xl font-bold text-slate-800 mb-6">Security Settings</h2>

              {/* Password Change */}
              <div className="bg-white border border-gray-200 rounded-xl p-6 mb-6">
                <h3 className="font-semibold text-slate-800 mb-4">Change Password</h3>
                <div className="space-y-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Current Password</label>
                    <input
                      type="password"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      placeholder="Enter current password"
                    />
                  </div>
                  <div className="grid grid-cols-2 gap-4">
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">New Password</label>
                      <input
                        type="password"
                        className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                        placeholder="Enter new password"
                      />
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">Confirm Password</label>
                      <input
                        type="password"
                        className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                        placeholder="Confirm new password"
                      />
                    </div>
                  </div>
                  <div className="flex justify-end">
                    <button className="px-4 py-2 bg-slate-800 text-white rounded-lg text-sm font-bold hover:bg-slate-900">
                      Update Password
                    </button>
                  </div>
                </div>
              </div>

              {/* Two-Factor Authentication */}
              <div className="bg-white border border-gray-200 rounded-xl p-6 mb-6">
                <div className="flex items-center justify-between">
                  <div>
                    <h3 className="font-semibold text-slate-800">Two-Factor Authentication</h3>
                    <p className="text-sm text-gray-500 mt-1">Add an extra layer of security to your account</p>
                  </div>
                  <span className={`text-xs font-semibold px-2 py-1 rounded-full ${mfaStatus.enabled ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'}`}>
                    {mfaStatus.enabled ? 'Enabled' : 'Not enabled'}
                  </span>
                </div>

                {!securityConfig.mfaEnforced && (
                  <div className="mt-4 flex items-start gap-3 p-4 bg-amber-50 border border-amber-200 rounded-lg">
                    <AlertTriangle className="w-5 h-5 text-amber-600 mt-0.5" />
                    <div>
                      <p className="text-sm font-semibold text-amber-800">MFA enforcement is paused</p>
                      <p className="text-xs text-amber-700">Login will not require verification codes until enforcement is re-enabled.</p>
                    </div>
                  </div>
                )}

                {mfaStatus.enabled ? (
                  <div className="mt-4 space-y-4">
                    <div className="flex items-start gap-3 p-4 bg-green-50 border border-green-200 rounded-lg">
                      <CheckCircle className="w-5 h-5 text-green-600 mt-0.5" />
                      <div>
                        <p className="text-sm font-semibold text-green-800">MFA is active</p>
                        <p className="text-xs text-green-700">Backup codes remaining: {mfaStatus.backupCodesRemaining}</p>
                      </div>
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-3 items-end">
                      <div className="md:col-span-2">
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Disable MFA (enter code)</label>
                        <input
                          value={mfaCode}
                          onChange={(e) => setMfaCode(e.target.value)}
                          disabled={!securityConfig.mfaEnforced}
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          placeholder="123456"
                        />
                      </div>
                      <button
                        onClick={handleDisableMfa}
                        disabled={securityLoading || !securityConfig.mfaEnforced}
                        className="px-4 py-2 bg-red-600 text-white rounded-lg text-sm font-bold hover:bg-red-700 disabled:opacity-50"
                      >
                        Disable MFA
                      </button>
                    </div>
                  </div>
                ) : (
                  <div className="mt-4 space-y-4">
                    <div className="flex items-start gap-3 p-4 bg-amber-50 border border-amber-200 rounded-lg">
                      <AlertTriangle className="w-5 h-5 text-amber-600 mt-0.5" />
                      <div>
                        <p className="text-sm font-semibold text-amber-800">MFA is not enabled</p>
                        <p className="text-xs text-amber-700">Use an authenticator app to generate verification codes.</p>
                      </div>
                    </div>
                    {!mfaSetup && (
                      <button
                        onClick={handleSetupMfa}
                        disabled={securityLoading || !securityConfig.mfaEnforced}
                        className="px-4 py-2 bg-slate-800 text-white rounded-lg text-sm font-bold hover:bg-slate-900 disabled:opacity-50"
                      >
                        Start MFA Setup
                      </button>
                    )}

                    {mfaSetup && (
                      <div className="space-y-4">
                        <div className="p-4 border border-gray-200 rounded-lg bg-gray-50">
                          <p className="text-xs text-gray-500 mb-2">Secret key</p>
                          <div className="flex items-center gap-2">
                            <code className="text-xs bg-white border border-gray-200 px-2 py-1 rounded">{mfaSetup.secret}</code>
                            <button
                              type="button"
                              onClick={() => handleCopy(mfaSetup.secret)}
                              className="text-xs text-gray-500 hover:text-gray-700"
                            >
                              <Copy className="w-4 h-4" />
                            </button>
                          </div>
                          <p className="text-xs text-gray-500 mt-2">otpauth URI</p>
                          <div className="flex items-center gap-2">
                            <code className="text-xs bg-white border border-gray-200 px-2 py-1 rounded truncate max-w-[320px]">{mfaSetup.otpauthUri}</code>
                            <button
                              type="button"
                              onClick={() => handleCopy(mfaSetup.otpauthUri)}
                              className="text-xs text-gray-500 hover:text-gray-700"
                            >
                              <Copy className="w-4 h-4" />
                            </button>
                          </div>
                        </div>
                        <div className="grid grid-cols-1 md:grid-cols-3 gap-3 items-end">
                          <div className="md:col-span-2">
                            <label className="block text-xs font-semibold text-gray-500 mb-1">Enter code to enable</label>
                            <input
                              value={mfaCode}
                              onChange={(e) => setMfaCode(e.target.value)}
                              disabled={!securityConfig.mfaEnforced}
                              className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                              placeholder="123456"
                            />
                          </div>
                          <button
                            onClick={handleEnableMfa}
                            disabled={securityLoading || !securityConfig.mfaEnforced}
                            className="px-4 py-2 bg-green-600 text-white rounded-lg text-sm font-bold hover:bg-green-700 disabled:opacity-50"
                          >
                            Enable MFA
                          </button>
                        </div>
                      </div>
                    )}

                    {backupCodes.length > 0 && (
                      <div className="p-4 bg-slate-50 border border-slate-200 rounded-lg">
                        <p className="text-sm font-semibold text-slate-800 mb-2">Backup codes (store securely)</p>
                        <div className="grid grid-cols-2 gap-2 text-xs text-slate-700">
                          {backupCodes.map(code => (
                            <span key={code} className="px-2 py-1 bg-white border border-slate-200 rounded">{code}</span>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                )}
              </div>

              {/* Session Settings */}
              <div className="bg-white border border-gray-200 rounded-xl p-6">
                <h3 className="font-semibold text-slate-800 mb-4">Session Settings</h3>
                <div className="space-y-4">
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <div className="p-4 bg-gray-50 rounded-lg">
                      <p className="text-xs text-gray-500 mb-1">Session timeout</p>
                      <p className="text-sm font-semibold text-slate-800">{securityConfig.sessionTimeoutMinutes} minutes</p>
                    </div>
                    <div className="p-4 bg-gray-50 rounded-lg">
                      <p className="text-xs text-gray-500 mb-1">Idle timeout</p>
                      <p className="text-sm font-semibold text-slate-800">{securityConfig.idleTimeoutMinutes} minutes</p>
                    </div>
                  </div>

                  <div className="flex items-center justify-between">
                    <h4 className="font-semibold text-slate-800">Active Sessions</h4>
                    <button
                      onClick={refreshSecurity}
                      className="flex items-center gap-2 text-xs text-gray-500 hover:text-gray-700"
                    >
                      <RefreshCw className={`w-4 h-4 ${securityLoading ? 'animate-spin' : ''}`} />
                      Refresh
                    </button>
                  </div>

                  {sessions.length === 0 ? (
                    <p className="text-sm text-gray-500">No active sessions found.</p>
                  ) : (
                    <div className="space-y-3">
                      {sessions.map((session: any) => (
                        <div key={session.id} className="p-3 border border-gray-200 rounded-lg flex items-center justify-between gap-3">
                          <div>
                            <p className="text-sm font-semibold text-slate-800">
                              {session.isCurrent ? 'Current session' : 'Session'}
                            </p>
                            <p className="text-xs text-gray-500">{session.ipAddress || 'Unknown IP'} • {session.userAgent || 'Unknown device'}</p>
                            <p className="text-xs text-gray-400">
                              Last active {session.lastSeenAt ? new Date(session.lastSeenAt).toLocaleString('en-US') : '-'}
                            </p>
                          </div>
                          {!session.isCurrent && (
                            <button
                              onClick={() => handleRevokeSession(session.id)}
                              disabled={revokingSessionId === session.id}
                              className="px-3 py-1 text-xs font-bold text-red-600 border border-red-200 rounded hover:bg-red-50 disabled:opacity-50"
                            >
                              Revoke
                            </button>
                          )}
                        </div>
                      ))}
                    </div>
                  )}

                  {securitySettings.auditLoggingEnabled && (
                    <div className="p-3 bg-emerald-50 border border-emerald-200 rounded-lg text-xs text-emerald-800">
                      Audit logging is active for compliance reporting.
                    </div>
                  )}
                </div>
              </div>
            </div>
          )}

          {/* Firm Info Tab - Admin Only */}
          {activeTab === 'firm' && (
            <div className="max-w-2xl">
              <h2 className="text-xl font-bold text-slate-800 mb-6">Firm Information</h2>
              <div className="bg-white border border-gray-200 rounded-xl p-6 space-y-6">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Firm Name</label>
                  <input
                    type="text"
                    className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                    value={firmSettings.firmName}
                    onChange={(e) => setFirmSettings({ ...firmSettings, firmName: e.target.value })}
                  />
                </div>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Tax ID (EIN)</label>
                    <input
                      type="text"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      placeholder="XX-XXXXXXX"
                      value={firmSettings.taxId}
                      onChange={(e) => setFirmSettings({ ...firmSettings, taxId: e.target.value })}
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">LEDES Firm ID</label>
                    <input
                      type="text"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      placeholder="For e-billing"
                      value={firmSettings.ledesFirmId}
                      onChange={(e) => setFirmSettings({ ...firmSettings, ledesFirmId: e.target.value })}
                    />
                  </div>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">Address</label>
                  <input
                    type="text"
                    className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                    value={firmSettings.address}
                    onChange={(e) => setFirmSettings({ ...firmSettings, address: e.target.value })}
                  />
                </div>
                <div className="grid grid-cols-3 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">City</label>
                    <input
                      type="text"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={firmSettings.city}
                      onChange={(e) => setFirmSettings({ ...firmSettings, city: e.target.value })}
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">State</label>
                    <input
                      type="text"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={firmSettings.state}
                      onChange={(e) => setFirmSettings({ ...firmSettings, state: e.target.value })}
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">ZIP Code</label>
                    <input
                      type="text"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={firmSettings.zipCode}
                      onChange={(e) => setFirmSettings({ ...firmSettings, zipCode: e.target.value })}
                    />
                  </div>
                </div>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Phone</label>
                    <input
                      type="tel"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={firmSettings.phone}
                      onChange={(e) => setFirmSettings({ ...firmSettings, phone: e.target.value })}
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Website</label>
                    <input
                      type="url"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      placeholder="https://"
                      value={firmSettings.website || ''}
                      onChange={(e) => setFirmSettings({ ...firmSettings, website: e.target.value })}
                    />
                  </div>
                </div>
                <div className="flex justify-end pt-4 border-t">
                  <button
                    onClick={handleSaveFirmSettings}
                    disabled={firmSaving || firmLoading}
                    className="px-6 py-2.5 bg-slate-800 text-white rounded-lg text-sm font-bold hover:bg-slate-900 disabled:opacity-50"
                  >
                    {firmSaving ? 'Saving...' : 'Save Changes'}
                  </button>
                </div>
              </div>
            </div>
          )}

          {/* Offices & Entities Tab - Admin Only */}
          {activeTab === 'organization' && (
            <div className="space-y-6">
              <div className="flex items-start justify-between">
                <div>
                  <h2 className="text-xl font-bold text-slate-800">Offices & Entities</h2>
                  <p className="text-sm text-gray-500 mt-1">Configure legal entities and office locations for multi-office operations.</p>
                </div>
                <button
                  onClick={openCreateEntity}
                  className="px-4 py-2 text-sm font-semibold text-white bg-slate-800 rounded-lg hover:bg-slate-900"
                >
                  + Add Entity
                </button>
              </div>

              <div className="grid grid-cols-1 xl:grid-cols-3 gap-6">
                <div className="bg-white border border-gray-200 rounded-xl p-4">
                  <div className="flex items-center justify-between mb-4">
                    <h3 className="font-semibold text-slate-800">Entities</h3>
                    {entitiesLoading && <span className="text-xs text-gray-500">Loading...</span>}
                  </div>
                  {entities.length === 0 ? (
                    <p className="text-sm text-gray-500">No entities configured yet.</p>
                  ) : (
                    <div className="space-y-3">
                      {entities.map(entity => (
                        <button
                          key={entity.id}
                          onClick={() => {
                            setSelectedEntity(entity);
                            loadOffices(entity.id);
                          }}
                          className={`w-full text-left border rounded-lg p-3 transition ${selectedEntity?.id === entity.id ? 'border-slate-700 bg-slate-50' : 'border-gray-200 hover:border-slate-400'}`}
                        >
                          <div className="flex items-start justify-between gap-2">
                            <div>
                              <p className="text-sm font-semibold text-slate-800">{entity.name}</p>
                              <p className="text-xs text-gray-500">{entity.legalName || 'Legal name not set'}</p>
                            </div>
                            <div className="flex flex-col items-end gap-1">
                              {entity.isDefault && (
                                <span className="text-[11px] font-semibold px-2 py-0.5 rounded-full bg-emerald-100 text-emerald-700">Default</span>
                              )}
                              <span className={`text-[11px] font-semibold px-2 py-0.5 rounded-full ${entity.isActive ? 'bg-blue-100 text-blue-700' : 'bg-gray-100 text-gray-500'}`}>
                                {entity.isActive ? 'Active' : 'Inactive'}
                              </span>
                            </div>
                          </div>
                          <div className="mt-3 flex flex-wrap items-center gap-2">
                            <button
                              onClick={(e) => { e.stopPropagation(); openEditEntity(entity); }}
                              className="px-2.5 py-1 text-xs font-semibold text-slate-700 border border-gray-200 rounded hover:bg-white"
                            >
                              Edit
                            </button>
                            {!entity.isDefault && (
                              <button
                                onClick={(e) => { e.stopPropagation(); handleSetDefaultEntity(entity); }}
                                className="px-2.5 py-1 text-xs font-semibold text-emerald-700 border border-emerald-200 rounded hover:bg-emerald-50"
                              >
                                Set Default
                              </button>
                            )}
                            <button
                              onClick={(e) => { e.stopPropagation(); handleToggleEntityActive(entity); }}
                              className="px-2.5 py-1 text-xs font-semibold text-gray-600 border border-gray-200 rounded hover:bg-gray-50"
                            >
                              {entity.isActive ? 'Deactivate' : 'Activate'}
                            </button>
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                if (window.confirm('Remove this entity and its offices?')) {
                                  handleDeleteEntity(entity);
                                }
                              }}
                              className="px-2.5 py-1 text-xs font-semibold text-red-600 border border-red-200 rounded hover:bg-red-50"
                            >
                              Remove
                            </button>
                          </div>
                        </button>
                      ))}
                    </div>
                  )}
                </div>

                <div className="xl:col-span-2 bg-white border border-gray-200 rounded-xl p-4">
                  <div className="flex items-start justify-between mb-4">
                    <div>
                      <h3 className="font-semibold text-slate-800">Offices</h3>
                      <p className="text-xs text-gray-500">
                        {selectedEntity ? `${selectedEntity.name} offices` : 'Select an entity to manage offices.'}
                      </p>
                    </div>
                    <button
                      onClick={openCreateOffice}
                      disabled={!selectedEntity}
                      className="px-3 py-2 text-xs font-semibold text-white bg-slate-800 rounded-lg hover:bg-slate-900 disabled:opacity-50"
                    >
                      + Add Office
                    </button>
                  </div>

                  {officesLoading ? (
                    <p className="text-sm text-gray-500">Loading offices...</p>
                  ) : !selectedEntity ? (
                    <p className="text-sm text-gray-500">Choose an entity from the list to view offices.</p>
                  ) : offices.length === 0 ? (
                    <p className="text-sm text-gray-500">No offices configured for this entity.</p>
                  ) : (
                    <div className="space-y-3">
                      {offices.map(office => (
                        <div key={office.id} className="border border-gray-200 rounded-lg p-4">
                          <div className="flex items-start justify-between gap-2">
                            <div>
                              <p className="text-sm font-semibold text-slate-800">{office.name}</p>
                              <p className="text-xs text-gray-500">
                                {office.city || office.state ? `${office.city || ''}${office.city && office.state ? ', ' : ''}${office.state || ''}` : 'Location not set'}
                              </p>
                              <p className="text-xs text-gray-400 mt-1">{office.timeZone || 'Time zone not set'}</p>
                            </div>
                            <div className="flex flex-col items-end gap-1">
                              {office.isDefault && (
                                <span className="text-[11px] font-semibold px-2 py-0.5 rounded-full bg-emerald-100 text-emerald-700">Default</span>
                              )}
                              <span className={`text-[11px] font-semibold px-2 py-0.5 rounded-full ${office.isActive ? 'bg-blue-100 text-blue-700' : 'bg-gray-100 text-gray-500'}`}>
                                {office.isActive ? 'Active' : 'Inactive'}
                              </span>
                            </div>
                          </div>
                          <div className="mt-3 flex flex-wrap items-center gap-2">
                            <button
                              onClick={() => openEditOffice(office)}
                              className="px-2.5 py-1 text-xs font-semibold text-slate-700 border border-gray-200 rounded hover:bg-white"
                            >
                              Edit
                            </button>
                            {!office.isDefault && (
                              <button
                                onClick={() => handleSetDefaultOffice(office)}
                                className="px-2.5 py-1 text-xs font-semibold text-emerald-700 border border-emerald-200 rounded hover:bg-emerald-50"
                              >
                                Set Default
                              </button>
                            )}
                            <button
                              onClick={() => handleToggleOfficeActive(office)}
                              className="px-2.5 py-1 text-xs font-semibold text-gray-600 border border-gray-200 rounded hover:bg-gray-50"
                            >
                              {office.isActive ? 'Deactivate' : 'Activate'}
                            </button>
                            <button
                              onClick={() => {
                                if (window.confirm('Remove this office?')) {
                                  handleDeleteOffice(office);
                                }
                              }}
                              className="px-2.5 py-1 text-xs font-semibold text-red-600 border border-red-200 rounded hover:bg-red-50"
                            >
                              Remove
                            </button>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>

              {entityModalOpen && (
                <div className="fixed inset-0 z-50 bg-black/40 flex items-start sm:items-center justify-center overflow-y-auto p-4">
                  <div className="bg-white rounded-xl shadow-xl w-full max-w-2xl p-6 my-4 max-h-[calc(100vh-2rem)] overflow-y-auto">
                    <div className="flex items-start justify-between mb-4">
                      <div>
                        <h3 className="text-lg font-bold text-slate-900">{editingEntity ? 'Edit Entity' : 'Add Entity'}</h3>
                        <p className="text-xs text-gray-500 mt-1">Use legal entity details for billing and tax compliance.</p>
                      </div>
                      <button onClick={closeEntityModal} className="text-gray-400 hover:text-gray-600">
                        <X className="w-4 h-4" />
                      </button>
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Entity Name *</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={entityForm.name}
                          onChange={(e) => setEntityForm(prev => ({ ...prev, name: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Legal Name</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={entityForm.legalName}
                          onChange={(e) => setEntityForm(prev => ({ ...prev, legalName: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Tax ID (EIN)</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          placeholder="XX-XXXXXXX"
                          value={entityForm.taxId}
                          onChange={(e) => setEntityForm(prev => ({ ...prev, taxId: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Website</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          placeholder="https://"
                          value={entityForm.website}
                          onChange={(e) => setEntityForm(prev => ({ ...prev, website: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Email</label>
                        <input
                          type="email"
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={entityForm.email}
                          onChange={(e) => setEntityForm(prev => ({ ...prev, email: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Phone</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={entityForm.phone}
                          onChange={(e) => setEntityForm(prev => ({ ...prev, phone: e.target.value }))}
                        />
                      </div>
                      <div className="md:col-span-2">
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Address</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={entityForm.address}
                          onChange={(e) => setEntityForm(prev => ({ ...prev, address: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">City</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={entityForm.city}
                          onChange={(e) => setEntityForm(prev => ({ ...prev, city: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">State</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={entityForm.state}
                          onChange={(e) => setEntityForm(prev => ({ ...prev, state: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Zip Code</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={entityForm.zipCode}
                          onChange={(e) => setEntityForm(prev => ({ ...prev, zipCode: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Country</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={entityForm.country}
                          onChange={(e) => setEntityForm(prev => ({ ...prev, country: e.target.value }))}
                        />
                      </div>
                      <div className="md:col-span-2 flex items-center justify-between bg-gray-50 border border-gray-200 rounded-lg p-3">
                        <div>
                          <p className="text-xs font-semibold text-gray-600">Set as default entity</p>
                          <p className="text-[11px] text-gray-400">Used for new matters and invoices.</p>
                        </div>
                        <button
                          onClick={() => setEntityForm(prev => ({ ...prev, isDefault: !prev.isDefault }))}
                          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${entityForm.isDefault ? 'bg-emerald-500' : 'bg-gray-300'}`}
                        >
                          <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${entityForm.isDefault ? 'translate-x-6' : 'translate-x-1'}`} />
                        </button>
                      </div>
                      <div className="md:col-span-2 flex items-center justify-between bg-gray-50 border border-gray-200 rounded-lg p-3">
                        <div>
                          <p className="text-xs font-semibold text-gray-600">Entity status</p>
                          <p className="text-[11px] text-gray-400">Inactive entities will not appear in selections.</p>
                        </div>
                        <button
                          onClick={() => setEntityForm(prev => ({ ...prev, isActive: !prev.isActive }))}
                          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${entityForm.isActive ? 'bg-emerald-500' : 'bg-gray-300'}`}
                        >
                          <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${entityForm.isActive ? 'translate-x-6' : 'translate-x-1'}`} />
                        </button>
                      </div>
                    </div>
                    <div className="flex items-center justify-end gap-2 mt-6">
                      <button
                        onClick={closeEntityModal}
                        className="px-4 py-2 text-sm font-semibold text-gray-600 hover:text-gray-800"
                      >
                        Cancel
                      </button>
                      <button
                        onClick={handleSaveEntity}
                        disabled={entitiesSaving}
                        className="px-4 py-2 text-sm font-semibold text-white bg-slate-800 rounded-lg hover:bg-slate-900 disabled:opacity-50"
                      >
                        {entitiesSaving ? 'Saving...' : 'Save Entity'}
                      </button>
                    </div>
                  </div>
                </div>
              )}

              {officeModalOpen && (
                <div className="fixed inset-0 z-50 bg-black/40 flex items-start sm:items-center justify-center overflow-y-auto p-4">
                  <div className="bg-white rounded-xl shadow-xl w-full max-w-2xl p-6 my-4 max-h-[calc(100vh-2rem)] overflow-y-auto">
                    <div className="flex items-start justify-between mb-4">
                      <div>
                        <h3 className="text-lg font-bold text-slate-900">{editingOffice ? 'Edit Office' : 'Add Office'}</h3>
                        <p className="text-xs text-gray-500 mt-1">Office details power scheduling, conflicts, and compliance notices.</p>
                      </div>
                      <button onClick={closeOfficeModal} className="text-gray-400 hover:text-gray-600">
                        <X className="w-4 h-4" />
                      </button>
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Office Name *</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={officeForm.name}
                          onChange={(e) => setOfficeForm(prev => ({ ...prev, name: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Office Code</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={officeForm.code}
                          onChange={(e) => setOfficeForm(prev => ({ ...prev, code: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Email</label>
                        <input
                          type="email"
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={officeForm.email}
                          onChange={(e) => setOfficeForm(prev => ({ ...prev, email: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Phone</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={officeForm.phone}
                          onChange={(e) => setOfficeForm(prev => ({ ...prev, phone: e.target.value }))}
                        />
                      </div>
                      <div className="md:col-span-2">
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Address</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={officeForm.address}
                          onChange={(e) => setOfficeForm(prev => ({ ...prev, address: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">City</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={officeForm.city}
                          onChange={(e) => setOfficeForm(prev => ({ ...prev, city: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">State</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={officeForm.state}
                          onChange={(e) => setOfficeForm(prev => ({ ...prev, state: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Zip Code</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={officeForm.zipCode}
                          onChange={(e) => setOfficeForm(prev => ({ ...prev, zipCode: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Country</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={officeForm.country}
                          onChange={(e) => setOfficeForm(prev => ({ ...prev, country: e.target.value }))}
                        />
                      </div>
                      <div className="md:col-span-2">
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Time Zone</label>
                        <select
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          value={officeForm.timeZone}
                          onChange={(e) => setOfficeForm(prev => ({ ...prev, timeZone: e.target.value }))}
                        >
                          {timeZoneOptions.map(option => (
                            <option key={option.value} value={option.value}>{option.label}</option>
                          ))}
                        </select>
                      </div>
                      <div className="md:col-span-2 flex items-center justify-between bg-gray-50 border border-gray-200 rounded-lg p-3">
                        <div>
                          <p className="text-xs font-semibold text-gray-600">Set as default office</p>
                          <p className="text-[11px] text-gray-400">Used for scheduling and notices.</p>
                        </div>
                        <button
                          onClick={() => setOfficeForm(prev => ({ ...prev, isDefault: !prev.isDefault }))}
                          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${officeForm.isDefault ? 'bg-emerald-500' : 'bg-gray-300'}`}
                        >
                          <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${officeForm.isDefault ? 'translate-x-6' : 'translate-x-1'}`} />
                        </button>
                      </div>
                      <div className="md:col-span-2 flex items-center justify-between bg-gray-50 border border-gray-200 rounded-lg p-3">
                        <div>
                          <p className="text-xs font-semibold text-gray-600">Office status</p>
                          <p className="text-[11px] text-gray-400">Inactive offices are hidden from scheduling.</p>
                        </div>
                        <button
                          onClick={() => setOfficeForm(prev => ({ ...prev, isActive: !prev.isActive }))}
                          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${officeForm.isActive ? 'bg-emerald-500' : 'bg-gray-300'}`}
                        >
                          <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${officeForm.isActive ? 'translate-x-6' : 'translate-x-1'}`} />
                        </button>
                      </div>
                    </div>
                    <div className="flex items-center justify-end gap-2 mt-6">
                      <button
                        onClick={closeOfficeModal}
                        className="px-4 py-2 text-sm font-semibold text-gray-600 hover:text-gray-800"
                      >
                        Cancel
                      </button>
                      <button
                        onClick={handleSaveOffice}
                        disabled={entitiesSaving}
                        className="px-4 py-2 text-sm font-semibold text-white bg-slate-800 rounded-lg hover:bg-slate-900 disabled:opacity-50"
                      >
                        {entitiesSaving ? 'Saving...' : 'Save Office'}
                      </button>
                    </div>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* Billing Tab - Admin Only */}
          {activeTab === 'billing' && (
            <div className="max-w-2xl">
              <h2 className="text-xl font-bold text-slate-800 mb-6">Billing & Rates</h2>

              {/* Default Rates */}
              <div className="bg-white border border-gray-200 rounded-xl p-6 mb-6">
                <h3 className="font-semibold text-slate-800 mb-4">Default Hourly Rates</h3>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Partner Rate</label>
                    <div className="relative">
                      <span className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500">$</span>
                      <input
                        type="number"
                        className="w-full border border-gray-300 rounded-lg p-2.5 pl-7 text-sm"
                        value={billingSettings.partnerRate}
                        onChange={(e) => setBillingSettings({ ...billingSettings, partnerRate: parseInt(e.target.value) || 0 })}
                      />
                    </div>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Associate Rate</label>
                    <div className="relative">
                      <span className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500">$</span>
                      <input
                        type="number"
                        className="w-full border border-gray-300 rounded-lg p-2.5 pl-7 text-sm"
                        value={billingSettings.associateRate}
                        onChange={(e) => setBillingSettings({ ...billingSettings, associateRate: parseInt(e.target.value) || 0 })}
                      />
                    </div>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Paralegal Rate</label>
                    <div className="relative">
                      <span className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500">$</span>
                      <input
                        type="number"
                        className="w-full border border-gray-300 rounded-lg p-2.5 pl-7 text-sm"
                        value={billingSettings.paralegalRate}
                        onChange={(e) => setBillingSettings({ ...billingSettings, paralegalRate: parseInt(e.target.value) || 0 })}
                      />
                    </div>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Default Rate</label>
                    <div className="relative">
                      <span className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500">$</span>
                      <input
                        type="number"
                        className="w-full border border-gray-300 rounded-lg p-2.5 pl-7 text-sm"
                        value={billingSettings.defaultHourlyRate}
                        onChange={(e) => setBillingSettings({ ...billingSettings, defaultHourlyRate: parseInt(e.target.value) || 0 })}
                      />
                    </div>
                  </div>
                </div>
              </div>

              {/* Time Entry Rules */}
              <div className="bg-white border border-gray-200 rounded-xl p-6 mb-6">
                <h3 className="font-semibold text-slate-800 mb-4">Time Entry Rules</h3>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Billing Increment</label>
                    <select
                      value={billingSettings.billingIncrement}
                      onChange={(e) => setBillingSettings({ ...billingSettings, billingIncrement: parseInt(e.target.value) as 6 | 10 | 15 })}
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                    >
                      <option value={6}>6 minutes (0.1 hr)</option>
                      <option value={10}>10 minutes</option>
                      <option value={15}>15 minutes (0.25 hr)</option>
                    </select>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Rounding</label>
                    <select
                      value={billingSettings.roundingRule}
                      onChange={(e) => setBillingSettings({ ...billingSettings, roundingRule: e.target.value as 'up' | 'down' | 'nearest' })}
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                    >
                      <option value="up">Round Up</option>
                      <option value="down">Round Down</option>
                      <option value="nearest">Round to Nearest</option>
                    </select>
                  </div>
                </div>
              </div>

              {/* Invoice Settings */}
              <div className="bg-white border border-gray-200 rounded-xl p-6 mb-6">
                <h3 className="font-semibold text-slate-800 mb-4">Invoice Defaults</h3>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Payment Terms</label>
                    <select
                      value={billingSettings.defaultPaymentTerms}
                      onChange={(e) => setBillingSettings({ ...billingSettings, defaultPaymentTerms: parseInt(e.target.value) })}
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                    >
                      <option value={14}>Net 14</option>
                      <option value={30}>Net 30</option>
                      <option value={45}>Net 45</option>
                      <option value={60}>Net 60</option>
                    </select>
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Invoice Prefix</label>
                    <input
                      type="text"
                      className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                      value={billingSettings.invoicePrefix}
                      onChange={(e) => setBillingSettings({ ...billingSettings, invoicePrefix: e.target.value })}
                    />
                  </div>
                </div>
              </div>

              {/* LEDES/UTBMS */}
              <div className="bg-white border border-gray-200 rounded-xl p-6 mb-6">
                <h3 className="font-semibold text-slate-800 mb-4">E-Billing (LEDES)</h3>
                <div className="space-y-4">
                  <div className="flex items-center justify-between p-4 bg-gray-50 rounded-lg">
                    <div>
                      <h4 className="font-medium text-slate-700">Enable LEDES Export</h4>
                      <p className="text-xs text-gray-500">Generate LEDES 1998B formatted invoices</p>
                    </div>
                    <button
                      onClick={() => setBillingSettings({ ...billingSettings, ledesEnabled: !billingSettings.ledesEnabled })}
                      className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${billingSettings.ledesEnabled ? 'bg-green-500' : 'bg-gray-300'}`}
                    >
                      <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${billingSettings.ledesEnabled ? 'translate-x-6' : 'translate-x-1'}`} />
                    </button>
                  </div>
                  <div className="flex items-center justify-between p-4 bg-gray-50 rounded-lg">
                    <div>
                      <h4 className="font-medium text-slate-700">Require UTBMS Codes</h4>
                      <p className="text-xs text-gray-500">Require activity codes on time entries</p>
                    </div>
                    <button
                      onClick={() => setBillingSettings({ ...billingSettings, utbmsCodesRequired: !billingSettings.utbmsCodesRequired })}
                      className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${billingSettings.utbmsCodesRequired ? 'bg-green-500' : 'bg-gray-300'}`}
                    >
                      <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${billingSettings.utbmsCodesRequired ? 'translate-x-6' : 'translate-x-1'}`} />
                    </button>
                  </div>
                </div>
              </div>

              {/* Trust Account */}
              <div className="bg-white border border-gray-200 rounded-xl p-6">
                <h3 className="font-semibold text-slate-800 mb-4">Trust Account (IOLTA)</h3>
                <div className="space-y-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1">Evergreen Retainer Minimum</label>
                    <div className="relative">
                      <span className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500">$</span>
                      <input
                        type="number"
                        className="w-full border border-gray-300 rounded-lg p-2.5 pl-7 text-sm"
                        value={billingSettings.evergreenRetainerMinimum}
                        onChange={(e) => setBillingSettings({ ...billingSettings, evergreenRetainerMinimum: parseInt(e.target.value) || 0 })}
                      />
                    </div>
                    <p className="text-xs text-gray-500 mt-1">Alert when client trust balance falls below this amount</p>
                  </div>
                  <div className="flex items-center justify-between p-4 bg-gray-50 rounded-lg">
                    <div>
                      <h4 className="font-medium text-slate-700">Trust Balance Alerts</h4>
                      <p className="text-xs text-gray-500">Email notifications for low balances</p>
                    </div>
                    <button
                      onClick={() => setBillingSettings({ ...billingSettings, trustBalanceAlerts: !billingSettings.trustBalanceAlerts })}
                      className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${billingSettings.trustBalanceAlerts ? 'bg-green-500' : 'bg-gray-300'}`}
                    >
                      <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${billingSettings.trustBalanceAlerts ? 'translate-x-6' : 'translate-x-1'}`} />
                    </button>
                  </div>
                </div>
                <div className="flex justify-end pt-4 mt-4 border-t">
                  <button
                    onClick={handleSaveBillingSettings}
                    disabled={billingSaving || billingLoading}
                    className="px-6 py-2.5 bg-slate-800 text-white rounded-lg text-sm font-bold hover:bg-slate-900 disabled:opacity-50"
                  >
                    {billingSaving ? 'Saving...' : 'Save Changes'}
                  </button>
                </div>
              </div>
            </div>
          )}

          {/* Integrations Tab - Admin Only */}
          {activeTab === 'integrations' && (
            <div className="space-y-6">
              <div>
                <h2 className="text-xl font-bold text-slate-800 mb-1">Integrations</h2>
                <p className="text-sm text-gray-500">Connect accounting, calendar, docket, and e-filing platforms with verified credentials.</p>
              </div>

              <div className="bg-white border border-gray-200 rounded-xl p-6">
                <div className="flex items-start justify-between">
                  <div>
                    <h3 className="font-semibold text-slate-800">Accounting & Payments</h3>
                    <p className="text-xs text-gray-500">Sync invoices, payments, and settlements with your ledger.</p>
                  </div>
                  {integrationsLoading && <span className="text-xs text-gray-500">Loading...</span>}
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mt-4">
                  {integrationCatalog.filter(item => item.category === 'Accounting' || item.category === 'Payments').map(item => {
                    const integration = findIntegration(item.provider, item.category, item.providerKey);
                    const badge = getIntegrationBadge(integration?.status);
                    const isConnected = integration?.status === 'connected';
                    return (
                      <div key={`${item.category}-${item.providerKey || item.provider}`} className="border border-gray-200 rounded-lg p-4 bg-gray-50/60">
                        <div className="flex items-start justify-between gap-2">
                          <div>
                            <p className="text-sm font-semibold text-slate-800">{item.provider}</p>
                            <p className="text-xs text-gray-500 mt-1">{item.description}</p>
                            {item.webhookFirst && (
                              <p className="text-[11px] text-emerald-700 mt-1">
                                Webhook-first, polling fallback {item.fallbackPollingMinutes || 360}m
                              </p>
                            )}
                          </div>
                          <span className={`text-[11px] font-semibold px-2 py-1 rounded-full ${badge.className}`}>{badge.label}</span>
                        </div>
                        {integration && (
                          <div className="mt-3 text-xs text-gray-500 space-y-1">
                            <div>Account: {integration.accountLabel || integration.accountEmail || 'Configured'}</div>
                            <div>Last sync: {integration.lastSyncAt ? new Date(integration.lastSyncAt).toLocaleString('en-US') : 'Not synced yet'}</div>
                            {integration.lastWebhookAt && (
                              <div>Last webhook: {new Date(integration.lastWebhookAt).toLocaleString('en-US')}</div>
                            )}
                          </div>
                        )}
                        <div className="mt-3 flex flex-wrap items-center gap-2">
                          {isConnected ? (
                            <>
                              <button
                                onClick={() => integration && handleSyncIntegration(integration)}
                                disabled={integrationsSaving}
                                className="px-3 py-1 text-xs font-semibold text-slate-700 border border-gray-300 rounded hover:bg-white disabled:opacity-50"
                              >
                                Sync Now
                              </button>
                              <button
                                onClick={() => integration && handleDisconnectIntegration(integration)}
                                disabled={integrationsSaving}
                                className="px-3 py-1 text-xs font-semibold text-red-600 border border-red-200 rounded hover:bg-red-50 disabled:opacity-50"
                              >
                                Disconnect
                              </button>
                            </>
                          ) : (
                            <button
                              onClick={() => handleConnectIntegration(item)}
                              className="px-3 py-1 text-xs font-semibold text-white bg-slate-800 rounded hover:bg-slate-900"
                            >
                              Connect
                            </button>
                          )}
                          {integration && (
                            <button
                              onClick={() => integration && handleToggleIntegrationSync(integration)}
                              disabled={integrationsSaving}
                              className={`px-3 py-1 text-xs font-semibold border rounded ${integration.syncEnabled ? 'border-emerald-200 text-emerald-700 bg-emerald-50' : 'border-gray-200 text-gray-600 bg-white'} disabled:opacity-50`}
                            >
                              Auto-sync {integration.syncEnabled ? 'On' : 'Off'}
                            </button>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>

              <div className="bg-white border border-gray-200 rounded-xl p-6">
                <div className="flex items-start justify-between">
                  <div>
                    <h3 className="font-semibold text-slate-800">Calendar</h3>
                    <p className="text-xs text-gray-500">Keep deadlines, hearings, and appointments aligned.</p>
                  </div>
                  {integrationsLoading && <span className="text-xs text-gray-500">Loading...</span>}
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mt-4">
                  {integrationCatalog.filter(item => item.category === 'Calendar').map(item => {
                    const integration = findIntegration(item.provider, item.category, item.providerKey);
                    const badge = getIntegrationBadge(integration?.status);
                    const isConnected = integration?.status === 'connected';
                    return (
                      <div key={`${item.category}-${item.providerKey || item.provider}`} className="border border-gray-200 rounded-lg p-4 bg-gray-50/60">
                        <div className="flex items-start justify-between gap-2">
                          <div>
                            <p className="text-sm font-semibold text-slate-800">{item.provider}</p>
                            <p className="text-xs text-gray-500 mt-1">{item.description}</p>
                            {item.webhookFirst && (
                              <p className="text-[11px] text-emerald-700 mt-1">
                                Webhook-first, polling fallback {item.fallbackPollingMinutes || 360}m
                              </p>
                            )}
                          </div>
                          <span className={`text-[11px] font-semibold px-2 py-1 rounded-full ${badge.className}`}>{badge.label}</span>
                        </div>
                        {integration && (
                          <div className="mt-3 text-xs text-gray-500 space-y-1">
                            <div>Calendar: {integration.accountLabel || integration.accountEmail || 'Primary calendar'}</div>
                            <div>Last sync: {integration.lastSyncAt ? new Date(integration.lastSyncAt).toLocaleString('en-US') : 'Not synced yet'}</div>
                            {integration.lastWebhookAt && (
                              <div>Last webhook: {new Date(integration.lastWebhookAt).toLocaleString('en-US')}</div>
                            )}
                          </div>
                        )}
                        <div className="mt-3 flex flex-wrap items-center gap-2">
                          {isConnected ? (
                            <>
                              <button
                                onClick={() => integration && handleSyncIntegration(integration)}
                                disabled={integrationsSaving}
                                className="px-3 py-1 text-xs font-semibold text-slate-700 border border-gray-300 rounded hover:bg-white disabled:opacity-50"
                              >
                                Sync Now
                              </button>
                              <button
                                onClick={() => integration && handleDisconnectIntegration(integration)}
                                disabled={integrationsSaving}
                                className="px-3 py-1 text-xs font-semibold text-red-600 border border-red-200 rounded hover:bg-red-50 disabled:opacity-50"
                              >
                                Disconnect
                              </button>
                            </>
                          ) : (
                            <button
                              onClick={() => handleConnectIntegration(item)}
                              className="px-3 py-1 text-xs font-semibold text-white bg-slate-800 rounded hover:bg-slate-900"
                            >
                              Connect
                            </button>
                          )}
                          {integration && (
                            <button
                              onClick={() => integration && handleToggleIntegrationSync(integration)}
                              disabled={integrationsSaving}
                              className={`px-3 py-1 text-xs font-semibold border rounded ${integration.syncEnabled ? 'border-emerald-200 text-emerald-700 bg-emerald-50' : 'border-gray-200 text-gray-600 bg-white'} disabled:opacity-50`}
                            >
                              Auto-sync {integration.syncEnabled ? 'On' : 'Off'}
                            </button>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>

              <div className="bg-white border border-gray-200 rounded-xl p-6">
                <div className="flex items-start justify-between">
                  <div>
                    <h3 className="font-semibold text-slate-800">Email, Court Docket & E-Filing</h3>
                    <p className="text-xs text-gray-500">Connect matter-linked mailboxes, U.S. dockets, and filing status feeds.</p>
                  </div>
                  {integrationsLoading && <span className="text-xs text-gray-500">Loading...</span>}
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mt-4">
                  {integrationCatalog
                    .filter(item => item.category === 'Email' || item.category === 'Court Docket' || item.category === 'E-Filing')
                    .map(item => {
                      const integration = findIntegration(item.provider, item.category, item.providerKey);
                      const badge = getIntegrationBadge(integration?.status);
                      const isConnected = integration?.status === 'connected';
                      return (
                        <div key={`${item.category}-${item.providerKey || item.provider}`} className="border border-gray-200 rounded-lg p-4 bg-gray-50/60">
                          <div className="flex items-start justify-between gap-2">
                            <div>
                              <p className="text-sm font-semibold text-slate-800">{item.provider}</p>
                              <p className="text-xs text-gray-500 mt-1">{item.description}</p>
                              {item.webhookFirst && (
                                <p className="text-[11px] text-emerald-700 mt-1">
                                  Webhook-first, polling fallback {item.fallbackPollingMinutes || 360}m
                                </p>
                              )}
                            </div>
                            <span className={`text-[11px] font-semibold px-2 py-1 rounded-full ${badge.className}`}>{badge.label}</span>
                          </div>
                          {integration && (
                            <div className="mt-3 text-xs text-gray-500 space-y-1">
                              <div>Account: {integration.accountLabel || integration.accountEmail || 'Configured'}</div>
                              <div>Last sync: {integration.lastSyncAt ? new Date(integration.lastSyncAt).toLocaleString('en-US') : 'Not synced yet'}</div>
                              {integration.lastWebhookAt && (
                                <div>Last webhook: {new Date(integration.lastWebhookAt).toLocaleString('en-US')}</div>
                              )}
                            </div>
                          )}
                          <div className="mt-3 flex flex-wrap items-center gap-2">
                            {isConnected ? (
                              <>
                                <button
                                  onClick={() => integration && handleSyncIntegration(integration)}
                                  disabled={integrationsSaving}
                                  className="px-3 py-1 text-xs font-semibold text-slate-700 border border-gray-300 rounded hover:bg-white disabled:opacity-50"
                                >
                                  Sync Now
                                </button>
                                <button
                                  onClick={() => integration && handleDisconnectIntegration(integration)}
                                  disabled={integrationsSaving}
                                  className="px-3 py-1 text-xs font-semibold text-red-600 border border-red-200 rounded hover:bg-red-50 disabled:opacity-50"
                                >
                                  Disconnect
                                </button>
                              </>
                            ) : (
                              <button
                                onClick={() => handleConnectIntegration(item)}
                                className="px-3 py-1 text-xs font-semibold text-white bg-slate-800 rounded hover:bg-slate-900"
                              >
                                Connect
                              </button>
                            )}
                            {integration && (
                              <button
                                onClick={() => integration && handleToggleIntegrationSync(integration)}
                                disabled={integrationsSaving}
                                className={`px-3 py-1 text-xs font-semibold border rounded ${integration.syncEnabled ? 'border-emerald-200 text-emerald-700 bg-emerald-50' : 'border-gray-200 text-gray-600 bg-white'} disabled:opacity-50`}
                              >
                                Auto-sync {integration.syncEnabled ? 'On' : 'Off'}
                              </button>
                            )}
                          </div>
                        </div>
                      );
                    })}
                </div>
              </div>

              <div className="bg-white border border-gray-200 rounded-xl p-6">
                <div className="flex items-start justify-between flex-wrap gap-3">
                  <div>
                    <h3 className="font-semibold text-slate-800">Email Sync</h3>
                    <p className="text-xs text-gray-500">Connect Gmail or Outlook for matter-linked email tracking.</p>
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => { setEmailConnectProvider('Gmail'); setEmailConnectOpen(true); }}
                      className="px-3 py-1 text-xs font-semibold text-white bg-red-500 rounded hover:bg-red-600"
                    >
                      Connect Gmail
                    </button>
                    <button
                      onClick={() => { setEmailConnectProvider('Outlook'); setEmailConnectOpen(true); }}
                      className="px-3 py-1 text-xs font-semibold text-white bg-blue-600 rounded hover:bg-blue-700"
                    >
                      Connect Outlook
                    </button>
                  </div>
                </div>

                {emailAccountsLoading ? (
                  <p className="text-sm text-gray-500 mt-4">Loading email accounts...</p>
                ) : emailAccounts.length === 0 ? (
                  <p className="text-sm text-gray-500 mt-4">No email accounts connected yet.</p>
                ) : (
                  <div className="space-y-3 mt-4">
                    {emailAccounts.map(account => (
                      <div key={account.id} className="border border-gray-200 rounded-lg p-4 flex flex-wrap items-center justify-between gap-3">
                        <div>
                          <p className="text-sm font-semibold text-slate-800">{account.provider} - {account.emailAddress}</p>
                          <p className="text-xs text-gray-500 mt-1">
                            {account.displayName || 'No display name'} · Last sync {account.lastSyncAt ? new Date(account.lastSyncAt).toLocaleString('en-US') : 'Not synced'}
                          </p>
                          {account.syncError && (
                            <p className="text-xs text-red-600 mt-1">Sync error: {account.syncError}</p>
                          )}
                        </div>
                        <div className="flex items-center gap-2">
                          <button
                            onClick={() => handleSyncEmailAccount(account.id)}
                            className="px-3 py-1 text-xs font-semibold text-slate-700 border border-gray-300 rounded hover:bg-white"
                          >
                            Sync Now
                          </button>
                          <button
                            onClick={() => handleDisconnectEmailAccount(account.id)}
                            className="px-3 py-1 text-xs font-semibold text-red-600 border border-red-200 rounded hover:bg-red-50"
                          >
                            Disconnect
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <IntegrationOpsPanel
                refreshKey={integrations
                  .map(i => `${i.id}:${i.status}:${i.syncEnabled ? '1' : '0'}:${i.lastSyncAt || ''}`)
                  .join('|')}
              />

              <JurisdictionRulesOpsPanel
                refreshKey={integrations
                  .map(i => `${i.id}:${i.status}:${i.syncEnabled ? '1' : '0'}:${i.lastSyncAt || ''}:${i.lastWebhookAt || ''}`)
                  .join('|')}
              />

              <LegalBillingOpsPanel
                refreshKey={integrations
                  .map(i => `${i.id}:${i.status}:${i.syncEnabled ? '1' : '0'}:${i.lastSyncAt || ''}:${i.lastWebhookAt || ''}`)
                  .join('|')}
              />

              <TrustRiskRadarPanel
                refreshKey={integrations
                  .map(i => `${i.id}:${i.status}:${i.syncEnabled ? '1' : '0'}:${i.lastSyncAt || ''}:${i.lastWebhookAt || ''}`)
                  .join('|')}
              />

              <EfilingWorkflowPanel
                integrations={integrations}
                refreshKey={integrations
                  .map(i => `${i.id}:${i.status}:${i.syncEnabled ? '1' : '0'}:${i.lastSyncAt || ''}:${i.lastWebhookAt || ''}`)
                  .join('|')}
              />

              {integrationModalOpen && activeIntegration && (
                <div className="fixed inset-0 z-50 bg-black/40 flex items-start sm:items-center justify-center overflow-y-auto p-4">
                  <div className="bg-white rounded-xl shadow-xl w-full max-w-md p-6 my-4 max-h-[calc(100vh-2rem)] overflow-y-auto">
                    <div className="flex items-start justify-between mb-4">
                      <div>
                        <h3 className="text-lg font-bold text-slate-900">Connect {activeIntegration.provider}</h3>
                        <p className="text-xs text-gray-500 mt-1">{activeIntegration.description}</p>
                      </div>
                      <button onClick={closeIntegrationModal} className="text-gray-400 hover:text-gray-600">
                        <X className="w-4 h-4" />
                      </button>
                    </div>
                    <div className="space-y-4">
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Account Label</label>
                        <input
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          placeholder="Acme Holdings - Operating"
                          value={integrationDraft.accountLabel}
                          onChange={(e) => setIntegrationDraft(prev => ({ ...prev, accountLabel: e.target.value }))}
                        />
                      </div>
                      <div>
                        <label className="block text-xs font-semibold text-gray-500 mb-1">Account Email (optional)</label>
                        <input
                          type="email"
                          className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                          placeholder="finance@yourfirm.com"
                          value={integrationDraft.accountEmail}
                          onChange={(e) => setIntegrationDraft(prev => ({ ...prev, accountEmail: e.target.value }))}
                        />
                      </div>
                      {(activeIntegration.connectionMode || '').toLowerCase() === 'api_key' && (
                        <div>
                          <label className="block text-xs font-semibold text-gray-500 mb-1">API Key</label>
                          <input
                            className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                            placeholder="Paste provider API key"
                            value={integrationDraft.apiKey}
                            onChange={(e) => setIntegrationDraft(prev => ({ ...prev, apiKey: e.target.value }))}
                          />
                        </div>
                      )}
                      {(activeIntegration.connectionMode || '').toLowerCase() === 'oauth' && (
                        supportsOAuthRedirectStart(activeIntegration.providerKey || resolveIntegrationKey(undefined, activeIntegration.provider, activeIntegration.category))
                      ) && (
                        <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
                          <p className="text-xs font-semibold text-slate-700">OAuth Connection</p>
                          <p className="text-[11px] text-gray-500 mt-1">
                            Use secure redirect flow. Authorization code entry is automatic after callback.
                          </p>
                          <button
                            type="button"
                            onClick={handleStartIntegrationOAuth}
                            className="mt-3 px-3 py-1.5 text-xs font-semibold text-white bg-slate-800 rounded hover:bg-slate-900"
                          >
                            Start OAuth
                          </button>
                        </div>
                      )}
                      {(activeIntegration.connectionMode || '').toLowerCase() === 'oauth' && !supportsOAuthRedirectStart(activeIntegration.providerKey || resolveIntegrationKey(undefined, activeIntegration.provider, activeIntegration.category)) && (
                        <>
                          <div>
                            <label className="block text-xs font-semibold text-gray-500 mb-1">Authorization Code (optional)</label>
                            <input
                              className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                              placeholder="Paste OAuth authorization code"
                              value={integrationDraft.authorizationCode}
                              onChange={(e) => setIntegrationDraft(prev => ({ ...prev, authorizationCode: e.target.value }))}
                            />
                          </div>
                          <div>
                            <label className="block text-xs font-semibold text-gray-500 mb-1">Access Token (optional)</label>
                            <input
                              className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                              placeholder="Paste OAuth access token"
                              value={integrationDraft.accessToken}
                              onChange={(e) => setIntegrationDraft(prev => ({ ...prev, accessToken: e.target.value }))}
                            />
                          </div>
                          <div>
                            <label className="block text-xs font-semibold text-gray-500 mb-1">Refresh Token (optional)</label>
                            <input
                              className="w-full border border-gray-300 rounded-lg p-2.5 text-sm"
                              placeholder="Paste OAuth refresh token"
                              value={integrationDraft.refreshToken}
                              onChange={(e) => setIntegrationDraft(prev => ({ ...prev, refreshToken: e.target.value }))}
                            />
                          </div>
                        </>
                      )}
                      <div className="flex items-center justify-between">
                        <div>
                          <p className="text-xs font-semibold text-gray-600">Auto-sync</p>
                          <p className="text-[11px] text-gray-400">Enable nightly sync for this integration.</p>
                        </div>
                        <button
                          onClick={() => setIntegrationDraft(prev => ({ ...prev, syncEnabled: !prev.syncEnabled }))}
                          className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${integrationDraft.syncEnabled ? 'bg-emerald-500' : 'bg-gray-300'}`}
                        >
                          <span className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${integrationDraft.syncEnabled ? 'translate-x-6' : 'translate-x-1'}`} />
                        </button>
                      </div>
                    </div>
                    <div className="flex items-center justify-end gap-2 mt-6">
                      <button
                        onClick={closeIntegrationModal}
                        className="px-4 py-2 text-sm font-semibold text-gray-600 hover:text-gray-800"
                      >
                        Cancel
                      </button>
                      <button
                        onClick={(activeIntegration.connectionMode || '').toLowerCase() === 'oauth' && supportsOAuthRedirectStart(activeIntegration.providerKey || resolveIntegrationKey(undefined, activeIntegration.provider, activeIntegration.category))
                          ? handleStartIntegrationOAuth
                          : handleSubmitIntegration}
                        disabled={integrationsSaving}
                        className="px-4 py-2 text-sm font-semibold text-white bg-slate-800 rounded-lg hover:bg-slate-900 disabled:opacity-50"
                      >
                        {integrationsSaving
                          ? 'Saving...'
                          : (activeIntegration.connectionMode || '').toLowerCase() === 'oauth' && supportsOAuthRedirectStart(activeIntegration.providerKey || resolveIntegrationKey(undefined, activeIntegration.provider, activeIntegration.category))
                            ? 'Start OAuth'
                            : 'Connect'}
                      </button>
                    </div>
                  </div>
                </div>
              )}

              {emailConnectOpen && (
                <div className="fixed inset-0 z-50 bg-black/40 flex items-start sm:items-center justify-center overflow-y-auto p-4">
                  <div className="bg-white rounded-xl shadow-xl w-full max-w-md p-6 my-4 max-h-[calc(100vh-2rem)] overflow-y-auto">
                    <div className="flex items-start justify-between mb-4">
                      <div>
                        <h3 className="text-lg font-bold text-slate-900">Connect {emailConnectProvider}</h3>
                        <p className="text-xs text-gray-500 mt-1">Add an email inbox for matter-linked communications.</p>
                      </div>
                      <button onClick={closeEmailModal} className="text-gray-400 hover:text-gray-600">
                        <X className="w-4 h-4" />
                      </button>
                    </div>
                    <div className="space-y-4">
                      <div className="rounded-lg border border-slate-200 bg-slate-50 p-4">
                        <p className="text-sm font-semibold text-slate-800">Bring your own mailbox</p>
                        <p className="text-xs text-gray-500 mt-2">
                          JurisFlow will redirect you to {emailConnectProvider} and read the mailbox identity from the provider profile.
                          No tenant-level SMTP setup is required.
                        </p>
                        <p className="text-xs text-gray-500 mt-2">
                          After approval, mail is sent as the connected staff mailbox and saved back to Sent items.
                        </p>
                      </div>
                    </div>
                    <div className="flex items-center justify-end gap-2 mt-6">
                      <button
                        onClick={closeEmailModal}
                        className="px-4 py-2 text-sm font-semibold text-gray-600 hover:text-gray-800"
                      >
                        Cancel
                      </button>
                      <button
                        onClick={handleConnectEmailAccount}
                        className="px-4 py-2 text-sm font-semibold text-white bg-slate-800 rounded-lg hover:bg-slate-900"
                      >
                        Start OAuth
                      </button>
                    </div>
                  </div>
                </div>
              )}
            </div>
          )}

          {activeTab === 'appDirectory' && (
            <AppDirectoryPanel isAdmin={user?.role === 'Admin'} />
          )}

          {/* Admin Panel */}
          {activeTab === 'admin' && (
            <AdminPanel />
          )}
        </div>
      </div>
    </div>
  );
};

export default Settings;


