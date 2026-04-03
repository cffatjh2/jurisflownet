import {
    Matter,
    MatterNote,
    Task,
    TimeEntry,
    Lead,
    CalendarEvent,
    Invoice,
    TaskStatus,
    Expense,
    Employee,
    IntegrationItem,
    IntegrationCatalogItem,
    FirmEntity,
    Office,
    AppDirectoryListing,
    AppDirectoryOnboardingRequest,
    AppDirectoryOnboardingResponse,
    AppDirectoryReviewRequest,
    AppDirectorySubmission,
    IntegrationCanonicalContractDescriptor,
    IntegrationCapabilityMatrixResponse,
    IntegrationMappingProfile,
    UpsertIntegrationMappingProfileRequest,
    RunCanonicalIntegrationActionRequest,
    CanonicalIntegrationActionResult,
    IntegrationConflictQueueItem,
    ResolveIntegrationConflictRequest,
    IntegrationReviewQueueItem,
    DecideIntegrationReviewItemRequest,
    IntegrationInboxEventListItem,
    ReplayIntegrationInboxEventResponse,
    IntegrationOutboxEventListItem,
    ReplayIntegrationOutboxEventResponse,
    IntegrationRunListItem,
    ReplayIntegrationRunResponse,
    IntegrationSecretStoreStatus,
    RotateIntegrationSecretsResponse,
    EfilingWorkspaceResponse,
    EfilingPrecheckResponse,
    EfilingSubmissionTimelineResponse,
    EfilingSubmissionTransitionResponse,
    EfilingDocketAutomationResponse,
    OutcomeFeePlanDetailResult,
    OutcomeFeePlanVersion,
    OutcomeFeePlanVersionCompareResult,
    OutcomeFeePlanTriggerResult,
    OutcomeFeePlanPortfolioMetricsResult,
    OutcomeFeeCalibrationSnapshotsListResult,
    OutcomeFeeCalibrationEffectiveResult,
    OutcomeFeeCalibrationJobRunResult,
    OutcomeFeeOutcomeFeedbackResult
} from "../types";

// Use relative path when in browser (proxy will handle it), fallback to full URL for SSR
const API_URL = typeof window !== 'undefined' ? '/api' : 'http://localhost:3001/api';
const TENANT_STORAGE_KEY = 'tenant_slug';

const getTenantSlug = () => {
    if (typeof window === 'undefined') return null;
    return localStorage.getItem(TENANT_STORAGE_KEY);
};

const mapInvoiceLineItemsForApi = (lineItems: any[] | undefined) => (lineItems || []).map((lineItem) => ({
    ...lineItem,
    serviceDate: lineItem?.serviceDate ?? lineItem?.date ?? null
}));

const parseResponseBody = async (res: Response) => {
    const contentType = res.headers.get('content-type') || '';
    const text = await res.text();
    const looksJson = contentType.includes('application/json') || contentType.includes('+json');
    const looksHtml = /^\s*</.test(text);

    if (!text.trim()) {
        return { contentType, text, data: null as any, looksJson, looksHtml };
    }

    if (looksJson) {
        try {
            return { contentType, text, data: JSON.parse(text), looksJson, looksHtml };
        } catch {
            throw new Error('Invalid JSON response body.');
        }
    }

    try {
        return { contentType, text, data: JSON.parse(text), looksJson: true, looksHtml };
    } catch {
        return { contentType, text, data: null as any, looksJson, looksHtml };
    }
};

const refreshAuthToken = async () => {
    if (typeof window === 'undefined') return false;
    const refreshToken = localStorage.getItem('auth_refresh_token');
    const sessionId = localStorage.getItem('auth_session_id');
    if (!refreshToken || !sessionId) return false;

    try {
        const tenantSlug = getTenantSlug();
        const res = await fetch(`${API_URL}/auth/refresh`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(tenantSlug ? { 'X-Tenant-Slug': tenantSlug } : {})
            },
            body: JSON.stringify({ sessionId, refreshToken })
        });
        if (!res.ok) return false;
        const data = await res.json();
        if (data?.token) {
            localStorage.setItem('auth_token', data.token);
        }
        if (data?.refreshToken) {
            localStorage.setItem('auth_refresh_token', data.refreshToken);
        }
        if (data?.refreshTokenExpiresAt) {
            localStorage.setItem('auth_refresh_expires_at', data.refreshTokenExpiresAt);
        }
        if (data?.session?.id && data?.session?.expiresAt) {
            localStorage.setItem('auth_session_id', data.session.id);
            localStorage.setItem('auth_session_expires_at', data.session.expiresAt);
        }
        if (data?.user) {
            localStorage.setItem('auth_user', JSON.stringify(data.user));
        }
        return true;
    } catch {
        return false;
    }
};

const fetchJson = async (endpoint: string, options: RequestInit = {}, allowRefresh: boolean = true) => {
    const token = typeof window !== 'undefined' ? localStorage.getItem('auth_token') : null;
    const tenantSlug = getTenantSlug();
    const res = await fetch(`${API_URL}${endpoint}`, {
        headers: {
            'Content-Type': 'application/json',
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
            ...(tenantSlug ? { 'X-Tenant-Slug': tenantSlug } : {})
        },
        cache: 'no-store',
        ...options
    });
    // Handle 401 Unauthorized gracefully - return null instead of throwing
    if (res.status === 401) {
        if (allowRefresh && await refreshAuthToken()) {
            return fetchJson(endpoint, options, false);
        }
        if (typeof window !== 'undefined') {
            window.dispatchEvent(new CustomEvent('auth:unauthorized', { detail: { endpoint } }));
        }
        return null;
    }
    if (!res.ok) {
        let errorDetail = '';
        try {
            const parsed = await parseResponseBody(res);
            if (parsed.data?.message) {
                errorDetail = parsed.data.message;
            } else if (parsed.looksHtml) {
                errorDetail = `Expected JSON from ${endpoint} but received HTML. Check API base URL / deployment routing.`;
            } else {
                errorDetail = parsed.text || JSON.stringify(parsed.data);
            }
        } catch (parseError: any) {
            errorDetail = parseError?.message || '';
        }
        const suffix = errorDetail ? ` (${errorDetail})` : '';
        throw new Error(`API Error: ${res.statusText}${suffix}`);
    }
    // Handle 204 No Content responses (common for DELETE operations)
    if (res.status === 204 || res.headers.get('content-length') === '0') {
        return null;
    }
    const parsed = await parseResponseBody(res);
    if (parsed.data !== null) {
        return parsed.data;
    }
    if (parsed.looksHtml) {
        throw new Error(`API Error: Expected JSON from ${endpoint} but received HTML. Check API base URL / deployment routing.`);
    }
    if (!parsed.text.trim()) {
        return null;
    }
    throw new Error(`API Error: Unexpected non-JSON response from ${endpoint}.`);
};

const fetchFile = async (endpoint: string) => {
    const token = typeof window !== 'undefined' ? localStorage.getItem('auth_token') : null;
    const tenantSlug = getTenantSlug();
    const res = await fetch(`${API_URL}${endpoint}`, {
        headers: {
            ...(token ? { Authorization: `Bearer ${token}` } : {}),
            ...(tenantSlug ? { 'X-Tenant-Slug': tenantSlug } : {})
        }
    });
    if (res.status === 401) {
        if (typeof window !== 'undefined') {
            window.dispatchEvent(new CustomEvent('auth:unauthorized', { detail: { endpoint } }));
        }
        return null;
    }
    if (!res.ok) {
        throw new Error(`API Error: ${res.statusText}`);
    }
    const blob = await res.blob();
    const disposition = res.headers.get('content-disposition') || '';
    const match = /filename="?([^"]+)"?/i.exec(disposition);
    const filename = match ? match[1] : undefined;
    return { blob, filename };
};

export const api = {
    downloadFile: (endpoint: string) => {
        const normalized = endpoint.startsWith('/api/') ? endpoint.replace('/api', '') : endpoint;
        return fetchFile(normalized);
    },
    get: (endpoint: string) => {
        const normalized = endpoint.startsWith('/api/') ? endpoint.replace('/api', '') : endpoint;
        return fetchJson(normalized);
    },
    post: (endpoint: string, data: any) => {
        const normalized = endpoint.startsWith('/api/') ? endpoint.replace('/api', '') : endpoint;
        return fetchJson(normalized, { method: 'POST', body: JSON.stringify(data) });
    },
    // Bootstrap — single request for initial data load
    bootstrap: (scope: 'initial' | 'deferred' | 'full' = 'full') => {
        const query = scope === 'full' ? '' : `?scope=${encodeURIComponent(scope)}`;
        return fetchJson(`/bootstrap${query}`);
    },
    // Auth
    login: (data: { email: string; password: string }) => fetchJson('/login', { method: 'POST', body: JSON.stringify(data) }),
    mfa: {
        status: () => fetchJson('/mfa/status'),
        setup: () => fetchJson('/mfa/setup', { method: 'POST' }),
        enable: (code: string) => fetchJson('/mfa/enable', { method: 'POST', body: JSON.stringify({ code }) }),
        disable: (code: string) => fetchJson('/mfa/disable', { method: 'POST', body: JSON.stringify({ code }) }),
        verify: (challengeId: string, code: string) =>
            fetchJson('/mfa/verify', { method: 'POST', body: JSON.stringify({ challengeId, code }) })
    },
    security: {
        getConfig: () => fetchJson('/security/config'),
        getSessions: () => fetchJson('/security/sessions'),
        revokeSession: (id: string) => fetchJson(`/security/sessions/${id}/revoke`, { method: 'POST' }),
        revokeCurrentSession: () => fetchJson('/security/sessions/revoke-current', { method: 'POST' })
    },
    settings: {
        getBilling: () => fetchJson('/settings/billing'),
        updateBilling: (data: any) => fetchJson('/settings/billing', { method: 'PUT', body: JSON.stringify(data) }),
        getFirm: () => fetchJson('/settings/firm'),
        updateFirm: (data: any) => fetchJson('/settings/firm', { method: 'PUT', body: JSON.stringify(data) }),
        getIntegrations: () => fetchJson('/settings/integrations'),
        getIntegrationCatalog: () => fetchJson('/settings/integrations/catalog') as Promise<IntegrationCatalogItem[] | null>,
        updateIntegrations: (items: IntegrationItem[]) =>
            fetchJson('/settings/integrations', { method: 'PUT', body: JSON.stringify({ items }) }),
        connectIntegration: (providerKey: string, payload: Record<string, unknown>) =>
            fetchJson(`/settings/integrations/${encodeURIComponent(providerKey)}/connect`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<IntegrationItem | null>,
        validateIntegration: (providerKey: string, payload: Record<string, unknown>) =>
            fetchJson(`/settings/integrations/${encodeURIComponent(providerKey)}/validate`, { method: 'POST', body: JSON.stringify(payload) }),
        syncIntegration: (providerKey: string) =>
            fetchJson(`/settings/integrations/${encodeURIComponent(providerKey)}/sync`, { method: 'POST' }),
        disconnectIntegration: (providerKey: string) =>
            fetchJson(`/settings/integrations/${encodeURIComponent(providerKey)}`, { method: 'DELETE' })
    },

    appDirectory: {
        listListings: (includeAll: boolean = false) =>
            fetchJson(`/app-directory/listings${includeAll ? '?includeAll=true' : ''}`) as Promise<AppDirectoryListing[] | null>,
        getListing: (id: string) =>
            fetchJson(`/app-directory/listings/${encodeURIComponent(id)}`) as Promise<AppDirectoryListing | null>,
        getReviewQueue: () =>
            fetchJson('/app-directory/review-queue') as Promise<AppDirectoryListing[] | null>,
        submitOnboarding: (payload: AppDirectoryOnboardingRequest) =>
            fetchJson('/app-directory/onboarding/submit', { method: 'POST', body: JSON.stringify(payload) }) as Promise<AppDirectoryOnboardingResponse | null>,
        retestListing: (id: string) =>
            fetchJson(`/app-directory/listings/${encodeURIComponent(id)}/retest`, { method: 'POST' }) as Promise<AppDirectoryOnboardingResponse | null>,
        reviewListing: (id: string, payload: AppDirectoryReviewRequest) =>
            fetchJson(`/app-directory/listings/${encodeURIComponent(id)}/review`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<AppDirectoryListing | null>,
        getSubmissions: (id: string) =>
            fetchJson(`/app-directory/listings/${encodeURIComponent(id)}/submissions`) as Promise<AppDirectorySubmission[] | null>
    },

    integrationsOps: {
        getContract: () =>
            fetchJson('/integrations/ops/contract') as Promise<IntegrationCanonicalContractDescriptor | null>,
        getCapabilityMatrix: () =>
            fetchJson('/integrations/ops/capability-matrix') as Promise<IntegrationCapabilityMatrixResponse | null>,
        getKpiAnalytics: (params?: { days?: number; bucket?: 'day' | 'week' | 'month' }) => {
            const qs = new URLSearchParams();
            if (typeof params?.days === 'number') qs.set('days', String(params.days));
            if (params?.bucket) qs.set('bucket', params.bucket);
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/integrations/ops/analytics/kpis${query}`) as Promise<any | null>;
        },
        getMappingProfiles: (connectionId: string) =>
            fetchJson(`/integrations/ops/connections/${encodeURIComponent(connectionId)}/mapping-profiles`) as Promise<IntegrationMappingProfile[] | null>,
        upsertMappingProfile: (connectionId: string, profileKey: string, payload: UpsertIntegrationMappingProfileRequest) =>
            fetchJson(
                `/integrations/ops/connections/${encodeURIComponent(connectionId)}/mapping-profiles/${encodeURIComponent(profileKey)}`,
                { method: 'PUT', body: JSON.stringify(payload) }
            ) as Promise<IntegrationMappingProfile | null>,
        runCanonicalAction: (connectionId: string, action: string, payload: RunCanonicalIntegrationActionRequest = {}) =>
            fetchJson(
                `/integrations/ops/connections/${encodeURIComponent(connectionId)}/actions/${encodeURIComponent(action)}`,
                { method: 'POST', body: JSON.stringify(payload) }
            ) as Promise<CanonicalIntegrationActionResult | null>,
        getConflicts: (params?: { status?: string; providerKey?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.status) qs.set('status', params.status);
            if (params?.providerKey) qs.set('providerKey', params.providerKey);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/integrations/ops/conflicts${query}`) as Promise<IntegrationConflictQueueItem[] | null>;
        },
        resolveConflict: (id: string, payload: ResolveIntegrationConflictRequest) =>
            fetchJson(`/integrations/ops/conflicts/${encodeURIComponent(id)}/resolve`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<IntegrationConflictQueueItem | null>,
        getReviewQueue: (params?: { status?: string; providerKey?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.status) qs.set('status', params.status);
            if (params?.providerKey) qs.set('providerKey', params.providerKey);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/integrations/ops/review-queue${query}`) as Promise<IntegrationReviewQueueItem[] | null>;
        },
        decideReviewItem: (id: string, payload: DecideIntegrationReviewItemRequest) =>
            fetchJson(`/integrations/ops/review-queue/${encodeURIComponent(id)}/decision`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<IntegrationReviewQueueItem | null>,
        getInboxEvents: (params?: { status?: string; providerKey?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.status) qs.set('status', params.status);
            if (params?.providerKey) qs.set('providerKey', params.providerKey);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/integrations/ops/events/inbox${query}`) as Promise<IntegrationInboxEventListItem[] | null>;
        },
        replayInboxEvent: (id: string) =>
            fetchJson(`/integrations/ops/events/inbox/${encodeURIComponent(id)}/replay`, { method: 'POST' }) as Promise<ReplayIntegrationInboxEventResponse | null>,
        getOutboxEvents: (params?: { status?: string; providerKey?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.status) qs.set('status', params.status);
            if (params?.providerKey) qs.set('providerKey', params.providerKey);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/integrations/ops/events/outbox${query}`) as Promise<IntegrationOutboxEventListItem[] | null>;
        },
        replayOutboxEvent: (id: string) =>
            fetchJson(`/integrations/ops/events/outbox/${encodeURIComponent(id)}/replay`, { method: 'POST' }) as Promise<ReplayIntegrationOutboxEventResponse | null>,
        getRuns: (params?: { status?: string; providerKey?: string; connectionId?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.status) qs.set('status', params.status);
            if (params?.providerKey) qs.set('providerKey', params.providerKey);
            if (params?.connectionId) qs.set('connectionId', params.connectionId);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/integrations/ops/runs${query}`) as Promise<IntegrationRunListItem[] | null>;
        },
        replayRun: (id: string) =>
            fetchJson(`/integrations/ops/runs/${encodeURIComponent(id)}/replay`, { method: 'POST' }) as Promise<ReplayIntegrationRunResponse | null>,
        getSecretStoreStatus: () =>
            fetchJson('/integrations/ops/security/secrets/status') as Promise<IntegrationSecretStoreStatus | null>,
        rotateSecretsNow: () =>
            fetchJson('/integrations/ops/security/secrets/rotate', { method: 'POST' }) as Promise<RotateIntegrationSecretsResponse | null>
    },

    efiling: {
        getWorkspace: (matterId: string, providerKey?: string) => {
            const qs = new URLSearchParams();
            if (providerKey) qs.set('providerKey', providerKey);
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/efiling/workspace/${encodeURIComponent(matterId)}${query}`) as Promise<EfilingWorkspaceResponse | null>;
        },
        precheckPacket: (payload: {
            matterId: string;
            providerKey?: string;
            packetName?: string;
            filingType?: string;
            courtType?: string;
            triggerDateUtc?: string;
            documentIds?: string[];
            metadata?: Record<string, string>;
        }) =>
            fetchJson('/efiling/precheck', { method: 'POST', body: JSON.stringify(payload) }) as Promise<EfilingPrecheckResponse | null>,
        listSubmissions: (params?: { matterId?: string; providerKey?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.matterId) qs.set('matterId', params.matterId);
            if (params?.providerKey) qs.set('providerKey', params.providerKey);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/efiling/submissions${query}`) as Promise<any[] | null>;
        },
        getSubmissionTimeline: (id: string) =>
            fetchJson(`/efiling/submissions/${encodeURIComponent(id)}/timeline`) as Promise<EfilingSubmissionTimelineResponse | null>,
        transitionSubmission: (id: string, payload: { targetStatus: string; rejectionReason?: string }) =>
            fetchJson(`/efiling/submissions/${encodeURIComponent(id)}/transition`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<EfilingSubmissionTransitionResponse | null>,
        startRepair: (id: string, payload?: { notes?: string }) =>
            fetchJson(`/efiling/submissions/${encodeURIComponent(id)}/repair`, { method: 'POST', body: JSON.stringify(payload || {}) }) as Promise<EfilingSubmissionTransitionResponse | null>,
        listDockets: (params?: { matterId?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.matterId) qs.set('matterId', params.matterId);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/efiling/dockets${query}`) as Promise<any[] | null>;
        },
        runDocketAutomation: (payload?: { connectionId?: string; providerKey?: string; matterId?: string; limit?: number; docketEntryIds?: string[] }) =>
            fetchJson('/efiling/dockets/automation', { method: 'POST', body: JSON.stringify(payload || {}) }) as Promise<EfilingDocketAutomationResponse | null>
    },

    jurisdictionRules: {
        getJurisdictions: (params?: { scope?: string; activeOnly?: boolean }) => {
            const qs = new URLSearchParams();
            if (params?.scope) qs.set('scope', params.scope);
            if (typeof params?.activeOnly === 'boolean') qs.set('activeOnly', String(params.activeOnly));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/jurisdiction-rules-platform/jurisdictions${query}`) as Promise<any[] | null>;
        },
        getCoverage: (params?: { jurisdictionCode?: string; supportLevel?: string; caseType?: string; activeOnly?: boolean; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.jurisdictionCode) qs.set('jurisdictionCode', params.jurisdictionCode);
            if (params?.supportLevel) qs.set('supportLevel', params.supportLevel);
            if (params?.caseType) qs.set('caseType', params.caseType);
            if (typeof params?.activeOnly === 'boolean') qs.set('activeOnly', String(params.activeOnly));
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/jurisdiction-rules-platform/coverage${query}`) as Promise<any[] | null>;
        },
        resolveCoverage: (params: { jurisdictionCode: string; courtSystem?: string; courtDivision?: string; venue?: string; caseType?: string; filingMethod?: string }) => {
            const qs = new URLSearchParams();
            qs.set('jurisdictionCode', params.jurisdictionCode);
            if (params.courtSystem) qs.set('courtSystem', params.courtSystem);
            if (params.courtDivision) qs.set('courtDivision', params.courtDivision);
            if (params.venue) qs.set('venue', params.venue);
            if (params.caseType) qs.set('caseType', params.caseType);
            if (params.filingMethod) qs.set('filingMethod', params.filingMethod);
            return fetchJson(`/jurisdiction-rules-platform/coverage/resolve?${qs.toString()}`) as Promise<any | null>;
        },
        getRulePacks: (params?: { jurisdictionCode?: string; status?: string; caseType?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.jurisdictionCode) qs.set('jurisdictionCode', params.jurisdictionCode);
            if (params?.status) qs.set('status', params.status);
            if (params?.caseType) qs.set('caseType', params.caseType);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/jurisdiction-rules-platform/rule-packs${query}`) as Promise<any[] | null>;
        },
        submitRulePackForReview: (id: string, notes?: string) =>
            fetchJson(`/jurisdiction-rules-platform/rule-packs/${encodeURIComponent(id)}/submit-review`, { method: 'POST', body: JSON.stringify({ notes: notes || null }) }) as Promise<any | null>,
        publishRulePack: (id: string, notes?: string) =>
            fetchJson(`/jurisdiction-rules-platform/rule-packs/${encodeURIComponent(id)}/publish`, { method: 'POST', body: JSON.stringify({ notes: notes || null }) }) as Promise<any | null>,
        getChanges: (params?: { jurisdictionCode?: string; status?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.jurisdictionCode) qs.set('jurisdictionCode', params.jurisdictionCode);
            if (params?.status) qs.set('status', params.status);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/jurisdiction-rules-platform/changes${query}`) as Promise<any[] | null>;
        },
        getValidationRuns: (params?: { limit?: number }) => {
            const qs = new URLSearchParams();
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/jurisdiction-rules-platform/validation-runs${query}`) as Promise<any[] | null>;
        },
        runValidationHarness: (payload?: { jurisdictionCode?: string; courtSystem?: string; caseType?: string; filingMethod?: string; rulePackId?: string; limit?: number }) =>
            fetchJson('/jurisdiction-rules-platform/validation-harness/run', { method: 'POST', body: JSON.stringify(payload || {}) }) as Promise<any | null>
    },

    legalBilling: {
        getMatterPolicy: (matterId: string) =>
            fetchJson(`/legal-billing/policies/matter/${encodeURIComponent(matterId)}`) as Promise<any | null>,
        upsertMatterPolicy: (payload: Record<string, unknown>) =>
            fetchJson('/legal-billing/policies/matter', { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        getRateCards: (params?: { scope?: string; clientId?: string; matterId?: string; status?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.scope) qs.set('scope', params.scope);
            if (params?.clientId) qs.set('clientId', params.clientId);
            if (params?.matterId) qs.set('matterId', params.matterId);
            if (params?.status) qs.set('status', params.status);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/rate-cards${query}`) as Promise<any[] | null>;
        },
        upsertRateCard: (payload: Record<string, unknown>) =>
            fetchJson('/legal-billing/rate-cards', { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        getRateCardEntries: (rateCardId: string) =>
            fetchJson(`/legal-billing/rate-cards/${encodeURIComponent(rateCardId)}/entries`) as Promise<any[] | null>,
        upsertRateCardEntry: (rateCardId: string, payload: Record<string, unknown>) =>
            fetchJson(`/legal-billing/rate-cards/${encodeURIComponent(rateCardId)}/entries`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        generatePrebill: (payload: Record<string, unknown>) =>
            fetchJson('/legal-billing/prebills/generate', { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        getPrebills: (params?: { matterId?: string; clientId?: string; status?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.matterId) qs.set('matterId', params.matterId);
            if (params?.clientId) qs.set('clientId', params.clientId);
            if (params?.status) qs.set('status', params.status);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/prebills${query}`) as Promise<any[] | null>;
        },
        getPrebill: (prebillId: string) =>
            fetchJson(`/legal-billing/prebills/${encodeURIComponent(prebillId)}`) as Promise<any | null>,
        submitPrebillForReview: (prebillId: string, notes?: string) =>
            fetchJson(`/legal-billing/prebills/${encodeURIComponent(prebillId)}/submit-review`, { method: 'POST', body: JSON.stringify({ notes: notes || null }) }) as Promise<any | null>,
        approvePrebill: (prebillId: string, notes?: string) =>
            fetchJson(`/legal-billing/prebills/${encodeURIComponent(prebillId)}/approve`, { method: 'POST', body: JSON.stringify({ notes: notes || null }) }) as Promise<any | null>,
        rejectPrebill: (prebillId: string, notes?: string) =>
            fetchJson(`/legal-billing/prebills/${encodeURIComponent(prebillId)}/reject`, { method: 'POST', body: JSON.stringify({ notes: notes || null }) }) as Promise<any | null>,
        finalizePrebill: (prebillId: string, payload: Record<string, unknown>) =>
            fetchJson(`/legal-billing/prebills/${encodeURIComponent(prebillId)}/finalize`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        adjustPrebillLine: (prebillLineId: string, payload: Record<string, unknown>) =>
            fetchJson(`/legal-billing/prebills/lines/${encodeURIComponent(prebillLineId)}/adjust`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        getLedesPreview: (prebillId: string) =>
            fetchJson(`/legal-billing/prebills/${encodeURIComponent(prebillId)}/ledes-preview`) as Promise<any | null>,
        getLedger: (params?: { ledgerDomain?: string; ledgerBucket?: string; invoiceId?: string; matterId?: string; paymentTransactionId?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.ledgerDomain) qs.set('ledgerDomain', params.ledgerDomain);
            if (params?.ledgerBucket) qs.set('ledgerBucket', params.ledgerBucket);
            if (params?.invoiceId) qs.set('invoiceId', params.invoiceId);
            if (params?.matterId) qs.set('matterId', params.matterId);
            if (params?.paymentTransactionId) qs.set('paymentTransactionId', params.paymentTransactionId);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/ledger${query}`) as Promise<any[] | null>;
        },
        postLedgerAdjustment: (payload: Record<string, unknown>) =>
            fetchJson('/legal-billing/ledger/adjustment', { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        reverseLedgerEntry: (ledgerEntryId: string, payload?: Record<string, unknown>) =>
            fetchJson(`/legal-billing/ledger/${encodeURIComponent(ledgerEntryId)}/reverse`, { method: 'POST', body: JSON.stringify(payload || {}) }) as Promise<any | null>,
        getAllocations: (params?: { paymentTransactionId?: string; invoiceId?: string }) => {
            const qs = new URLSearchParams();
            if (params?.paymentTransactionId) qs.set('paymentTransactionId', params.paymentTransactionId);
            if (params?.invoiceId) qs.set('invoiceId', params.invoiceId);
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/allocations${query}`) as Promise<any[] | null>;
        },
        applyAllocation: (payload: Record<string, unknown>) =>
            fetchJson('/legal-billing/allocations', { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        reverseAllocation: (allocationId: string, payload?: Record<string, unknown>) =>
            fetchJson(`/legal-billing/allocations/${encodeURIComponent(allocationId)}/reverse`, { method: 'POST', body: JSON.stringify(payload || {}) }) as Promise<any | null>,
        getTrustReconciliation: (params?: { trustAccountId?: string; asOfUtc?: string }) => {
            const qs = new URLSearchParams();
            if (params?.trustAccountId) qs.set('trustAccountId', params.trustAccountId);
            if (params?.asOfUtc) qs.set('asOfUtc', params.asOfUtc);
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/trust/reconciliation${query}`) as Promise<any | null>;
        },
        getInvoicePayorStatements: (invoiceId: string, params?: { payorClientId?: string }) => {
            const qs = new URLSearchParams();
            if (params?.payorClientId) qs.set('payorClientId', params.payorClientId);
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/invoices/${encodeURIComponent(invoiceId)}/payor-statements${query}`) as Promise<any | null>;
        },
        getPayorAging: (params?: { asOfUtc?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.asOfUtc) qs.set('asOfUtc', params.asOfUtc);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/collections/payor-aging${query}`) as Promise<any | null>;
        },
        getEbillingTransmissions: (params?: { providerKey?: string; invoiceId?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.providerKey) qs.set('providerKey', params.providerKey);
            if (params?.invoiceId) qs.set('invoiceId', params.invoiceId);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/ebilling/transmissions${query}`) as Promise<any[] | null>;
        },
        getEbillingEvents: (params?: { providerKey?: string; invoiceId?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.providerKey) qs.set('providerKey', params.providerKey);
            if (params?.invoiceId) qs.set('invoiceId', params.invoiceId);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/ebilling/events${query}`) as Promise<any[] | null>;
        },
        recordEbillingTransmission: (payload: Record<string, unknown>) =>
            fetchJson('/legal-billing/ebilling/transmissions', { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        recordEbillingEvent: (payload: Record<string, unknown>) =>
            fetchJson('/legal-billing/ebilling/events', { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        repairEbillingTransmission: (transmissionId: string, payload?: Record<string, unknown>) =>
            fetchJson(`/legal-billing/ebilling/transmissions/${encodeURIComponent(transmissionId)}/repair`, { method: 'POST', body: JSON.stringify(payload || {}) }) as Promise<any | null>,
        getTrustRiskPolicy: () =>
            fetchJson('/legal-billing/trust-risk/policy') as Promise<any | null>,
        getTrustRiskPolicyVersions: (params?: { limit?: number }) => {
            const qs = new URLSearchParams();
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/trust-risk/policy/versions${query}`) as Promise<any[] | null>;
        },
        getTrustRiskPolicyTemplates: () =>
            fetchJson('/legal-billing/trust-risk/policy/templates') as Promise<any[] | null>,
        upsertTrustRiskPolicy: (payload: Record<string, unknown>) =>
            fetchJson('/legal-billing/trust-risk/policy', { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        getTrustRiskEvents: (params?: { status?: string; decision?: string; severity?: string; sourceType?: string; invoiceId?: string; matterId?: string; clientId?: string; fromUtc?: string; toUtc?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.status) qs.set('status', params.status);
            if (params?.decision) qs.set('decision', params.decision);
            if (params?.severity) qs.set('severity', params.severity);
            if (params?.sourceType) qs.set('sourceType', params.sourceType);
            if (params?.invoiceId) qs.set('invoiceId', params.invoiceId);
            if (params?.matterId) qs.set('matterId', params.matterId);
            if (params?.clientId) qs.set('clientId', params.clientId);
            if (params?.fromUtc) qs.set('fromUtc', params.fromUtc);
            if (params?.toUtc) qs.set('toUtc', params.toUtc);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/trust-risk/events${query}`) as Promise<any[] | null>;
        },
        getTrustRiskEventDetail: (eventId: string) =>
            fetchJson(`/legal-billing/trust-risk/events/${encodeURIComponent(eventId)}`) as Promise<any | null>,
        rescoreTrustRiskEvent: (eventId: string, payload?: { reason?: string | null }) =>
            fetchJson(`/legal-billing/trust-risk/events/${encodeURIComponent(eventId)}/rescore`, { method: 'POST', body: JSON.stringify(payload || {}) }) as Promise<any | null>,
        rescoreTrustRiskEventsBatch: (payload?: Record<string, unknown>) =>
            fetchJson('/legal-billing/trust-risk/events/rescore-batch', { method: 'POST', body: JSON.stringify(payload || {}) }) as Promise<any | null>,
        acknowledgeTrustRiskEvent: (eventId: string, payload?: { note?: string | null }) =>
            fetchJson(`/legal-billing/trust-risk/events/${encodeURIComponent(eventId)}/ack`, { method: 'POST', body: JSON.stringify(payload || {}) }) as Promise<any | null>,
        assignTrustRiskEvent: (eventId: string, payload: { assigneeUserId: string; note?: string | null }) =>
            fetchJson(`/legal-billing/trust-risk/events/${encodeURIComponent(eventId)}/assign`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        setTrustRiskReviewDisposition: (eventId: string, payload: { disposition: string; reason: string; approverReason: string }) =>
            fetchJson(`/legal-billing/trust-risk/events/${encodeURIComponent(eventId)}/review-disposition`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        getTrustRiskHolds: (params?: { status?: string; holdType?: string; targetType?: string; targetId?: string; trustRiskEventId?: string; fromUtc?: string; toUtc?: string; limit?: number }) => {
            const qs = new URLSearchParams();
            if (params?.status) qs.set('status', params.status);
            if (params?.holdType) qs.set('holdType', params.holdType);
            if (params?.targetType) qs.set('targetType', params.targetType);
            if (params?.targetId) qs.set('targetId', params.targetId);
            if (params?.trustRiskEventId) qs.set('trustRiskEventId', params.trustRiskEventId);
            if (params?.fromUtc) qs.set('fromUtc', params.fromUtc);
            if (params?.toUtc) qs.set('toUtc', params.toUtc);
            if (typeof params?.limit === 'number') qs.set('limit', String(params.limit));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/trust-risk/holds${query}`) as Promise<any[] | null>;
        },
        getTrustRiskHoldDetail: (holdId: string) =>
            fetchJson(`/legal-billing/trust-risk/holds/${encodeURIComponent(holdId)}`) as Promise<any | null>,
        markTrustRiskHoldUnderReview: (holdId: string, payload?: { reason?: string | null }) =>
            fetchJson(`/legal-billing/trust-risk/holds/${encodeURIComponent(holdId)}/under-review`, { method: 'POST', body: JSON.stringify(payload || {}) }) as Promise<any | null>,
        releaseTrustRiskHold: (holdId: string, payload: { reason: string; approverReason: string }) =>
            fetchJson(`/legal-billing/trust-risk/holds/${encodeURIComponent(holdId)}/release`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        escalateTrustRiskHold: (holdId: string, payload: { reason: string; approverReason: string }) =>
            fetchJson(`/legal-billing/trust-risk/holds/${encodeURIComponent(holdId)}/escalate`, { method: 'POST', body: JSON.stringify(payload) }) as Promise<any | null>,
        getTrustRiskMetrics: (params?: { days?: number }) => {
            const qs = new URLSearchParams();
            if (typeof params?.days === 'number') qs.set('days', String(params.days));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/trust-risk/metrics${query}`) as Promise<any | null>;
        },
        getTrustRiskBaselines: (params?: { days?: number; top?: number }) => {
            const qs = new URLSearchParams();
            if (typeof params?.days === 'number') qs.set('days', String(params.days));
            if (typeof params?.top === 'number') qs.set('top', String(params.top));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/trust-risk/baselines${query}`) as Promise<any | null>;
        },
        getTrustRiskTuning: (params?: { days?: number }) => {
            const qs = new URLSearchParams();
            if (typeof params?.days === 'number') qs.set('days', String(params.days));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/trust-risk/tuning${query}`) as Promise<any | null>;
        },
        getTrustRiskEvidenceExport: (params?: {
            days?: number;
            policyLimit?: number;
            eventLimit?: number;
            holdLimit?: number;
            actionLimit?: number;
            auditLimit?: number;
            includeAuditLogs?: boolean;
            includeEvents?: boolean;
        }) => {
            const qs = new URLSearchParams();
            if (typeof params?.days === 'number') qs.set('days', String(params.days));
            if (typeof params?.policyLimit === 'number') qs.set('policyLimit', String(params.policyLimit));
            if (typeof params?.eventLimit === 'number') qs.set('eventLimit', String(params.eventLimit));
            if (typeof params?.holdLimit === 'number') qs.set('holdLimit', String(params.holdLimit));
            if (typeof params?.actionLimit === 'number') qs.set('actionLimit', String(params.actionLimit));
            if (typeof params?.auditLimit === 'number') qs.set('auditLimit', String(params.auditLimit));
            if (typeof params?.includeAuditLogs === 'boolean') qs.set('includeAuditLogs', String(params.includeAuditLogs));
            if (typeof params?.includeEvents === 'boolean') qs.set('includeEvents', String(params.includeEvents));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/trust-risk/evidence-export${query}`) as Promise<any | null>;
        },
        getTrustRiskTuningImpact: (params?: { days?: number; warn?: number; review?: number; softHold?: number; hardHold?: number }) => {
            const qs = new URLSearchParams();
            if (typeof params?.days === 'number') qs.set('days', String(params.days));
            if (typeof params?.warn === 'number') qs.set('warn', String(params.warn));
            if (typeof params?.review === 'number') qs.set('review', String(params.review));
            if (typeof params?.softHold === 'number') qs.set('softHold', String(params.softHold));
            if (typeof params?.hardHold === 'number') qs.set('hardHold', String(params.hardHold));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/trust-risk/tuning/impact${query}`) as Promise<any | null>;
        },
        compareTrustRiskPolicyImpact: (params?: { days?: number; fromPolicyId?: string; fromVersion?: number; toPolicyId?: string; toVersion?: number }) => {
            const qs = new URLSearchParams();
            if (typeof params?.days === 'number') qs.set('days', String(params.days));
            if (params?.fromPolicyId) qs.set('fromPolicyId', params.fromPolicyId);
            if (typeof params?.fromVersion === 'number') qs.set('fromVersion', String(params.fromVersion));
            if (params?.toPolicyId) qs.set('toPolicyId', params.toPolicyId);
            if (typeof params?.toVersion === 'number') qs.set('toVersion', String(params.toVersion));
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/legal-billing/trust-risk/policy/compare-impact${query}`) as Promise<any | null>;
        }
    },

    // Firm Entities & Offices
    entities: {
        list: () => fetchJson('/entities'),
        create: (data: Partial<FirmEntity>) => fetchJson('/entities', { method: 'POST', body: JSON.stringify(data) }),
        update: (id: string, data: Partial<FirmEntity>) => fetchJson(`/entities/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
        remove: (id: string) => fetchJson(`/entities/${id}`, { method: 'DELETE' }),
        setDefault: (id: string) => fetchJson(`/entities/${id}/default`, { method: 'POST' }),
        offices: {
            list: (entityId: string) => fetchJson(`/entities/${entityId}/offices`),
            create: (entityId: string, data: Partial<Office>) =>
                fetchJson(`/entities/${entityId}/offices`, { method: 'POST', body: JSON.stringify(data) }),
            update: (entityId: string, officeId: string, data: Partial<Office>) =>
                fetchJson(`/entities/${entityId}/offices/${officeId}`, { method: 'PUT', body: JSON.stringify(data) }),
            remove: (entityId: string, officeId: string) =>
                fetchJson(`/entities/${entityId}/offices/${officeId}`, { method: 'DELETE' }),
            setDefault: (entityId: string, officeId: string) =>
                fetchJson(`/entities/${entityId}/offices/${officeId}/default`, { method: 'POST' })
        }
    },

    // Matters
    getMatters: (params?: { status?: string; entityId?: string; officeId?: string }) => {
        const qs = new URLSearchParams();
        if (params?.status) qs.set('status', params.status);
        if (params?.entityId) qs.set('entityId', params.entityId);
        if (params?.officeId) qs.set('officeId', params.officeId);
        const query = qs.toString() ? `?${qs.toString()}` : '';
        return fetchJson(`/matters${query}`);
    },
    createMatter: (data: Partial<Matter>) => fetchJson('/matters', { method: 'POST', body: JSON.stringify(data) }),
    updateMatter: (id: string, data: Partial<Matter>) => fetchJson(`/matters/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    deleteMatter: (id: string) => fetchJson(`/matters/${id}`, { method: 'DELETE' }),
    getMatterNotes: (matterId: string) =>
        fetchJson(`/matters/${encodeURIComponent(matterId)}/notes`) as Promise<MatterNote[] | null>,
    createMatterNote: (matterId: string, data: { title?: string; body: string }) =>
        fetchJson(`/matters/${encodeURIComponent(matterId)}/notes`, { method: 'POST', body: JSON.stringify(data) }) as Promise<MatterNote | null>,
    updateMatterNote: (matterId: string, noteId: string, data: { title?: string; body: string }) =>
        fetchJson(`/matters/${encodeURIComponent(matterId)}/notes/${encodeURIComponent(noteId)}`, { method: 'PUT', body: JSON.stringify(data) }) as Promise<MatterNote | null>,
    deleteMatterNote: (matterId: string, noteId: string) =>
        fetchJson(`/matters/${encodeURIComponent(matterId)}/notes/${encodeURIComponent(noteId)}`, { method: 'DELETE' }),

    // Tasks
    getTasks: () => fetchJson('/tasks'),
    createTask: (data: Partial<Task>) => fetchJson('/tasks', { method: 'POST', body: JSON.stringify(data) }),
    updateTaskStatus: (id: string, status: TaskStatus) => fetchJson(`/tasks/${id}/status`, { method: 'PUT', body: JSON.stringify({ status }) }),
    updateTask: (id: string, data: Partial<Task>) => fetchJson(`/tasks/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    deleteTask: (id: string) => fetchJson(`/tasks/${id}`, { method: 'DELETE' }),

    // Task Templates
    getTaskTemplates: () => fetchJson('/task-templates'),
    createTasksFromTemplate: (data: { templateId: string; matterId?: string; assignedTo?: string; baseDate?: string }) =>
        fetchJson('/tasks/from-template', { method: 'POST', body: JSON.stringify(data) }),

    // Time & Expenses
    getTimeEntries: () => fetchJson('/time-entries'),
    createTimeEntry: (data: Partial<TimeEntry>) => fetchJson('/time-entries', { method: 'POST', body: JSON.stringify(data) }),
    approveTimeEntry: (id: string) => fetchJson(`/time-entries/${id}/approve`, { method: 'POST' }),
    rejectTimeEntry: (id: string, reason?: string) =>
        fetchJson(`/time-entries/${id}/reject`, { method: 'POST', body: JSON.stringify({ reason }) }),
    getExpenses: () => fetchJson('/expenses'),
    createExpense: (data: Partial<Expense>) => fetchJson('/expenses', { method: 'POST', body: JSON.stringify(data) }),
    approveExpense: (id: string) => fetchJson(`/expenses/${id}/approve`, { method: 'POST' }),
    rejectExpense: (id: string, reason?: string) =>
        fetchJson(`/expenses/${id}/reject`, { method: 'POST', body: JSON.stringify({ reason }) }),
    markAsBilled: (matterId: string) => fetchJson('/billing/mark-billed', { method: 'POST', body: JSON.stringify({ matterId }) }),

    // CRM
    getClients: () => fetchJson('/clients'),
    getClientStatusHistory: (id: string) => fetchJson(`/clients/${id}/status-history`),
    createClient: (data: any) => fetchJson('/clients', { method: 'POST', body: JSON.stringify(data) }),
    updateClient: (id: string, data: any) => fetchJson(`/clients/${id}`, { method: 'PATCH', body: JSON.stringify(data) }),
    setClientPassword: (id: string, password: string) =>
        fetchJson(`/clients/${id}/set-password`, { method: 'POST', body: JSON.stringify({ password }) }),
    getLeads: () => fetchJson('/leads'),
    createLead: (data: Partial<Lead>) => fetchJson('/leads', { method: 'POST', body: JSON.stringify(data) }),
    updateLead: (id: string, data: Partial<Lead>) => fetchJson(`/leads/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    deleteLead: (id: string) => fetchJson(`/leads/${id}`, { method: 'DELETE' }),

    // Opposing Parties
    getOpposingParties: () => fetchJson('/opposingparties'),
    getOpposingPartiesByMatter: (matterId: string) => fetchJson(`/opposingparties/matter/${matterId}`),
    createOpposingParty: (data: any) => fetchJson('/opposingparties', { method: 'POST', body: JSON.stringify(data) }),
    updateOpposingParty: (id: string, data: any) => fetchJson(`/opposingparties/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    deleteOpposingParty: (id: string) => fetchJson(`/opposingparties/${id}`, { method: 'DELETE' }),

    // Calendar
    getEvents: () => fetchJson('/events'),
    createEvent: (data: Partial<CalendarEvent>) => fetchJson('/events', { method: 'POST', body: JSON.stringify(data) }),
    updateEvent: (id: string, data: Partial<CalendarEvent>) => fetchJson(`/events/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    deleteEvent: (id: string) => fetchJson(`/events/${id}`, { method: 'DELETE' }),

    // Invoices
    getInvoices: (params?: { entityId?: string; officeId?: string }) => {
        const qs = new URLSearchParams();
        if (params?.entityId) qs.set('entityId', params.entityId);
        if (params?.officeId) qs.set('officeId', params.officeId);
        const query = qs.toString() ? `?${qs.toString()}` : '';
        return fetchJson(`/invoices${query}`);
    },
    getInvoice: (id: string) => fetchJson(`/invoices/${id}`),
    createInvoice: (data: any) => {
        const payload = {
            ...data,
            clientId: data.client?.id || data.clientId,
            lineItems: mapInvoiceLineItemsForApi(data?.lineItems)
        };
        return fetchJson('/invoices', { method: 'POST', body: JSON.stringify(payload) });
    },
    updateInvoice: (id: string, data: any) => fetchJson(`/invoices/${id}`, {
        method: 'PUT',
        body: JSON.stringify({
            ...data,
            lineItems: mapInvoiceLineItemsForApi(data?.lineItems)
        })
    }),
    deleteInvoice: (id: string) => fetchJson(`/invoices/${id}`, { method: 'DELETE' }),
    exportInvoiceLedes: (id: string) => fetchFile(`/invoices/${id}/ledes`),

    // Invoice Workflow
    approveInvoice: (id: string) => fetchJson(`/invoices/${id}/approve`, { method: 'POST' }),
    sendInvoice: (id: string) => fetchJson(`/invoices/${id}/send`, { method: 'POST' }),

    // Invoice Line Items
    addInvoiceLineItem: (invoiceId: string, data: any) => fetchJson(`/invoices/${invoiceId}/line-items`, { method: 'POST', body: JSON.stringify(data) }),
    updateInvoiceLineItem: (invoiceId: string, itemId: string, data: any) => fetchJson(`/invoices/${invoiceId}/line-items/${itemId}`, { method: 'PUT', body: JSON.stringify(data) }),
    deleteInvoiceLineItem: (invoiceId: string, itemId: string) => fetchJson(`/invoices/${invoiceId}/line-items/${itemId}`, { method: 'DELETE' }),

    // Invoice Payments
    recordPayment: (invoiceId: string, data: any) => fetchJson(`/invoices/${invoiceId}/payments`, { method: 'POST', body: JSON.stringify(data) }),
    refundPayment: (invoiceId: string, paymentId: string, data: any) => fetchJson(`/invoices/${invoiceId}/payments/${paymentId}/refund`, { method: 'POST', body: JSON.stringify(data) }),

    // Notifications
    getNotifications: (userId?: string) => fetchJson(`/notifications${userId ? `?userId=${encodeURIComponent(userId)}` : ''}`),
    markNotificationRead: (id: string) => fetchJson(`/notifications/${id}/read`, { method: 'POST' }),
    markNotificationUnread: (id: string) => fetchJson(`/notifications/${id}/unread`, { method: 'POST' }),
    markAllNotificationsRead: () => fetchJson('/notifications/read-all', { method: 'POST' }),

    // Reports
    getReportOverview: (params: { from?: string; to?: string; matterId?: string } = {}) => {
        const qs = new URLSearchParams();
        Object.entries(params).forEach(([k, v]) => {
            if (!v) return;
            qs.set(k, v);
        });
        const query = qs.toString() ? `?${qs.toString()}` : '';
        return fetchJson(`/reports/overview${query}`);
    },

    // User Profile
    updateUserProfile: (data: any) => fetchJson('/user/profile', { method: 'PUT', body: JSON.stringify(data) }),

    // Admin: User Management
    admin: {
        getUsers: () => fetchJson('/admin/users'),
        createUser: (data: any) => fetchJson('/admin/users', { method: 'POST', body: JSON.stringify(data) }),
        updateUser: (id: string, data: any) => fetchJson(`/admin/users/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
        deleteUser: (id: string) => fetchJson(`/admin/users/${id}`, { method: 'DELETE' }),

        // Admin: Client Management
        getClients: () => fetchJson('/admin/clients'),
        updateClient: (id: string, data: any) => fetchJson(`/admin/clients/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
        deleteClient: (id: string) => fetchJson(`/admin/clients/${id}`, { method: 'DELETE' }),

        // Admin: Audit Logs
        getAuditLogs: (params: {
            page?: number;
            limit?: number;
            action?: string;
            entityType?: string;
            entityId?: string;
            userId?: string;
            clientId?: string;
            email?: string;
            q?: string;
            from?: string;
            to?: string;
        } = {}) => {
            const qs = new URLSearchParams();
            Object.entries(params).forEach(([k, v]) => {
                if (v === undefined || v === null || v === '') return;
                qs.set(k, String(v));
            });
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/admin/audit-logs${query}`);
        },
        // Admin: Retention
        getRetentionPolicies: () => fetchJson('/admin/retention'),
        updateRetentionPolicies: (data: any) => fetchJson('/admin/retention', { method: 'PUT', body: JSON.stringify(data) }),
        runRetention: () => fetchJson('/admin/retention/run', { method: 'POST' })
    },

    // Documents
    uploadDocument: async (file: File, matterId?: string, description?: string) => {
        const token = typeof window !== 'undefined' ? localStorage.getItem('auth_token') : null;
        const tenantSlug = getTenantSlug();
        const formData = new FormData();
        formData.append('file', file);
        if (matterId) formData.append('matterId', matterId);
        if (description) formData.append('description', description);

        const res = await fetch(`${API_URL}/documents/upload`, {
            method: 'POST',
            headers: {
                ...(token ? { Authorization: `Bearer ${token}` } : {}),
                ...(tenantSlug ? { 'X-Tenant-Slug': tenantSlug } : {})
            },
            body: formData
        });
        if (res.status === 401) return null;
        if (!res.ok) throw new Error(`API Error: ${res.statusText}`);
        return res.json();
    },
    getDocuments: (params?: { matterId?: string; q?: string }) => {
        const qs = new URLSearchParams();
        if (params?.matterId) qs.set('matterId', params.matterId);
        if (params?.q) qs.set('q', params.q);
        const query = qs.toString() ? `?${qs.toString()}` : '';
        return fetchJson(`/documents${query}`);
    },
    getDocumentShares: (documentId: string) => fetchJson(`/documents/${documentId}/shares`),
    upsertDocumentShare: (documentId: string, data: { clientId: string; canView?: boolean; canDownload?: boolean; canComment?: boolean; canUpload?: boolean; expiresAt?: string | null; note?: string | null }) =>
        fetchJson(`/documents/${documentId}/shares`, { method: 'POST', body: JSON.stringify(data) }),
    removeDocumentShare: (documentId: string, clientId: string) =>
        fetchJson(`/documents/${documentId}/shares/${clientId}`, { method: 'DELETE' }),
    getDocumentComments: (documentId: string) => fetchJson(`/documents/${documentId}/comments`),
    addDocumentComment: (documentId: string, data: { body: string }) =>
        fetchJson(`/documents/${documentId}/comments`, { method: 'POST', body: JSON.stringify(data) }),
    searchDocuments: (q: string, options?: { matterId?: string; includeContent?: boolean }) => {
        const qs = new URLSearchParams({ q });
        if (options?.matterId) qs.set('matterId', options.matterId);
        if (options?.includeContent) qs.set('includeContent', 'true');
        return fetchJson(`/documents/search?${qs.toString()}`);
    },
    deleteDocument: (id: string) => fetchJson(`/documents/${id}`, { method: 'DELETE' }),
    updateDocument: (id: string, data: { matterId?: string | null; description?: string | null; tags?: string[] | string | null; category?: string | null; status?: string | null; legalHoldReason?: string | null }) =>
        fetchJson(`/documents/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    bulkAssignDocuments: (data: { ids: string[]; matterId?: string | null }) =>
        fetchJson('/documents/bulk-assign', { method: 'PUT', body: JSON.stringify(data) }),
    getDocumentVersions: (documentId: string) => fetchJson(`/documents/${documentId}/versions`),
    downloadDocument: (documentId: string) => fetchFile(`/documents/${documentId}/download`),
    downloadDocumentVersion: (versionId: string) => fetchFile(`/documents/versions/${versionId}/download`),
    restoreDocumentVersion: (versionId: string) => fetchJson(`/documents/versions/${versionId}/restore`, { method: 'POST' }),
    diffDocumentVersions: (leftVersionId: string, rightVersionId: string) =>
        fetchJson(`/documents/versions/diff?leftVersionId=${encodeURIComponent(leftVersionId)}&rightVersionId=${encodeURIComponent(rightVersionId)}`),
    uploadDocumentVersion: async (documentId: string, file: File) => {
        const token = typeof window !== 'undefined' ? localStorage.getItem('auth_token') : null;
        const formData = new FormData();
        formData.append('file', file);
        const res = await fetch(`${API_URL}/documents/${documentId}/versions`, {
            method: 'POST',
            headers: {
                ...(token ? { Authorization: `Bearer ${token}` } : {})
            },
            body: formData
        });
        if (res.status === 401) return null;
        if (!res.ok) throw new Error(`API Error: ${res.statusText}`);
        return res.json();
    },

    // Password Reset
    forgotPassword: (email: string, userType: 'attorney' | 'client') =>
        fetchJson('/auth/forgot-password', { method: 'POST', body: JSON.stringify({ email, userType }) }),
    resetPassword: (token: string, password: string) =>
        fetchJson('/auth/reset-password', { method: 'POST', body: JSON.stringify({ token, password }) }),

    // ========== V2.0 APIs ==========

    // Trust Accounting
    getTrustTransactions: (matterId: string) => fetchJson(`/matters/${matterId}/trust`),
    createTrustTransaction: (matterId: string, data: { type: string; amount: number; description: string; reference?: string }) =>
        fetchJson(`/matters/${matterId}/trust`, { method: 'POST', body: JSON.stringify(data) }),

    // Workflows
    getWorkflows: () => fetchJson('/workflows'),
    createWorkflow: (data: { name: string; description?: string; trigger: string; actions: any[]; isActive?: boolean }) =>
        fetchJson('/workflows', { method: 'POST', body: JSON.stringify(data) }),
    updateWorkflow: (id: string, data: Partial<{ name: string; description: string; trigger: string; actions: any[]; isActive: boolean }>) =>
        fetchJson(`/workflows/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    deleteWorkflow: (id: string) => fetchJson(`/workflows/${id}`, { method: 'DELETE' }),

    // Appointments (Attorney)
    getAppointments: () => fetchJson('/appointments'),
    updateAppointment: (id: string, data: { status: string; approvedDate?: string; assignedTo?: string; duration?: number }) =>
        fetchJson(`/appointments/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    notifyAppointment: (id: string) => fetchJson(`/appointments/${id}/notify`, { method: 'POST' }),

    // Intake Forms
    getIntakeForms: () => fetchJson('/intake-forms'),
    createIntakeForm: (data: { name: string; description?: string; fields: any[]; practiceArea?: string }) =>
        fetchJson('/intake-forms', { method: 'POST', body: JSON.stringify(data) }),
    getIntakeSubmissions: () => fetchJson('/intake-submissions'),
    updateIntakeSubmission: (id: string, data: { status: string; notes?: string }) =>
        fetchJson(`/intake-submissions/${id}`, { method: 'PUT', body: JSON.stringify(data) }),

    // Settlement Statements
    getSettlementStatements: (matterId: string) => fetchJson(`/matters/${matterId}/settlement`),
    createSettlementStatement: (matterId: string, data: { grossSettlement: number; attorneyFees: number; expenses: number; liens?: number }) =>
        fetchJson(`/matters/${matterId}/settlement`, { method: 'POST', body: JSON.stringify(data) }),

    // Signature Requests
    createSignatureRequest: (documentId: string, data: { clientId: string; expiresAt?: string }) =>
        fetchJson(`/documents/${documentId}/signature`, { method: 'POST', body: JSON.stringify(data) }),
    getDocumentSignatures: (documentId: string) => fetchJson(`/documents/${documentId}/signatures`),

    // Employees
    getEmployees: (params?: { entityId?: string; officeId?: string }) => {
        const qs = new URLSearchParams();
        if (params?.entityId) qs.set('entityId', params.entityId);
        if (params?.officeId) qs.set('officeId', params.officeId);
        const query = qs.toString() ? `?${qs.toString()}` : '';
        return fetchJson(`/employees${query}`);
    },
    getEmployee: (id: string) => fetchJson(`/employees/${id}`),
    createEmployee: (data: Partial<Employee>) => fetchJson('/employees', { method: 'POST', body: JSON.stringify(data) }),
    updateEmployee: (id: string, data: Partial<Employee>) => fetchJson(`/employees/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    deleteEmployee: (id: string) => fetchJson(`/employees/${id}`, { method: 'DELETE' }),
    resetEmployeePassword: (id: string) => fetchJson(`/employees/${id}/reset-password`, { method: 'POST' }),
    assignTaskToEmployee: (employeeId: string, taskId: string) => fetchJson(`/employees/${employeeId}/assign-task`, { method: 'POST', body: JSON.stringify({ taskId }) }),
    uploadEmployeeAvatar: async (employeeId: string, file: File) => {
        const token = typeof window !== 'undefined' ? localStorage.getItem('auth_token') : null;
        const tenantSlug = getTenantSlug();
        const formData = new FormData();
        formData.append('file', file);
        const res = await fetch(`${API_URL}/employees/${employeeId}/avatar`, {
            method: 'POST',
            headers: {
                ...(token ? { Authorization: `Bearer ${token}` } : {}),
                ...(tenantSlug ? { 'X-Tenant-Slug': tenantSlug } : {})
            },
            body: formData
        });
        if (res.status === 401) return null;
        if (!res.ok) throw new Error(`API Error: ${res.statusText}`);
        return res.json();
    },

    // Conflict Checking
    conflicts: {
        check: (data: { searchQuery: string; checkType?: string; entityType?: string; entityId?: string }) =>
            fetchJson('/conflicts/check', { method: 'POST', body: JSON.stringify(data) }),
        get: (id: string) => fetchJson(`/conflicts/${id}`),
        waive: (id: string, reason: string) => fetchJson(`/conflicts/${id}/waive`, { method: 'POST', body: JSON.stringify({ reason }) }),
        history: (limit?: number) => fetchJson(`/conflicts/history${limit ? `?limit=${limit}` : ''}`),
    },

    // E-Signatures
    signatures: {
        request: (data: { documentId: string; signerEmail: string; signerName?: string; clientId?: string; expiresAt?: string; verificationMethod?: string; requiresKba?: boolean; disclosureProvided?: boolean; disclosureVersion?: string }) =>
            fetchJson('/signatures/request', { method: 'POST', body: JSON.stringify(data) }),
        get: (id: string) => fetchJson(`/signatures/${id}`),
        getByDocument: (documentId: string) => fetchJson(`/signatures/document/${documentId}`),
        getByMatter: (matterId: string) => fetchJson(`/signatures/matter/${matterId}`),
        sign: (id: string) => fetchJson(`/signatures/${id}/sign`, { method: 'POST' }),
        decline: (id: string, reason?: string) => fetchJson(`/signatures/${id}/decline`, { method: 'POST', body: JSON.stringify({ reason }) }),
        remind: (id: string) => fetchJson(`/signatures/${id}/remind`, { method: 'POST' }),
        void: (id: string) => fetchJson(`/signatures/${id}/void`, { method: 'POST' }),
        view: (id: string) => fetchJson(`/signatures/${id}/view`, { method: 'POST' }),
        verify: (id: string, data: { method?: string; passed: boolean; notes?: string }) =>
            fetchJson(`/signatures/${id}/verify`, { method: 'POST', body: JSON.stringify(data) }),
        auditTrail: (id: string) => fetchJson(`/signatures/${id}/audit-trail`),
    },

    // Online Payments
    payments: {
        createCheckout: (data: { invoiceId?: string; matterId?: string; clientId?: string; amount: number; currency?: string; payerEmail?: string; payerName?: string }) =>
            fetchJson('/payments/create-checkout', { method: 'POST', body: JSON.stringify(data) }),
        get: (id: string) => fetchJson(`/payments/${id}`),
        getByInvoice: (invoiceId: string) => fetchJson(`/payments/invoice/${invoiceId}`),
        getByMatter: (matterId: string) => fetchJson(`/payments/matter/${matterId}`),
        getByClient: (clientId: string) => fetchJson(`/payments/client/${clientId}`),
        complete: (id: string, data: { externalTransactionId?: string; cardLast4?: string; cardBrand?: string; receiptUrl?: string }) =>
            fetchJson(`/payments/${id}/complete`, { method: 'POST', body: JSON.stringify(data) }),
        fail: (id: string, reason?: string) => fetchJson(`/payments/${id}/fail`, { method: 'POST', body: JSON.stringify({ reason }) }),
        refund: (id: string, data: { amount?: number; reason?: string }) =>
            fetchJson(`/payments/${id}/refund`, { method: 'POST', body: JSON.stringify(data) }),
        stats: (from?: string, to?: string) => fetchJson(`/payments/stats${from || to ? `?from=${from || ''}&to=${to || ''}` : ''}`),
    },

    // Payment Plans
    paymentPlans: {
        list: (params?: { clientId?: string; invoiceId?: string; status?: string }) => {
            const qs = new URLSearchParams();
            if (params?.clientId) qs.set('clientId', params.clientId);
            if (params?.invoiceId) qs.set('invoiceId', params.invoiceId);
            if (params?.status) qs.set('status', params.status);
            const query = qs.toString() ? `?${qs.toString()}` : '';
            return fetchJson(`/payment-plans${query}`);
        },
        create: (data: any) => fetchJson('/payment-plans', { method: 'POST', body: JSON.stringify(data) }),
        update: (id: string, data: any) => fetchJson(`/payment-plans/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
        run: (id: string) => fetchJson(`/payment-plans/${id}/run`, { method: 'POST' }),
        runDue: (limit?: number) =>
            fetchJson(`/payment-plans/run-due${limit ? `?limit=${limit}` : ''}`, { method: 'POST' })
    },

    // Deadlines
    deadlines: {
        list: (params?: { matterId?: string; status?: string; days?: number }) => {
            const query = new URLSearchParams();
            if (params?.matterId) query.append('matterId', params.matterId);
            if (params?.status) query.append('status', params.status);
            if (params?.days) query.append('days', params.days.toString());
            return fetchJson(`/deadlines?${query.toString()}`);
        },
        get: (id: string) => fetchJson(`/deadlines/${id}`),
        create: (data: { matterId: string; title: string; dueDate: string; description?: string; priority?: string; deadlineType?: string; assignedTo?: string; reminderDays?: number }) =>
            fetchJson('/deadlines', { method: 'POST', body: JSON.stringify(data) }),
        update: (id: string, data: { title?: string; description?: string; dueDate?: string; status?: string; priority?: string; assignedTo?: string; reminderDays?: number }) =>
            fetchJson(`/deadlines/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
        complete: (id: string) => fetchJson(`/deadlines/${id}/complete`, { method: 'POST' }),
        delete: (id: string) => fetchJson(`/deadlines/${id}`, { method: 'DELETE' }),
        upcoming: (days?: number) => fetchJson(`/deadlines/upcoming${days ? `?days=${days}` : ''}`),
        calculate: (data: { courtRuleId: string; triggerDate?: string; serviceMethod?: string }) =>
            fetchJson('/deadlines/calculate', { method: 'POST', body: JSON.stringify(data) }),
    },
    holidays: {
        list: (jurisdiction?: string) =>
            fetchJson(`/holidays${jurisdiction ? `?jurisdiction=${encodeURIComponent(jurisdiction)}` : ''}`)
    },

    // Court Rules
    courtRules: {
        list: (params?: { jurisdiction?: string; ruleType?: string; triggerEvent?: string }) => {
            const query = new URLSearchParams();
            if (params?.jurisdiction) query.append('jurisdiction', params.jurisdiction);
            if (params?.ruleType) query.append('ruleType', params.ruleType);
            if (params?.triggerEvent) query.append('triggerEvent', params.triggerEvent);
            return fetchJson(`/court-rules?${query.toString()}`);
        },
        get: (id: string) => fetchJson(`/court-rules/${id}`),
        create: (data: any) => fetchJson('/court-rules', { method: 'POST', body: JSON.stringify(data) }),
        update: (id: string, data: any) => fetchJson(`/court-rules/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
        delete: (id: string) => fetchJson(`/court-rules/${id}`, { method: 'DELETE' }),
        jurisdictions: () => fetchJson('/court-rules/jurisdictions'),
        triggerEvents: (jurisdiction?: string) => fetchJson(`/court-rules/trigger-events${jurisdiction ? `?jurisdiction=${jurisdiction}` : ''}`),
        seed: () => fetchJson('/court-rules/seed', { method: 'POST' }),
    },

    // Email Sync
    emails: {
        list: (params?: { matterId?: string; clientId?: string; folder?: string; limit?: number }) => {
            const query = new URLSearchParams();
            if (params?.matterId) query.append('matterId', params.matterId);
            if (params?.clientId) query.append('clientId', params.clientId);
            if (params?.folder) query.append('folder', params.folder);
            if (params?.limit) query.append('limit', params.limit.toString());
            return fetchJson(`/emails?${query.toString()}`);
        },
        get: (id: string) => fetchJson(`/emails/${id}`),
        link: (id: string, data: { matterId?: string; clientId?: string }) =>
            fetchJson(`/emails/${id}/link`, { method: 'POST', body: JSON.stringify(data) }),
        unlink: (id: string) => fetchJson(`/emails/${id}/unlink`, { method: 'POST' }),
        unlinked: (limit?: number) => fetchJson(`/emails/unlinked${limit ? `?limit=${limit}` : ''}`),
        autoLink: () => fetchJson('/emails/auto-link', { method: 'POST' }),
        // Accounts
        accounts: {
            list: () => fetchJson('/emails/accounts'),
            connectOutlook: (data: { email: string; displayName?: string; accessToken?: string; refreshToken?: string }) =>
                fetchJson('/emails/accounts/connect/outlook', { method: 'POST', body: JSON.stringify(data) }),
            connectGmail: (data: { email: string; displayName?: string; accessToken?: string; refreshToken?: string }) =>
                fetchJson('/emails/accounts/connect/gmail', { method: 'POST', body: JSON.stringify(data) }),
            sync: (id: string) => fetchJson(`/emails/accounts/${id}/sync`, { method: 'POST' }),
            disconnect: (id: string) => fetchJson(`/emails/accounts/${id}`, { method: 'DELETE' }),
        },
    },

    // SMS Messaging (Twilio)
    sms: {
        send: (data: { toNumber: string; body: string; matterId?: string; clientId?: string; templateId?: string }) =>
            fetchJson('/sms/send', { method: 'POST', body: JSON.stringify(data) }),
        list: (params?: { clientId?: string; matterId?: string; limit?: number }) => {
            const query = new URLSearchParams();
            if (params?.clientId) query.append('clientId', params.clientId);
            if (params?.matterId) query.append('matterId', params.matterId);
            if (params?.limit) query.append('limit', params.limit.toString());
            return fetchJson(`/sms?${query.toString()}`);
        },
        conversation: (phoneNumber: string, limit?: number) =>
            fetchJson(`/sms/conversation/${encodeURIComponent(phoneNumber)}${limit ? `?limit=${limit}` : ''}`),
        // Templates
        templates: {
            list: (category?: string) => fetchJson(`/sms/templates${category ? `?category=${category}` : ''}`),
            create: (data: { name: string; body: string; category?: string; variables?: string }) =>
                fetchJson('/sms/templates', { method: 'POST', body: JSON.stringify(data) }),
            update: (id: string, data: any) => fetchJson(`/sms/templates/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
            delete: (id: string) => fetchJson(`/sms/templates/${id}`, { method: 'DELETE' }),
            seed: () => fetchJson('/sms/templates/seed', { method: 'POST' }),
        },
        // Reminders
        reminders: {
            list: (params?: { status?: string; days?: number }) => {
                const query = new URLSearchParams();
                if (params?.status) query.append('status', params.status);
                if (params?.days) query.append('days', params.days.toString());
                return fetchJson(`/sms/reminders?${query.toString()}`);
            },
            create: (data: { reminderType: string; toNumber: string; message: string; scheduledFor: string; entityId?: string; clientId?: string }) =>
                fetchJson('/sms/reminders', { method: 'POST', body: JSON.stringify(data) }),
            cancel: (id: string) => fetchJson(`/sms/reminders/${id}/cancel`, { method: 'POST' }),
            process: () => fetchJson('/sms/reminders/process', { method: 'POST' }),
        },
    },

    // Intake Forms
    intake: {
        // Forms
        forms: {
            list: (activeOnly?: boolean) => fetchJson(`/intake/forms${activeOnly !== undefined ? `?activeOnly=${activeOnly}` : ''}`),
            get: (id: string) => fetchJson(`/intake/forms/${id}`),
            create: (data: { name: string; description?: string; practiceArea?: string; fieldsJson?: string }) =>
                fetchJson('/intake/forms', { method: 'POST', body: JSON.stringify(data) }),
            update: (id: string, data: any) => fetchJson(`/intake/forms/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
            delete: (id: string) => fetchJson(`/intake/forms/${id}`, { method: 'DELETE' }),
        },
        // Public form
        public: {
            get: (slug: string) => fetchJson(`/intake/public/${slug}`),
            submit: (slug: string, dataJson: string) =>
                fetchJson(`/intake/public/${slug}/submit`, { method: 'POST', body: JSON.stringify({ dataJson }) }),
        },
        // Submissions
        submissions: {
            list: (params?: { formId?: string; status?: string; limit?: number }) => {
                const query = new URLSearchParams();
                if (params?.formId) query.append('formId', params.formId);
                if (params?.status) query.append('status', params.status);
                if (params?.limit) query.append('limit', params.limit.toString());
                return fetchJson(`/intake/submissions?${query.toString()}`);
            },
            get: (id: string) => fetchJson(`/intake/submissions/${id}`),
            review: (id: string, data: { status: string; notes?: string }) =>
                fetchJson(`/intake/submissions/${id}/review`, { method: 'POST', body: JSON.stringify(data) }),
            convertToLead: (id: string) => fetchJson(`/intake/submissions/${id}/convert-to-lead`, { method: 'POST' }),
            delete: (id: string) => fetchJson(`/intake/submissions/${id}`, { method: 'DELETE' }),
        },
    },

    // AI Innovation Suite
    ai: {
        // Legal Research
        research: {
            start: (data: { query: string; title?: string; matterId?: string; jurisdiction?: string; practiceArea?: string }) =>
                fetchJson('/ai/research', { method: 'POST', body: JSON.stringify(data) }),
            list: (params?: { matterId?: string; limit?: number }) => {
                const query = new URLSearchParams();
                if (params?.matterId) query.append('matterId', params.matterId);
                if (params?.limit) query.append('limit', params.limit.toString());
                return fetchJson(`/ai/research?${query.toString()}`);
            },
            get: (id: string) => fetchJson(`/ai/research/${id}`),
        },
        // Contract Analysis
        contracts: {
            analyze: (data: { documentId: string; documentContent: string; matterId?: string; contractType?: string }) =>
                fetchJson('/ai/analyze-contract', { method: 'POST', body: JSON.stringify(data) }),
            list: (params?: { documentId?: string; matterId?: string }) => {
                const query = new URLSearchParams();
                if (params?.documentId) query.append('documentId', params.documentId);
                if (params?.matterId) query.append('matterId', params.matterId);
                return fetchJson(`/ai/contract-analyses?${query.toString()}`);
            },
            get: (id: string) => fetchJson(`/ai/contract-analyses/${id}`),
        },
        // Case Prediction
        predictions: {
            predict: (data: { matterId: string; additionalContext?: string }) =>
                fetchJson('/ai/predict-case', { method: 'POST', body: JSON.stringify(data) }),
            list: (matterId: string) => fetchJson(`/ai/predictions/${matterId}`),
        },
        // Evidence-Linked Drafting
        evidenceDrafts: {
            create: (data: any) =>
                fetchJson('/ai/drafts/evidence-linked', { method: 'POST', body: JSON.stringify(data) }),
            generate: (data: any) =>
                fetchJson('/ai/drafts/evidence-linked/generate', { method: 'POST', body: JSON.stringify(data) }),
            get: (id: string) => fetchJson(`/ai/drafts/${id}`),
            verify: (id: string, data?: { createReviewQueueItems?: boolean }) =>
                fetchJson(`/ai/drafts/${id}/verify`, { method: 'POST', body: JSON.stringify(data ?? {}) }),
            batchReverify: (data?: { draftOutputIds?: string[]; createReviewQueueItems?: boolean; days?: number; limit?: number }) =>
                fetchJson('/ai/drafts/evidence-linked/batch-reverify', { method: 'POST', body: JSON.stringify(data ?? {}) }),
            metrics: (params?: { days?: number }) => {
                const query = new URLSearchParams();
                if (params?.days) query.set('days', String(params.days));
                return fetchJson(`/ai/drafts/evidence-linked/metrics${query.toString() ? `?${query.toString()}` : ''}`);
            },
            reviewClaim: (draftId: string, claimId: string, data: { action: string; statusOverride?: string; reviewerNotes?: string; approverReason?: string; rewrittenText?: string }) =>
                fetchJson(`/ai/drafts/${draftId}/claims/${claimId}/review`, { method: 'POST', body: JSON.stringify(data) }),
            publish: (id: string, data: { policy?: string; lowConfidenceThreshold?: number }) =>
                fetchJson(`/ai/drafts/${id}/publish`, { method: 'POST', body: JSON.stringify(data) }),
            exportEvidence: (id: string) =>
                fetchJson(`/ai/drafts/${id}/evidence-export`),
        },
    },

    // Client Transparency (staff reviewer workflow)
    clientTransparency: {
        getCurrent: (matterId: string) =>
            fetchJson(`/client-transparency/matter/${encodeURIComponent(matterId)}`),
        regenerate: (matterId: string, data?: any) =>
            fetchJson(`/client-transparency/matter/${encodeURIComponent(matterId)}/regenerate`, { method: 'POST', body: JSON.stringify(data ?? {}) }),
        history: (matterId: string, params?: { limit?: number }) => {
            const query = new URLSearchParams();
            if (typeof params?.limit === 'number') query.set('limit', String(params.limit));
            return fetchJson(`/client-transparency/matter/${encodeURIComponent(matterId)}/history${query.toString() ? `?${query.toString()}` : ''}`);
        },
        trigger: (data: any) =>
            fetchJson('/client-transparency/triggers', { method: 'POST', body: JSON.stringify(data ?? {}) }),
        getReviewWorkspace: (matterId: string) =>
            fetchJson(`/client-transparency/matter/${encodeURIComponent(matterId)}/review-workspace`),
        upsertMatterPolicy: (matterId: string, data: any) =>
            fetchJson(`/client-transparency/matter/${encodeURIComponent(matterId)}/policy`, { method: 'POST', body: JSON.stringify(data ?? {}) }),
      reviewSnapshot: (snapshotId: string, data: any) =>
          fetchJson(`/client-transparency/snapshots/${encodeURIComponent(snapshotId)}/review`, { method: 'POST', body: JSON.stringify(data ?? {}) }),
      publishSnapshot: (snapshotId: string, data?: any) =>
          fetchJson(`/client-transparency/snapshots/${encodeURIComponent(snapshotId)}/publish`, { method: 'POST', body: JSON.stringify(data ?? {}) }),
      getSnapshotEvidence: (snapshotId: string) =>
          fetchJson(`/client-transparency/snapshots/${encodeURIComponent(snapshotId)}/evidence`),
      batchReverifyEvidence: (data?: any) =>
          fetchJson('/client-transparency/batch-reverify', { method: 'POST', body: JSON.stringify(data ?? {}) }),
      metrics: (params?: { days?: number; matterId?: string }) => {
          const query = new URLSearchParams();
          if (typeof params?.days === 'number') query.set('days', String(params.days));
          if (params?.matterId) query.set('matterId', params.matterId);
          return fetchJson(`/client-transparency/metrics${query.toString() ? `?${query.toString()}` : ''}`);
      },
      },

    // Outcome-to-Fee Planner
    outcomeFeePlans: {
        generate: (data: any) =>
            fetchJson('/outcome-fee-plans/generate', { method: 'POST', body: JSON.stringify(data) }) as Promise<OutcomeFeePlanDetailResult | null>,
        getLatestForMatter: (matterId: string) =>
            fetchJson(`/outcome-fee-plans/matter/${encodeURIComponent(matterId)}`) as Promise<OutcomeFeePlanDetailResult | null>,
        get: (planId: string) =>
            fetchJson(`/outcome-fee-plans/${encodeURIComponent(planId)}`) as Promise<OutcomeFeePlanDetailResult | null>,
        listVersions: (planId: string) =>
            fetchJson(`/outcome-fee-plans/${encodeURIComponent(planId)}/versions`) as Promise<OutcomeFeePlanVersion[] | null>,
        compare: (planId: string, params?: { fromVersionId?: string; toVersionId?: string }) => {
            const query = new URLSearchParams();
            if (params?.fromVersionId) query.set('fromVersionId', params.fromVersionId);
            if (params?.toVersionId) query.set('toVersionId', params.toVersionId);
            return fetchJson(`/outcome-fee-plans/${encodeURIComponent(planId)}/compare${query.toString() ? `?${query.toString()}` : ''}`) as Promise<OutcomeFeePlanVersionCompareResult | null>;
        },
        recompute: (planId: string, data?: any) =>
            fetchJson(`/outcome-fee-plans/${encodeURIComponent(planId)}/recompute`, { method: 'POST', body: JSON.stringify(data ?? {}) }) as Promise<OutcomeFeePlanDetailResult | null>,
        trigger: (data: any) =>
            fetchJson('/outcome-fee-plans/triggers', { method: 'POST', body: JSON.stringify(data ?? {}) }) as Promise<OutcomeFeePlanTriggerResult | null>,
        metrics: (params?: { days?: number }) => {
            const query = new URLSearchParams();
            if (params?.days) query.set('days', String(params.days));
            return fetchJson(`/outcome-fee-plans/metrics${query.toString() ? `?${query.toString()}` : ''}`) as Promise<OutcomeFeePlanPortfolioMetricsResult | null>;
        },
        listCalibrationSnapshots: (params?: { status?: string; cohortKey?: string; limit?: number }) => {
            const query = new URLSearchParams();
            if (params?.status) query.set('status', params.status);
            if (params?.cohortKey) query.set('cohortKey', params.cohortKey);
            if (typeof params?.limit === 'number') query.set('limit', String(params.limit));
            return fetchJson(`/outcome-fee-plans/calibration/snapshots${query.toString() ? `?${query.toString()}` : ''}`) as Promise<OutcomeFeeCalibrationSnapshotsListResult | null>;
        },
        getEffectiveCalibrationForMatter: (matterId: string) =>
            fetchJson(`/outcome-fee-plans/calibration/effective/matter/${encodeURIComponent(matterId)}`) as Promise<OutcomeFeeCalibrationEffectiveResult | null>,
        runCalibrationJob: (data?: any) =>
            fetchJson('/outcome-fee-plans/calibration/jobs/run', { method: 'POST', body: JSON.stringify(data ?? {}) }) as Promise<OutcomeFeeCalibrationJobRunResult | null>,
        recordOutcomeFeedback: (planId: string, data: any) =>
            fetchJson(`/outcome-fee-plans/${encodeURIComponent(planId)}/outcome-feedback`, { method: 'POST', body: JSON.stringify(data ?? {}) }) as Promise<OutcomeFeeOutcomeFeedbackResult | null>,
        activateCalibrationSnapshot: (snapshotId: string, data?: { asShadow?: boolean; reason?: string; correlationId?: string }) =>
            fetchJson(`/outcome-fee-plans/calibration/snapshots/${encodeURIComponent(snapshotId)}/activate`, { method: 'POST', body: JSON.stringify(data ?? {}) }),
        rollbackCalibrationSnapshot: (snapshotId: string, data?: { targetSnapshotId?: string; reason?: string; correlationId?: string }) =>
            fetchJson(`/outcome-fee-plans/calibration/snapshots/${encodeURIComponent(snapshotId)}/rollback`, { method: 'POST', body: JSON.stringify(data ?? {}) }),
    },

    // Staff Direct Messages
    staffMessages: {
        list: (userId?: string) =>
            fetchJson(`/staffmessages${userId ? `?userId=${encodeURIComponent(userId)}` : ''}`),
        thread: (userA: string, userB: string) =>
            fetchJson(`/staffmessages/thread?userA=${encodeURIComponent(userA)}&userB=${encodeURIComponent(userB)}`),
        send: (data: { senderId: string; recipientId: string; body: string }) =>
            fetchJson('/staffmessages', { method: 'POST', body: JSON.stringify(data) }),
        markRead: (id: string) =>
            fetchJson(`/staffmessages/${id}/read`, { method: 'POST' }),
    },
};

