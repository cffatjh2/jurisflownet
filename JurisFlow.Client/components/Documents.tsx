import React, { useRef, useState, useEffect } from 'react';
import { DocumentFile, DocumentCategory, DocumentStatus } from '../types';
import { Folder, FileText, Search, Plus, Filter, X, Trash2 } from './Icons';
import { useTranslation } from '../contexts/LanguageContext';
import { useData } from '../contexts/DataContext';
import { api } from '../services/api';
import mammoth from 'mammoth';
import { googleDocsService, type GoogleDoc } from '../services/googleDocsService';
import { toast } from './Toast';
import { useConfirm } from './ConfirmDialog';
import { getGoogleClientId } from '../services/googleConfig';
import { clearOAuthTokens, getOAuthAccessToken, requestOAuthState } from '../services/oauthSecurity';
import { getCurrentAppReturnPath } from '../services/returnPath';
import SignatureRequestModal from './SignatureRequestModal';
import SignatureStatus from './SignatureStatus';

const Documents: React.FC = () => {
  const { t, formatDate } = useTranslation();
  const { matters, clients, documents, addDocument, updateDocument, deleteDocument, bulkAssignDocuments } = useData();
  const { confirm } = useConfirm();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const versionFileInputRef = useRef<HTMLInputElement>(null);
  const [showFilter, setShowFilter] = useState(false);
  const [filterType, setFilterType] = useState('all');
  const [selectedMatter, setSelectedMatter] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [viewingDoc, setViewingDoc] = useState<DocumentFile | null>(null);
  const [docContent, setDocContent] = useState<string>('');
  const [loadingContent, setLoadingContent] = useState(false);
  const [docObjectUrl, setDocObjectUrl] = useState<string | null>(null);
  const [showMatterModal, setShowMatterModal] = useState(false);
  const [pendingFile, setPendingFile] = useState<File | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [selectedMatterForUpload, setSelectedMatterForUpload] = useState<string>('');
  const [editingDoc, setEditingDoc] = useState<DocumentFile | null>(null);
  const [editMatterId, setEditMatterId] = useState<string>('');
  const [editTags, setEditTags] = useState<string>('');
  const [isGoogleDocsConnected, setIsGoogleDocsConnected] = useState(false);
  const [googleDocsAccessToken, setGoogleDocsAccessToken] = useState<string | null>(
    getOAuthAccessToken('google-docs')
  );
  const [isSyncingGoogleDocs, setIsSyncingGoogleDocs] = useState(false);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [bulkMatterId, setBulkMatterId] = useState<string>('');
  const [editCategory, setEditCategory] = useState<string>('');
  const [editStatus, setEditStatus] = useState<string>('');
  const [editLegalHoldReason, setEditLegalHoldReason] = useState('');
  const [searchResults, setSearchResults] = useState<DocumentFile[]>([]);
  const [isSearchingContent, setIsSearchingContent] = useState(false);
  const [searchInContent, setSearchInContent] = useState(false);
  const [showSignatureModal, setShowSignatureModal] = useState(false);
  const [signatureDocumentId, setSignatureDocumentId] = useState<string | null>(null);
  const [signatureMatterId, setSignatureMatterId] = useState<string | undefined>(undefined);
  const [showVersionModal, setShowVersionModal] = useState(false);
  const [versionDoc, setVersionDoc] = useState<DocumentFile | null>(null);
  const [versionList, setVersionList] = useState<any[]>([]);
  const [versionLoading, setVersionLoading] = useState(false);
  const [diffLeftId, setDiffLeftId] = useState('');
  const [diffRightId, setDiffRightId] = useState('');
  const [diffResult, setDiffResult] = useState('');
  const [showShareModal, setShowShareModal] = useState(false);
  const [shareDoc, setShareDoc] = useState<DocumentFile | null>(null);
  const [shareClientId, setShareClientId] = useState('');
  const [sharePermissions, setSharePermissions] = useState({
    canView: true,
    canDownload: true,
    canComment: true,
    canUpload: false
  });
  const [shareNote, setShareNote] = useState('');
  const [shareExpiresAt, setShareExpiresAt] = useState('');
  const [shareLoading, setShareLoading] = useState(false);
  const [shareMap, setShareMap] = useState<Record<string, any>>({});

  useEffect(() => {
    setIsGoogleDocsConnected(!!googleDocsAccessToken);
  }, [googleDocsAccessToken]);

  const GOOGLE_DOC_EXPORT_MIME = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
  const GOOGLE_DOC_ID_TAG_PREFIX = 'source:google-doc-id:';
  const GOOGLE_DOC_UPDATED_TAG_PREFIX = 'source:google-doc-updated:';
  const GOOGLE_DOC_LINK_TAG_PREFIX = 'source:google-doc-link:';

  const getNormalizedDocumentDate = (dateString?: string | null) => {
    if (!dateString) {
      return new Date().toISOString();
    }

    const date = new Date(dateString);
    return Number.isNaN(date.getTime()) ? new Date().toISOString() : date.toISOString();
  };

  const inferDocumentType = (mimeType?: string): DocumentFile['type'] => {
    const mime = (mimeType || '').toLowerCase();
    if (mime.includes('pdf')) return 'pdf';
    if (mime.includes('word') || mime.includes('officedocument') || mime.includes('msword')) return 'docx';
    if (mime.includes('text') || mime.includes('csv')) return 'txt';
    if (mime.includes('image')) return 'img';
    return 'txt';
  };

  const formatDocumentSize = (fileSize?: number) =>
    typeof fileSize === 'number' ? `${(fileSize / 1024 / 1024).toFixed(2)} MB` : undefined;

  const mapApiDocumentToDocumentFile = (doc: any): DocumentFile => ({
    id: doc.id,
    name: doc.name,
    type: inferDocumentType(doc.mimeType),
    size: formatDocumentSize(doc.fileSize),
    fileSize: doc.fileSize,
    updatedAt: doc.updatedAt || doc.createdAt || new Date().toISOString(),
    matterId: doc.matterId || undefined,
    filePath: doc.filePath || undefined,
    downloadUrl: doc.downloadUrl || undefined,
    mimeType: doc.mimeType || undefined,
    description: doc.description || undefined,
    tags: parseTags(doc.tags),
    category: doc.category || undefined,
    status: doc.status || undefined,
    version: doc.version || undefined,
    uploadedBy: doc.uploadedBy || undefined,
    legalHoldReason: doc.legalHoldReason || undefined,
    legalHoldPlacedAt: doc.legalHoldPlacedAt || undefined,
    legalHoldReleasedAt: doc.legalHoldReleasedAt || undefined,
    legalHoldPlacedBy: doc.legalHoldPlacedBy || undefined
  });

  const hasServerDocument = (doc: DocumentFile) => Boolean(doc.downloadUrl || doc.filePath);
  const isExternalLinkDocument = (doc: DocumentFile) => !hasServerDocument(doc) && !!doc.content && /^https?:\/\//i.test(doc.content);
  const getDocumentDownloadEndpoint = (doc: DocumentFile) => doc.downloadUrl || (hasServerDocument(doc) ? `/documents/${doc.id}/download` : null);
  const ensureDocxFileName = (name: string) => /\.[a-z0-9]+$/i.test(name) ? name : `${name}.docx`;
  const getGoogleDocIdTag = (googleDocId: string) => `${GOOGLE_DOC_ID_TAG_PREFIX}${googleDocId}`;
  const getGoogleDocUpdatedTag = (updatedAt: string) => `${GOOGLE_DOC_UPDATED_TAG_PREFIX}${updatedAt}`;
  const getGoogleDocLinkTag = (link?: string) => `${GOOGLE_DOC_LINK_TAG_PREFIX}${link || ''}`;

  const withoutGoogleSyncTags = (tags?: string[]) =>
    (tags || []).filter(tag =>
      !tag.startsWith(GOOGLE_DOC_ID_TAG_PREFIX)
      && !tag.startsWith(GOOGLE_DOC_UPDATED_TAG_PREFIX)
      && !tag.startsWith(GOOGLE_DOC_LINK_TAG_PREFIX));

  const buildGoogleDocTags = (existingTags: string[] | undefined, googleDocId: string, updatedAt: string, webViewLink?: string) => {
    const nextTags = [
      ...withoutGoogleSyncTags(existingTags),
      getGoogleDocIdTag(googleDocId),
      getGoogleDocUpdatedTag(updatedAt)
    ];

    if (webViewLink) {
      nextTags.push(getGoogleDocLinkTag(webViewLink));
    }

    return Array.from(new Set(nextTags));
  };

  const findImportedGoogleDoc = (googleDocId: string) =>
    documents.find(doc => (doc.tags || []).includes(getGoogleDocIdTag(googleDocId)));

  const selectAll = () => {
    if (selectedIds.length === filteredDocs.length) {
      setSelectedIds([]);
    } else {
      setSelectedIds(filteredDocs.map(d => d.id));
    }
  };

  const handleGoogleDocsConnect = async () => {
    const clientId = getGoogleClientId();

    if (!clientId) return;

    try {
      const state = await requestOAuthState('google', 'google-docs', getCurrentAppReturnPath('/#documents'));
      const redirectUri = `${window.location.origin}/auth/google/callback`;
      const scope = 'https://www.googleapis.com/auth/documents.readonly https://www.googleapis.com/auth/drive.readonly';
      const authUrl = `https://accounts.google.com/o/oauth2/v2/auth?client_id=${clientId}&redirect_uri=${encodeURIComponent(redirectUri)}&response_type=code&scope=${encodeURIComponent(scope)}&access_type=offline&prompt=consent&state=${encodeURIComponent(state)}`;
      window.location.href = authUrl;
    } catch (error) {
      console.error('Failed to initialize Google Docs OAuth', error);
      toast.error('Failed to start Google Docs connection. Please try again.');
    }
  };

  const handleGoogleDocsSync = async () => {
    if (!googleDocsAccessToken || isSyncingGoogleDocs) return;

    const syncSingleGoogleDoc = async (doc: GoogleDoc) => {
      const updatedAt = getNormalizedDocumentDate(doc.modifiedTime || doc.createdTime);
      const existing = findImportedGoogleDoc(doc.id);
      const nextTags = buildGoogleDocTags(existing?.tags, doc.id, updatedAt, doc.webViewLink);
      const syncDescription = existing?.description?.trim() || 'Imported from Google Docs';

      if (existing && (existing.tags || []).includes(getGoogleDocUpdatedTag(updatedAt))) {
        if (documents.some(item => item.id === doc.id && isExternalLinkDocument(item))) {
          deleteDocument(doc.id);
        }
        return 'skipped' as const;
      }

      const exportedBlob = await googleDocsService.exportDocument(
        googleDocsAccessToken,
        doc.id,
        GOOGLE_DOC_EXPORT_MIME
      );

      const exportedFile = new File(
        [exportedBlob],
        ensureDocxFileName(doc.name),
        {
          type: GOOGLE_DOC_EXPORT_MIME,
          lastModified: Date.parse(updatedAt) || Date.now()
        }
      );

      if (existing) {
        const uploadedVersion = await api.uploadDocumentVersion(existing.id, exportedFile);
        const updatedMetadata = await api.updateDocument(existing.id, {
          tags: nextTags,
          category: existing.category || 'Google Docs',
          description: syncDescription
        });

        addDocument(mapApiDocumentToDocumentFile({
          ...(uploadedVersion || {}),
          ...(updatedMetadata || {}),
          id: existing.id,
          tags: nextTags,
          category: existing.category || 'Google Docs',
          description: syncDescription
        }));
      } else {
        const uploadedDoc = await api.uploadDocument(exportedFile, undefined, syncDescription);
        if (!uploadedDoc) {
          return 'skipped' as const;
        }

        const updatedMetadata = await api.updateDocument(uploadedDoc.id, {
          tags: nextTags,
          category: 'Google Docs',
          description: syncDescription
        });

        addDocument(mapApiDocumentToDocumentFile({
          ...uploadedDoc,
          ...(updatedMetadata || {}),
          tags: nextTags,
          category: 'Google Docs',
          description: syncDescription
        }));
      }

      if (documents.some(item => item.id === doc.id && isExternalLinkDocument(item))) {
        deleteDocument(doc.id);
      }

      return existing ? 'updated' as const : 'created' as const;
    };

    setIsSyncingGoogleDocs(true);
    try {
      const docs = await googleDocsService.getDocuments(googleDocsAccessToken);
      const summary = { created: 0, updated: 0, skipped: 0, failed: 0 };
      const queue = [...docs];
      const workerCount = Math.min(3, queue.length || 1);

      await Promise.all(Array.from({ length: workerCount }, async () => {
        while (queue.length > 0) {
          const nextDoc = queue.shift();
          if (!nextDoc) {
            return;
          }

          try {
            const outcome = await syncSingleGoogleDoc(nextDoc);
            summary[outcome] += 1;
          } catch (error) {
            summary.failed += 1;
            console.error('Failed to sync Google Doc', nextDoc.id, error);
          }
        }
      }));

      if (summary.failed > 0) {
        toast.warning(`Google Docs sync finished: ${summary.created} added, ${summary.updated} updated, ${summary.skipped} unchanged, ${summary.failed} failed.`);
      } else {
        toast.success(`Google Docs sync finished: ${summary.created} added, ${summary.updated} updated, ${summary.skipped} unchanged.`);
      }
    } catch (error) {
      console.error('Google Docs sync error:', error);
      toast.error('Failed to sync Google Docs. Please reconnect.');
      clearOAuthTokens('google-docs');
      setGoogleDocsAccessToken(null);
    } finally {
      setIsSyncingGoogleDocs(false);
    }
  };

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      const file = e.target.files[0];
      setPendingFile(file);
      setShowMatterModal(true);
    }
  };

  const buildUploadedDocument = (uploadedDoc: any): DocumentFile => mapApiDocumentToDocumentFile(uploadedDoc);

  const finalizeUploadedDocument = (uploadedDoc: any) => {
    const doc = buildUploadedDocument(uploadedDoc);
    addDocument(doc);
    setShowMatterModal(false);
    setPendingFile(null);
    setSelectedMatterForUpload('');
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  const handleConfirmUpload = async () => {
    if (!pendingFile) return;

    setIsUploading(true);
    try {
      // Upload to server
      const uploadedDoc = await api.uploadDocument(
        pendingFile,
        selectedMatterForUpload || undefined,
        undefined
      );

      if (uploadedDoc) {
        finalizeUploadedDocument(uploadedDoc);
        toast.success('File uploaded successfully.');
      }
    } catch (error: any) {
      console.error('Upload error:', error);
      const errorMessage = error?.message || 'Unknown error';

      if (selectedMatterForUpload && /matter not found/i.test(errorMessage)) {
        try {
          const uploadedWithoutMatter = await api.uploadDocument(pendingFile, undefined, undefined);
          if (uploadedWithoutMatter) {
            finalizeUploadedDocument(uploadedWithoutMatter);
            toast.warning('Selected matter could not be linked. The file was uploaded as unassigned.');
            return;
          }
        } catch (retryError: any) {
          console.error('Upload retry without matter failed:', retryError);
        }
      }

      toast.error('File upload failed: ' + errorMessage);
    } finally {
      setIsUploading(false);
    }
  };

  const parseTags = (raw: any): string[] | undefined => {
    if (!raw) return undefined;
    if (Array.isArray(raw)) return raw.map(String);
    if (typeof raw === 'string') {
      try {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed)) return parsed.map(String);
      } catch {
        return raw.split(',').map(s => s.trim()).filter(Boolean);
      }
    }
    return undefined;
  };

  const getSearchHaystack = (doc: DocumentFile) => {
    return [
      doc.name,
      doc.description,
      ...(doc.tags || [])
    ]
      .filter(Boolean)
      .join(' ')
      .toLowerCase();
  };

  const searchTerm = searchQuery.trim().toLowerCase();
  const useContentSearch = searchInContent && searchTerm.length >= 2;

  const getMatterName = (matterId?: string) => {
    if (!matterId) return 'Unassigned';
    const matter = matters.find(m => m.id === matterId);
    return matter ? `${matter.caseNumber} - ${matter.name}` : 'Unknown Matter';
  };

  const closeViewer = () => {
    if (docObjectUrl) {
      URL.revokeObjectURL(docObjectUrl);
    }
    setDocObjectUrl(null);
    setViewingDoc(null);
    setDocContent('');
  };

  const handleOpen = async (doc: DocumentFile) => {
    if (doc.content && doc.content.startsWith('http')) {
      window.open(doc.content, '_blank', 'noopener,noreferrer');
      return;
    }

    setViewingDoc(doc);
    setLoadingContent(true);
    setDocContent('');
    if (docObjectUrl) {
      URL.revokeObjectURL(docObjectUrl);
      setDocObjectUrl(null);
    }

    try {
      // If file is stored on the server, load it from there.
      if (hasServerDocument(doc)) {
        const downloadEndpoint = getDocumentDownloadEndpoint(doc);
        if (!downloadEndpoint) {
          throw new Error('Document download failed');
        }

        const response = await api.downloadFile(downloadEndpoint);
        if (!response) {
          throw new Error('Document download failed');
        }

        if (doc.type === 'pdf') {
          const url = window.URL.createObjectURL(response.blob);
          setDocObjectUrl(url);
          setDocContent(url);
        } else if (doc.type === 'txt') {
          const text = await response.blob.text();
          setDocContent(text);
        } else if (doc.type === 'docx') {
          const arrayBuffer = await response.blob.arrayBuffer();
          const result = await mammoth.convertToHtml({ arrayBuffer });
          setDocContent(result.value);
        } else {
          const url = window.URL.createObjectURL(response.blob);
          setDocObjectUrl(url);
          setDocContent(url);
        }
      } else if (doc.content) {
        // Fallback to old content storage
        if (doc.type === 'txt') {
          const base64 = (doc.content as string).split(',')[1];
          const text = atob(base64);
          setDocContent(text);
        } else if (doc.type === 'docx') {
          const base64 = (doc.content as string).split(',')[1];
          const binaryString = atob(base64);
          const bytes = new Uint8Array(binaryString.length);
          for (let i = 0; i < binaryString.length; i++) {
            bytes[i] = binaryString.charCodeAt(i);
          }
          const arrayBuffer = bytes.buffer;
          const result = await mammoth.convertToHtml({ arrayBuffer });
          setDocContent(result.value);
        } else if (doc.type === 'pdf') {
          setDocContent(doc.content as string);
        } else {
          setDocContent(doc.content as string);
        }
      } else {
        toast.warning('No content is available for this file.');
        closeViewer();
      }
    } catch (error) {
      console.error('Error opening document:', error);
      toast.error('Unable to open the file.');
      closeViewer();
    } finally {
      setLoadingContent(false);
    }
  };

  const handleRequestSignature = (doc: DocumentFile) => {
    if (!hasServerDocument(doc)) {
      toast.warning('Sync Google Docs first so the file is imported before requesting a signature.');
      return;
    }

    setSignatureDocumentId(doc.id);
    setSignatureMatterId(doc.matterId || undefined);
    setShowSignatureModal(true);
  };

  const loadVersions = async (documentId: string) => {
    setVersionLoading(true);
    try {
      const versions = await api.getDocumentVersions(documentId);
      setVersionList(Array.isArray(versions) ? versions : []);
    } catch (error) {
      console.error('Failed to load document versions', error);
      toast.error('Unable to load version history.');
      setVersionList([]);
    } finally {
      setVersionLoading(false);
    }
  };

  const openVersionHistory = async (doc: DocumentFile) => {
    if (!hasServerDocument(doc)) {
      toast.warning('Version history is available for uploaded documents only.');
      return;
    }
    setVersionDoc(doc);
    setDiffLeftId('');
    setDiffRightId('');
    setDiffResult('');
    setShowVersionModal(true);
    await loadVersions(doc.id);
  };

  const handleDownloadVersion = async (versionId: string) => {
    try {
      const res = await api.downloadDocumentVersion(versionId);
      if (!res) return;
      const url = window.URL.createObjectURL(res.blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = res.filename || 'document-version';
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
    } catch (error: any) {
      console.error('Download version error:', error);
      toast.error('Failed to download version.');
    }
  };

  const handleRestoreVersion = async (versionId: string) => {
    const ok = await confirm({
      title: 'Restore version',
      message: 'Restore this version as the current document?',
      confirmText: 'Restore',
      cancelText: 'Cancel'
    });
    if (!ok || !versionDoc) return;
    try {
      const updated = await api.restoreDocumentVersion(versionId);
      if (updated) {
        updateDocument(versionDoc.id, {
          name: updated.name,
          downloadUrl: updated.downloadUrl,
          mimeType: updated.mimeType,
          fileSize: updated.fileSize,
          size: updated.fileSize ? `${(updated.fileSize / 1024 / 1024).toFixed(2)} MB` : undefined,
          version: updated.version,
          updatedAt: updated.updatedAt || updated.createdAt
        });
      }
      toast.success('Version restored.');
      await loadVersions(versionDoc.id);
    } catch (error: any) {
      console.error('Restore version error:', error);
      toast.error('Failed to restore version.');
    }
  };

  const handleUploadVersion = async (file: File) => {
    if (!versionDoc) return;
    try {
      const updated = await api.uploadDocumentVersion(versionDoc.id, file);
      if (updated) {
        updateDocument(versionDoc.id, {
          name: updated.name,
          downloadUrl: updated.downloadUrl,
          mimeType: updated.mimeType,
          fileSize: updated.fileSize,
          size: updated.fileSize ? `${(updated.fileSize / 1024 / 1024).toFixed(2)} MB` : undefined,
          version: updated.version,
          updatedAt: updated.updatedAt || updated.createdAt
        });
      }
      toast.success('New version uploaded.');
      await loadVersions(versionDoc.id);
    } catch (error: any) {
      console.error('Upload version error:', error);
      toast.error('Failed to upload new version.');
    }
  };

  const handleDiffVersions = async () => {
    if (!diffLeftId || !diffRightId) {
      toast.error('Select two versions to compare.');
      return;
    }
    try {
      const result = await api.diffDocumentVersions(diffLeftId, diffRightId);
      setDiffResult(result?.diff || '');
    } catch (error: any) {
      console.error('Diff error', error);
      toast.error('Unable to compare versions.');
    }
  };

  const openShareSettings = async (doc: DocumentFile) => {
    setShareDoc(doc);
    setShowShareModal(true);
    setShareLoading(true);
    setShareMap({});
    try {
      const shares = await api.getDocumentShares(doc.id);
      const map: Record<string, any> = {};
      (shares || []).forEach((share: any) => {
        if (share.clientId) map[share.clientId] = share;
      });
      setShareMap(map);

      let defaultClientId = '';
      if (doc.matterId) {
        const matter = matters.find(m => m.id === doc.matterId);
        defaultClientId = matter?.client?.id || '';
      }
      if (!defaultClientId && shares?.length) {
        defaultClientId = shares[0].clientId;
      }
      if (!defaultClientId && clients.length > 0) {
        defaultClientId = clients[0].id;
      }
      setShareClientId(defaultClientId);
    } catch (error) {
      console.error('Failed to load document shares', error);
    } finally {
      setShareLoading(false);
    }
  };

  useEffect(() => {
    if (!showShareModal) return;
    if (!shareClientId) return;
    const existing = shareMap[shareClientId];
    if (existing) {
      setSharePermissions({
        canView: !!existing.canView,
        canDownload: !!existing.canDownload,
        canComment: !!existing.canComment,
        canUpload: !!existing.canUpload
      });
      setShareNote(existing.note || '');
      setShareExpiresAt(existing.expiresAt ? existing.expiresAt.slice(0, 10) : '');
    } else {
      setSharePermissions({
        canView: true,
        canDownload: true,
        canComment: true,
        canUpload: false
      });
      setShareNote('');
      setShareExpiresAt('');
    }
  }, [shareClientId, shareMap, showShareModal]);

  const handleSaveShare = async () => {
    if (!shareDoc || !shareClientId) {
      toast.error('Select a client to share with.');
      return;
    }
    try {
      const payload = {
        clientId: shareClientId,
        canView: sharePermissions.canView,
        canDownload: sharePermissions.canDownload,
        canComment: sharePermissions.canComment,
        canUpload: sharePermissions.canUpload,
        note: shareNote.trim() || null,
        expiresAt: shareExpiresAt ? new Date(shareExpiresAt).toISOString() : null
      };
      const saved = await api.upsertDocumentShare(shareDoc.id, payload);
      setShareMap(prev => ({ ...prev, [shareClientId]: saved }));
      toast.success('Share settings saved.');
    } catch (error: any) {
      console.error('Failed to save share', error);
      toast.error('Unable to save share settings.');
    }
  };

  const handleRemoveShare = async () => {
    if (!shareDoc || !shareClientId) return;
    try {
      await api.removeDocumentShare(shareDoc.id, shareClientId);
      setShareMap(prev => {
        const copy = { ...prev };
        delete copy[shareClientId];
        return copy;
      });
      toast.success('Share removed.');
    } catch (error: any) {
      console.error('Failed to remove share', error);
      toast.error('Unable to remove share.');
    }
  };

  // Content-aware search against backend
  useEffect(() => {
    const q = searchQuery.trim();
    if (!searchInContent || q.length < 2) {
      setSearchResults([]);
      setIsSearchingContent(false);
      return;
    }

    let cancelled = false;
    const fetchSearch = async () => {
      try {
        setIsSearchingContent(true);
        const res = await api.searchDocuments(q, { matterId: selectedMatter || undefined, includeContent: true });
        if (cancelled) return;
        if (res) {
          // Map to DocumentFile shape
          const mapped: DocumentFile[] = res.map((d: any) => mapApiDocumentToDocumentFile(d));
          setSearchResults(mapped);
        } else {
          setSearchResults([]);
        }
      } catch (err) {
        console.error('Search error', err);
        setSearchResults([]);
      } finally {
        if (!cancelled) setIsSearchingContent(false);
      }
    };

    fetchSearch();
    return () => { cancelled = true; };
  }, [searchQuery, selectedMatter, searchInContent]);

  const activeDocs = useContentSearch ? searchResults : documents;

  const filteredDocs = activeDocs.filter(doc => {
    // Filter by matter if selected
    if (selectedMatter) {
      // If a matter is selected, only show documents for that matter
      if (doc.matterId !== selectedMatter) return false;
    }
    // If "My Files" is selected (selectedMatter === null), show ALL documents

    // Apply metadata search when not using content search
    if (!useContentSearch && searchTerm.length >= 2) {
      if (!getSearchHaystack(doc).includes(searchTerm)) return false;
    }

    // Filter by type or category
    if (filterType === 'all') return true;
    if (['pdf', 'docx', 'img'].includes(filterType)) {
      if (doc.type !== filterType) return false;
    } else {
      // Assume it's a category
      if (doc.category !== filterType) return false;
    }
    return true;
  });

  // Deep-link from Command Palette
  useEffect(() => {
    const targetId = localStorage.getItem('cmd_target_document');
    if (!targetId) return;
    const target = documents.find(d => d.id === targetId);
    if (target) {
      handleOpen(target);
      localStorage.removeItem('cmd_target_document');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [documents]);

  const handleDelete = async (doc: DocumentFile) => {
    if (!hasServerDocument(doc)) {
      deleteDocument(doc.id);
      toast.success('Unsynced document card removed.');
      return;
    }

    const ok = await confirm({
      title: 'Delete file',
      message: `Are you sure you want to delete "${doc.name}"?`,
      confirmText: 'Delete',
      cancelText: 'Cancel',
      variant: 'danger'
    });
    if (!ok) return;

    // Optimistically remove from UI
    deleteDocument(doc.id);

    try {
      await api.deleteDocument(doc.id);
      toast.success('File deleted.');
    } catch (error: any) {
      // Re-add document if deletion failed
      addDocument(doc);
      toast.error('Failed to delete file: ' + (error.message || 'Unknown error'));
    }
  };

  const handleDownload = async (doc: DocumentFile) => {
    try {
      if (hasServerDocument(doc)) {
        const downloadEndpoint = getDocumentDownloadEndpoint(doc);
        if (!downloadEndpoint) {
          throw new Error('File download failed');
        }

        const response = await api.downloadFile(downloadEndpoint);
        if (!response) {
          throw new Error('File download failed');
        }
        const blob = response.blob;
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = response.filename || doc.name;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.URL.revokeObjectURL(url);
      } else if (isExternalLinkDocument(doc) && googleDocsAccessToken) {
        const blob = await googleDocsService.exportDocument(googleDocsAccessToken, doc.id, GOOGLE_DOC_EXPORT_MIME);
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = ensureDocxFileName(doc.name);
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.URL.revokeObjectURL(url);
      } else if (doc.content) {
        // Fallback to old content storage
        const link = document.createElement('a');
        link.href = doc.content as string;
        link.download = doc.name;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
      } else {
        toast.warning('No content is available for this file.');
      }
    } catch (error: any) {
      console.error('Download error:', error);
      toast.error('Failed to download file: ' + (error.message || 'Unknown error'));
    }
  };

  const toggleSelected = (id: string) => {
    setSelectedIds(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]);
  };

  const clearSelected = () => {
    setSelectedIds([]);
    setBulkMatterId('');
  };

  const applyBulkAssign = async () => {
    if (selectedIds.length === 0) return;
    await bulkAssignDocuments(selectedIds, bulkMatterId || null);
    toast.success('Documents updated.');
    clearSelected();
  };

  const totalDocuments = documents.length;
  const assignedDocuments = documents.filter(doc => !!doc.matterId).length;
  const unassignedDocuments = totalDocuments - assignedDocuments;
  const activeScopeLabel = selectedMatter ? getMatterName(selectedMatter) : t('my_files');
  const currentMatterDocumentCount = selectedMatter
    ? documents.filter(doc => doc.matterId === selectedMatter).length
    : totalDocuments;

  const getDocumentTypeLabel = (doc: DocumentFile) => {
    switch (doc.type) {
      case 'pdf':
        return 'PDF';
      case 'docx':
        return 'Document';
      case 'txt':
        return 'Text';
      case 'img':
        return 'Image';
      default:
        return 'File';
    }
  };

  const getDocumentTypeClasses = (doc: DocumentFile) => {
    switch (doc.type) {
      case 'pdf':
        return 'bg-rose-50 text-rose-600 border-rose-100';
      case 'docx':
        return 'bg-blue-50 text-blue-600 border-blue-100';
      case 'txt':
        return 'bg-amber-50 text-amber-700 border-amber-100';
      case 'img':
        return 'bg-emerald-50 text-emerald-600 border-emerald-100';
      default:
        return 'bg-slate-100 text-slate-600 border-slate-200';
    }
  };

  const getDocumentSubtitle = (doc: DocumentFile) => {
    if (doc.description?.trim()) {
      return doc.description.trim();
    }

    if (doc.tags && doc.tags.length > 0) {
      return doc.tags.join(', ');
    }

    if (doc.matterId) {
      return getMatterName(doc.matterId);
    }

    return 'No description or tags';
  };

  return (
    <div className="h-full overflow-y-auto bg-slate-50 p-4 md:p-6">
      {/* Header */}
      <div className="rounded-[28px] border border-slate-200 bg-white px-5 py-5 shadow-sm md:px-6">
        <div className="flex flex-col gap-4 lg:flex-row lg:justify-between lg:items-start">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.24em] text-slate-400">Document Hub</p>
          <h1 className="mt-2 text-3xl font-semibold tracking-tight text-slate-900">{t('docs_title')}</h1>
          <p className="mt-2 text-sm text-slate-500">{t('docs_subtitle')}</p>
          <div className="mt-4 grid gap-3 sm:grid-cols-3">
            <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Visible</p>
              <p className="mt-2 text-2xl font-semibold text-slate-900">{filteredDocs.length}</p>
              <p className="mt-1 text-xs text-slate-500">{activeScopeLabel}</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Assigned</p>
              <p className="mt-2 text-2xl font-semibold text-slate-900">{assignedDocuments}</p>
              <p className="mt-1 text-xs text-slate-500">Linked to a matter</p>
            </div>
            <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Unassigned</p>
              <p className="mt-2 text-2xl font-semibold text-slate-900">{unassignedDocuments}</p>
              <p className="mt-1 text-xs text-slate-500">Needs sorting</p>
            </div>
          </div>
        </div>
        <div className="flex flex-wrap gap-3 relative lg:max-w-xl">
          <div className="flex flex-col gap-1 w-full">
            <div className="flex items-center gap-2 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
              <Search className="w-4 h-4 text-slate-400" />
              <input
                value={searchQuery}
                onChange={e => setSearchQuery(e.target.value)}
                placeholder="Search by name, tag, or description..."
                className="w-full bg-transparent outline-none text-sm text-slate-700 placeholder:text-slate-400"
              />
            </div>
            <label className="flex items-center gap-2 text-xs text-slate-500">
              <input
                type="checkbox"
                className="rounded border-slate-300"
                checked={searchInContent}
                onChange={e => setSearchInContent(e.target.checked)}
              />
              Search document text (indexed)
              {searchInContent && isSearchingContent && (
                <span className="text-slate-400">Searching...</span>
              )}
            </label>
          </div>
          <button
            onClick={() => setShowFilter(!showFilter)}
            className={`flex items-center gap-2 rounded-2xl border px-4 py-3 text-sm font-medium transition-colors ${showFilter ? 'border-primary-500 bg-primary-50 text-primary-700' : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50'}`}>
            <Filter className="w-4 h-4" /> {t('filter')}
          </button>

          {showFilter && (
            <div className="absolute top-full right-0 mt-2 w-56 rounded-2xl border border-slate-200 bg-white p-2 shadow-xl z-10">
              <div className="px-2 py-1 text-[11px] font-semibold uppercase tracking-wide text-slate-400">Type</div>
              <button onClick={() => { setFilterType('all'); setShowFilter(false); }} className="w-full rounded-xl px-3 py-2 text-left text-sm text-slate-700 hover:bg-slate-50">All Files</button>
              <button onClick={() => { setFilterType('pdf'); setShowFilter(false); }} className="w-full rounded-xl px-3 py-2 text-left text-sm text-slate-700 hover:bg-slate-50">PDFs</button>
              <button onClick={() => { setFilterType('docx'); setShowFilter(false); }} className="w-full rounded-xl px-3 py-2 text-left text-sm text-slate-700 hover:bg-slate-50">Documents</button>

              <div className="px-2 py-1 mt-2 text-[11px] font-semibold uppercase tracking-wide text-slate-400">Category</div>
              {Object.values(DocumentCategory).slice(0, 6).map(cat => (
                <button key={cat} onClick={() => { setFilterType(cat); setShowFilter(false); }} className="w-full rounded-xl px-3 py-2 text-left text-sm text-slate-700 hover:bg-slate-50">{cat}</button>
              ))}
            </div>
          )}

          <input type="file" className="hidden" ref={fileInputRef} onChange={handleFileChange} />
          {isGoogleDocsConnected && (
            <button
              onClick={handleGoogleDocsSync}
              disabled={isSyncingGoogleDocs}
              className="flex items-center gap-2 rounded-2xl bg-emerald-600 px-4 py-3 text-sm font-semibold text-white hover:bg-emerald-700 disabled:cursor-not-allowed disabled:opacity-60">
              <FileText className="w-4 h-4" /> {isSyncingGoogleDocs ? 'Syncing Google Docs...' : 'Sync Google Docs'}
            </button>
          )}
          {!isGoogleDocsConnected && (
            <button
              onClick={handleGoogleDocsConnect}
              className="flex items-center gap-2 rounded-2xl bg-blue-600 px-4 py-3 text-sm font-semibold text-white hover:bg-blue-700">
              <FileText className="w-4 h-4" /> Connect Google Docs
            </button>
          )}
          <button
            onClick={() => fileInputRef.current?.click()}
            className="flex items-center gap-2 rounded-2xl bg-primary-600 px-4 py-3 text-sm font-semibold text-white hover:bg-primary-700">
            <Plus className="w-4 h-4" /> {t('upload')}
          </button>
        </div>
      </div>
      </div>

      <div className="mt-5 grid gap-5 xl:grid-cols-[260px_minmax(0,1fr)]">
        {/* Sidebar Tree */}
        <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
          <div className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5">
            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-400">Scope</p>
            <p className="mt-1.5 text-base font-semibold text-slate-900">{activeScopeLabel}</p>
            <p className="mt-1 text-sm text-slate-500">{currentMatterDocumentCount} documents in this view</p>
          </div>
          <div className="text-xs font-bold text-slate-400 uppercase tracking-wider mb-2 mt-5 px-2">Locations</div>
          <button
            onClick={() => setSelectedMatter(null)}
            className={`flex items-center justify-between gap-2 px-3 py-2.5 rounded-xl text-sm font-medium transition-colors text-left w-full ${selectedMatter === null
              ? 'bg-slate-50 border border-primary-200 text-primary-700 shadow-sm'
              : 'hover:bg-slate-50 text-slate-600'
              }`}
          >
            <span className="flex items-center gap-2">
              <Folder className="w-4 h-4" /> {t('my_files')}
            </span>
            <span className="rounded-full bg-white px-2 py-0.5 text-xs text-slate-500">{totalDocuments}</span>
          </button>

          <div className="text-xs font-bold text-slate-400 uppercase tracking-wider mt-6 mb-2 px-2">{t('nav_matters')}</div>
          {matters.length === 0 && <div className="rounded-2xl border border-dashed border-slate-200 px-3 py-4 text-sm text-slate-400">No matters created.</div>}
          {matters.map(m => (
            <button
              key={m.id}
              onClick={() => setSelectedMatter(m.id)}
              className={`flex items-center justify-between gap-2 px-3 py-2.5 rounded-xl text-sm transition-colors text-left truncate w-full ${selectedMatter === m.id
                ? 'bg-slate-50 border border-primary-200 text-primary-700 shadow-sm'
                : 'hover:bg-slate-50 text-slate-600'
                }`}
            >
              <span className="flex min-w-0 items-center gap-2">
                <Folder className="w-4 h-4 text-slate-400 shrink-0" />
                <span className="truncate">{m.caseNumber}</span>
              </span>
              <span className="rounded-full bg-white px-2 py-0.5 text-xs text-slate-500">{documents.filter(doc => doc.matterId === m.id).length}</span>
            </button>
          ))}
        </div>

        {/* File Grid */}
        <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm md:p-5">
          <div className="mb-4 flex flex-col gap-3 border-b border-slate-100 pb-4 lg:flex-row lg:items-start lg:justify-between">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.22em] text-slate-400">Current View</p>
              <h2 className="mt-1.5 text-lg font-semibold text-slate-900">{activeScopeLabel}</h2>
              <p className="mt-1 text-sm text-slate-500">
                Showing {filteredDocs.length} of {totalDocuments} documents
                {searchTerm.length >= 2 ? ` matching "${searchQuery.trim()}"` : ''}.
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              {filterType !== 'all' && (
                <span className="rounded-full border border-indigo-200 bg-indigo-50 px-3 py-1 text-xs font-medium text-indigo-700">
                  Filter: {filterType}
                </span>
              )}
              {selectedIds.length > 0 && (
                <span className="rounded-full border border-primary-200 bg-primary-50 px-3 py-1 text-xs font-medium text-primary-700">
                  {selectedIds.length} selected
                </span>
              )}
            </div>
          </div>
          {selectedIds.length > 0 && (
            <div className="mb-5 rounded-2xl border border-indigo-200 bg-indigo-50 p-4 flex flex-col md:flex-row md:items-center md:justify-between gap-3">
              <div className="text-sm text-indigo-900 font-semibold">
                {selectedIds.length} documents selected
              </div>
              <div className="flex items-center gap-2 flex-wrap">
                <button onClick={selectAll} className="rounded-xl border border-indigo-200 bg-white px-3 py-2 text-xs font-semibold text-indigo-700 hover:bg-indigo-100">
                  {selectedIds.length === filteredDocs.length ? 'Deselect All' : 'Select All'}
                </button>
                <select
                  value={bulkMatterId}
                  onChange={e => setBulkMatterId(e.target.value)}
                  className="rounded-xl border border-indigo-200 bg-white px-3 py-2 text-sm text-slate-700"
                >
                  <option value="">Unassigned</option>
                  {matters.map(m => (
                    <option key={m.id} value={m.id}>{m.caseNumber} - {m.name}</option>
                  ))}
                </select>
                <button
                  onClick={applyBulkAssign}
                  className="rounded-xl bg-indigo-600 px-3 py-2 text-xs font-semibold text-white hover:bg-indigo-700"
                >
                  Assign to Matter
                </button>
                <button
                  onClick={clearSelected}
                  className="rounded-xl border border-indigo-200 bg-white px-3 py-2 text-xs font-semibold text-indigo-700 hover:bg-indigo-100"
                >
                  Clear
                </button>
              </div>
            </div>
          )}
          {filteredDocs.length === 0 ? (
            <div className="flex min-h-[340px] flex-col items-center justify-center rounded-[24px] border border-dashed border-slate-200 bg-slate-50 px-6 text-center text-slate-400">
              <Folder className="w-16 h-16 mb-4 text-slate-300" />
              <p className="text-lg font-semibold text-slate-800">No documents found.</p>
              <p className="mt-2 max-w-md text-sm text-slate-500">Try another matter, clear the filter, or upload a new file.</p>
              <div className="flex gap-3 mt-5 flex-wrap justify-center">
                <button onClick={() => fileInputRef.current?.click()} className="rounded-2xl bg-primary-600 px-4 py-3 text-sm font-semibold text-white hover:bg-primary-700">
                  Upload a file
                </button>
                {isGoogleDocsConnected ? (
                  <button
                    onClick={handleGoogleDocsSync}
                    disabled={isSyncingGoogleDocs}
                    className="rounded-2xl bg-emerald-600 px-4 py-3 text-sm font-semibold text-white hover:bg-emerald-700 disabled:cursor-not-allowed disabled:opacity-60">
                    {isSyncingGoogleDocs ? 'Syncing Google Docs...' : 'Sync Google Docs'}
                  </button>
                ) : (
                  <button onClick={handleGoogleDocsConnect} className="rounded-2xl bg-blue-600 px-4 py-3 text-sm font-semibold text-white hover:bg-blue-700">
                    Connect Google Docs
                  </button>
                )}
              </div>
            </div>
          ) : (
            <div className="grid grid-cols-[repeat(auto-fill,minmax(220px,260px))] gap-3">
              {filteredDocs.map(doc => (
                <div key={doc.id} className="group flex h-full min-h-[245px] flex-col rounded-2xl border border-slate-200 bg-white p-4 shadow-sm transition-all hover:-translate-y-0.5 hover:border-slate-300 hover:shadow-md">
                  <div className="mb-2.5 flex flex-col gap-2.5">
                    <div className="flex justify-between items-start gap-2">
                      <label className="flex items-center gap-1.5 text-[11px] font-medium text-slate-500">
                        <input
                          type="checkbox"
                          checked={selectedIds.includes(doc.id)}
                          onChange={() => toggleSelected(doc.id)}
                          className="rounded border-slate-300"
                        />
                        Select
                      </label>
                      <div className={`flex h-9 w-9 items-center justify-center rounded-xl border ${getDocumentTypeClasses(doc)}`}>
                        <FileText className="w-4 h-4" />
                      </div>
                    </div>
                    <div className="flex flex-wrap gap-1.5">
                      <span className={`rounded-full border px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide ${getDocumentTypeClasses(doc)}`}>
                        {getDocumentTypeLabel(doc)}
                      </span>
                      {doc.category && (
                        <span className="rounded-full border border-indigo-100 bg-indigo-50 px-2 py-0.5 text-[10px] font-medium text-indigo-700">{doc.category}</span>
                      )}
                    </div>
                    <div className="flex flex-wrap gap-1.5">
                      <button onClick={() => handleOpen(doc)} className="rounded-lg bg-slate-900 px-2.5 py-1.5 text-[11px] font-semibold text-white hover:bg-slate-950">
                        Open
                      </button>
                      <button onClick={() => handleDownload(doc)} className="rounded-lg border border-slate-200 px-2.5 py-1.5 text-[11px] font-semibold text-slate-700 hover:bg-slate-50">
                        Download
                      </button>
                      <button
                        onClick={() => handleRequestSignature(doc)}
                        className="rounded-lg border border-indigo-200 px-2.5 py-1.5 text-[11px] font-semibold text-indigo-700 hover:bg-indigo-50"
                      >
                        Signature
                      </button>
                      <button
                        onClick={() => openVersionHistory(doc)}
                        className="rounded-lg border border-slate-200 px-2.5 py-1.5 text-[11px] font-semibold text-slate-700 hover:bg-slate-50"
                      >
                        Versions
                      </button>
                      <button
                        onClick={() => {
                          if (!hasServerDocument(doc)) {
                            toast.warning('Sync Google Docs first so the file is imported before assigning it to a matter.');
                            return;
                          }
                          setEditingDoc(doc);
                          setEditMatterId(doc.matterId || '');
                          setEditTags((doc.tags || []).join(', '));
                          setEditCategory(doc.category || '');
                          setEditStatus(doc.status || '');
                          setEditLegalHoldReason(doc.legalHoldReason || '');
                        }}
                        className="rounded-lg border border-slate-200 px-2.5 py-1.5 text-[11px] font-semibold text-slate-700 hover:bg-slate-50"
                        title="Assign to matter"
                      >
                        Assign
                      </button>
                      <button
                        onClick={() => handleDelete(doc)}
                        disabled={doc.status === DocumentStatus.OnLegalHold}
                        className="flex items-center gap-1 rounded-lg border border-red-200 px-2.5 py-1.5 text-[11px] font-semibold text-red-600 hover:bg-red-50 disabled:opacity-50 disabled:hover:bg-transparent"
                        title="Delete"
                      >
                        <Trash2 className="w-3 h-3" /> Delete
                      </button>
                    </div>
                  </div>
                  <h3 className="line-clamp-2 text-sm font-semibold text-slate-900" title={doc.name}>{doc.name}</h3>
                  <div className="mt-1.5 flex-1 space-y-1.5">
                    <p className="line-clamp-2 text-xs text-slate-500" title={getDocumentSubtitle(doc)}>
                      {getDocumentSubtitle(doc)}
                    </p>
                    {doc.matterId && (
                      <div className="hidden text-xs text-primary-600 font-medium truncate" title={getMatterName(doc.matterId)}>
                        📁 {getMatterName(doc.matterId)}
                      </div>
                    )}
                    {doc.tags && doc.tags.length > 0 && (
                      <div className="hidden text-[11px] text-gray-500 truncate" title={doc.tags.join(', ')}>
                        🏷️ {doc.tags.join(', ')}
                      </div>
                    )}
                    <div className="grid grid-cols-[minmax(0,1fr)_auto] gap-x-2 gap-y-1 text-[11px] text-slate-500">
                      <span className="truncate">{doc.matterId ? getMatterName(doc.matterId) : 'Unassigned'}</span>
                      <span>{doc.size || 'Unknown size'}</span>
                      <span className="col-span-2">{formatDate(doc.updatedAt)}</span>
                    </div>
                    {doc.version && (
                      <div className="mt-1 text-[10px] text-slate-400">Version v{doc.version}</div>
                    )}
                    {(doc.category || doc.status) && (
                      <div className="hidden mt-1 flex items-center gap-1">
                        {doc.category && (
                          <span className="inline-block px-2 py-0.5 bg-indigo-50 text-indigo-600 text-[10px] rounded border border-indigo-100 font-medium">{doc.category}</span>
                        )}
                        {doc.status === DocumentStatus.OnLegalHold && (
                          <span
                            title={doc.legalHoldReason || 'Document is on legal hold.'}
                            className="inline-block px-2 py-0.5 bg-red-50 text-red-600 text-[10px] rounded border border-red-100 font-bold"
                          >
                            Legal Hold
                          </span>
                        )}
                        {doc.status && doc.status !== DocumentStatus.OnLegalHold && (
                          <span className={`inline-block px-2 py-0.5 text-[10px] rounded border font-medium ${doc.status === DocumentStatus.Final ? 'bg-green-50 text-green-600 border-green-100' :
                            doc.status === DocumentStatus.Filed ? 'bg-blue-50 text-blue-600 border-blue-100' :
                              doc.status === DocumentStatus.Draft ? 'bg-gray-50 text-gray-600 border-gray-200' :
                                'bg-gray-50 text-gray-500 border-gray-200'
                            }`}>{doc.status}</span>
                        )}
                      </div>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Document Viewer Modal */}
      {viewingDoc && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-4xl h-[90vh] flex flex-col">
            {/* Header */}
            <div className="px-6 py-4 border-b border-gray-200 flex justify-between items-center bg-gray-50">
              <div>
                <h3 className="font-bold text-lg text-slate-800">{viewingDoc.name}</h3>
                <p className="text-xs text-gray-500 mt-1">{viewingDoc.size} - {formatDate(viewingDoc.updatedAt)}</p>
              </div>
              <button
                onClick={closeViewer}
                className="text-gray-400 hover:text-gray-600"
              >
                <X className="w-6 h-6" />
              </button>
            </div>

            {/* Content */}
            <div className="flex-1 overflow-auto p-6 bg-white">
              {loadingContent ? (
                <div className="flex items-center justify-center h-full">
                  <div className="text-gray-400">Loading...</div>
                </div>
              ) : viewingDoc.type === 'pdf' ? (
                <iframe
                  src={docContent}
                  className="w-full h-full border-0"
                  title={viewingDoc.name}
                />
              ) : viewingDoc.type === 'txt' ? (
                <pre className="whitespace-pre-wrap font-mono text-sm text-slate-800 bg-gray-50 p-4 rounded-lg border border-gray-200 max-h-full overflow-auto">
                  {docContent}
                </pre>
              ) : viewingDoc.type === 'docx' ? (
                <div
                  className="prose max-w-none text-slate-800"
                  dangerouslySetInnerHTML={{ __html: docContent }}
                />
              ) : (
                <img
                  src={docContent}
                  alt={viewingDoc.name}
                  className="max-w-full h-auto mx-auto"
                />
              )}
            </div>

            <div className="border-t border-gray-200 bg-white px-6 py-4">
              <SignatureStatus documentId={viewingDoc.id} showActions />
            </div>

            {/* Footer Actions */}
            <div className="px-6 py-4 border-t border-gray-200 bg-gray-50 flex justify-end gap-3">
              <button
                onClick={() => handleRequestSignature(viewingDoc)}
                className="px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-bold hover:bg-indigo-700"
              >
                Request Signature
              </button>
              <button
                onClick={() => openShareSettings(viewingDoc)}
                className="px-4 py-2 bg-emerald-600 text-white rounded-lg text-sm font-bold hover:bg-emerald-700"
              >
                Share
              </button>
              <button
                onClick={() => handleDownload(viewingDoc)}
                className="px-4 py-2 bg-slate-800 text-white rounded-lg text-sm font-bold hover:bg-slate-900"
              >
                Download
              </button>
            </div>
          </div>
        </div>
      )}

      {showSignatureModal && signatureDocumentId && (
        <SignatureRequestModal
          isOpen={showSignatureModal}
          documentId={signatureDocumentId}
          matterId={signatureMatterId}
          onClose={() => {
            setShowSignatureModal(false);
            setSignatureDocumentId(null);
            setSignatureMatterId(undefined);
          }}
          onSuccess={() => {
            toast.success('Signature request sent.');
          }}
        />
      )}

      {showVersionModal && versionDoc && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-3xl overflow-hidden">
            <div className="px-6 py-4 border-b border-gray-200 flex items-center justify-between bg-gray-50">
              <div>
                <h3 className="font-bold text-lg text-slate-800">Version History</h3>
                <p className="text-xs text-gray-500 mt-1">{versionDoc.name}</p>
              </div>
              <button
                onClick={() => { setShowVersionModal(false); setVersionDoc(null); }}
                className="text-gray-400 hover:text-gray-600"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <div className="p-6 space-y-6">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-sm font-semibold text-slate-800">Uploaded Versions</p>
                  <p className="text-xs text-gray-500">Restore or download prior versions.</p>
                </div>
                <div className="flex items-center gap-2">
                  <input
                    ref={versionFileInputRef}
                    type="file"
                    className="hidden"
                    onChange={(e) => {
                      if (e.target.files && e.target.files[0]) {
                        handleUploadVersion(e.target.files[0]);
                        e.target.value = '';
                      }
                    }}
                  />
                  <button
                    onClick={() => versionFileInputRef.current?.click()}
                    className="px-3 py-2 bg-slate-800 text-white rounded-lg text-xs font-bold hover:bg-slate-900"
                  >
                    Upload New Version
                  </button>
                </div>
              </div>

              {versionLoading ? (
                <p className="text-sm text-gray-400">Loading version history...</p>
              ) : versionList.length === 0 ? (
                <p className="text-sm text-gray-400">No versions found.</p>
              ) : (
                <div className="border border-gray-200 rounded-lg overflow-hidden">
                  {versionList.map((version: any) => (
                    <div key={version.id} className="flex items-center justify-between px-4 py-3 border-b border-gray-100 last:border-0">
                      <div>
                        <p className="text-sm font-semibold text-slate-800">{version.fileName}</p>
                        <p className="text-xs text-gray-500">
                          {formatDate(version.createdAt)} - {typeof version.fileSize === 'number' ? `${(version.fileSize / 1024 / 1024).toFixed(2)} MB` : 'Unknown size'}
                        </p>
                      </div>
                      <div className="flex items-center gap-2">
                        <button
                          onClick={() => handleDownloadVersion(version.id)}
                          className="px-2 py-1 text-xs text-slate-600 border border-gray-200 rounded hover:bg-gray-50"
                        >
                          Download
                        </button>
                        <button
                          onClick={() => handleRestoreVersion(version.id)}
                          className="px-2 py-1 text-xs text-emerald-600 border border-emerald-200 rounded hover:bg-emerald-50"
                        >
                          Restore
                        </button>
                        <button
                          onClick={() => setDiffLeftId(version.id)}
                          className={`px-2 py-1 text-xs border rounded ${diffLeftId === version.id ? 'bg-slate-800 text-white border-slate-800' : 'border-gray-200 text-gray-600 hover:bg-gray-50'}`}
                        >
                          Left
                        </button>
                        <button
                          onClick={() => setDiffRightId(version.id)}
                          className={`px-2 py-1 text-xs border rounded ${diffRightId === version.id ? 'bg-slate-800 text-white border-slate-800' : 'border-gray-200 text-gray-600 hover:bg-gray-50'}`}
                        >
                          Right
                        </button>
                      </div>
                    </div>
                  ))}
                </div>
              )}

              <div className="border-t border-gray-100 pt-4">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-semibold text-slate-800">Compare Versions</p>
                    <p className="text-xs text-gray-500">Select two versions to generate a diff.</p>
                  </div>
                  <button
                    onClick={handleDiffVersions}
                    className="px-3 py-2 bg-indigo-600 text-white rounded-lg text-xs font-bold hover:bg-indigo-700"
                  >
                    Compare
                  </button>
                </div>
                {diffResult && (
                  <pre className="mt-3 max-h-64 overflow-y-auto text-xs bg-gray-50 border border-gray-200 rounded-lg p-3 whitespace-pre-wrap text-slate-700">
                    {diffResult}
                  </pre>
                )}
              </div>
            </div>
          </div>
        </div>
      )}

      {showShareModal && shareDoc && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-md">
            <div className="px-6 py-4 border-b border-gray-200 flex items-center justify-between bg-gray-50">
              <div>
                <h3 className="font-bold text-lg text-slate-800">Share Document</h3>
                <p className="text-xs text-gray-500 mt-1">{shareDoc.name}</p>
              </div>
              <button
                onClick={() => {
                  setShowShareModal(false);
                  setShareDoc(null);
                }}
                className="text-gray-400 hover:text-gray-600"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <div className="px-6 py-5 space-y-4">
              {shareLoading ? (
                <div className="text-sm text-gray-400">Loading share settings...</div>
              ) : (
                <>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-2">Client</label>
                    <select
                      value={shareClientId}
                      onChange={(e) => setShareClientId(e.target.value)}
                      className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-emerald-500 focus:border-transparent"
                    >
                      <option value="">Select client</option>
                      {clients.map(c => (
                        <option key={c.id} value={c.id}>
                          {c.name}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div className="grid grid-cols-2 gap-3 text-sm text-gray-700">
                    <label className="flex items-center gap-2">
                      <input
                        type="checkbox"
                        checked={sharePermissions.canView}
                        onChange={(e) => setSharePermissions(prev => ({ ...prev, canView: e.target.checked }))}
                      />
                      View
                    </label>
                    <label className="flex items-center gap-2">
                      <input
                        type="checkbox"
                        checked={sharePermissions.canDownload}
                        onChange={(e) => setSharePermissions(prev => ({ ...prev, canDownload: e.target.checked }))}
                      />
                      Download
                    </label>
                    <label className="flex items-center gap-2">
                      <input
                        type="checkbox"
                        checked={sharePermissions.canComment}
                        onChange={(e) => setSharePermissions(prev => ({ ...prev, canComment: e.target.checked }))}
                      />
                      Comment
                    </label>
                    <label className="flex items-center gap-2">
                      <input
                        type="checkbox"
                        checked={sharePermissions.canUpload}
                        onChange={(e) => setSharePermissions(prev => ({ ...prev, canUpload: e.target.checked }))}
                      />
                      Upload
                    </label>
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-2">Expiration date (optional)</label>
                    <input
                      type="date"
                      value={shareExpiresAt}
                      onChange={(e) => setShareExpiresAt(e.target.value)}
                      className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-emerald-500 focus:border-transparent"
                    />
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-2">Notes (optional)</label>
                    <textarea
                      value={shareNote}
                      onChange={(e) => setShareNote(e.target.value)}
                      rows={3}
                      className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-emerald-500 focus:border-transparent resize-none"
                    />
                  </div>
                </>
              )}
            </div>

            <div className="px-6 py-4 border-t border-gray-200 flex justify-end gap-3 bg-gray-50">
              <button
                onClick={() => {
                  setShowShareModal(false);
                  setShareDoc(null);
                }}
                className="px-4 py-2 text-gray-600 hover:text-gray-800 text-sm font-medium"
              >
                Cancel
              </button>
              <button
                onClick={handleRemoveShare}
                disabled={!shareClientId}
                className="px-4 py-2 text-sm font-medium text-red-600 hover:text-red-700 disabled:opacity-60"
              >
                Remove
              </button>
              <button
                onClick={handleSaveShare}
                disabled={!shareClientId}
                className="px-4 py-2 bg-emerald-600 text-white rounded-lg text-sm font-medium hover:bg-emerald-700 disabled:opacity-60"
              >
                Save
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Edit Matter Modal */}
      {editingDoc && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-md">
            <div className="px-6 py-4 border-b border-gray-200">
              <h3 className="font-bold text-lg text-slate-800">Assign Document to Matter</h3>
              <p className="text-sm text-gray-500 mt-1">{editingDoc.name}</p>
            </div>

            <div className="px-6 py-4">
              <label className="block text-sm font-medium text-gray-700 mb-2">Matter</label>
              <select
                value={editMatterId}
                onChange={(e) => setEditMatterId(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent"
              >
                <option value="">-- No Matter (Unassigned) --</option>
                {matters.map(m => (
                  <option key={m.id} value={m.id}>
                    {m.caseNumber} - {m.name}
                  </option>
                ))}
              </select>

              <label className="block text-sm font-medium text-gray-700 mb-2 mt-4">Tags</label>
              <input
                value={editTags}
                onChange={(e) => setEditTags(e.target.value)}
                placeholder="e.g. contract, power of attorney, evidence"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent"
              />

              <label className="block text-sm font-medium text-gray-700 mb-2 mt-4">Category</label>
              <select
                value={editCategory}
                onChange={(e) => setEditCategory(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent"
              >
                <option value="">-- No Category --</option>
                {Object.values(DocumentCategory).map(cat => (
                  <option key={cat} value={cat}>{cat}</option>
                ))}
              </select>

              <label className="block text-sm font-medium text-gray-700 mb-2 mt-4">Status</label>
              <select
                value={editStatus}
                onChange={(e) => setEditStatus(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent"
              >
                <option value="">-- No Status --</option>
                {Object.values(DocumentStatus).map(s => (
                  <option key={s} value={s}>{s}</option>
                ))}
              </select>

              {editStatus === DocumentStatus.OnLegalHold && (
                <div className="mt-4">
                  <label className="block text-sm font-medium text-gray-700 mb-2">Legal Hold Reason</label>
                  <textarea
                    value={editLegalHoldReason}
                    onChange={(e) => setEditLegalHoldReason(e.target.value)}
                    placeholder="Describe the legal hold scope or reason"
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                    rows={3}
                  />
                </div>
              )}
            </div>

            <div className="px-6 py-4 border-t border-gray-200 flex justify-end gap-3">
              <button
                onClick={() => {
                  setEditingDoc(null);
                  setEditMatterId('');
                  setEditTags('');
                  setEditCategory('');
                  setEditStatus('');
                  setEditLegalHoldReason('');
                }}
                className="px-4 py-2 text-gray-600 hover:text-gray-800 text-sm font-medium"
              >
                Cancel
              </button>
              <button
                onClick={async () => {
                  if (editingDoc) {
                    const tags = editTags
                      .split(',')
                      .map(s => s.trim())
                      .filter(Boolean);
                    await updateDocument(editingDoc.id, {
                      matterId: editMatterId || undefined,
                      tags,
                      category: editCategory,
                      status: editStatus,
                      legalHoldReason: editStatus === DocumentStatus.OnLegalHold ? editLegalHoldReason.trim() : undefined
                    });
                    toast.success('Document updated');
                    setEditingDoc(null);
                    setEditMatterId('');
                    setEditTags('');
                    setEditCategory('');
                    setEditStatus('');
                    setEditLegalHoldReason('');
                  }
                }}
                className="px-4 py-2 bg-primary-600 text-white rounded-lg text-sm font-medium hover:bg-primary-700"
              >
                Save
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Matter Selection Modal */}
      {showMatterModal && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-[24px] shadow-2xl w-full max-w-lg overflow-hidden">
            <div className="px-6 py-5 border-b border-slate-200 bg-slate-50">
              <h3 className="font-bold text-lg text-slate-800">Upload Document</h3>
              <p className="text-sm text-slate-500 mt-1">Choose a matter or leave it unassigned for later review.</p>
            </div>

            <div className="px-6 py-5 space-y-4">
              {pendingFile && (
                <div className="rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3">
                  <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">Selected file</p>
                  <p className="mt-2 text-sm font-semibold text-slate-900 break-all">{pendingFile.name}</p>
                  <p className="mt-1 text-xs text-slate-500">
                    {(pendingFile.size / 1024 / 1024).toFixed(2)} MB
                  </p>
                </div>
              )}

              <label className="block text-sm font-medium text-slate-700 mb-2">Matter</label>
              <select
                value={selectedMatterForUpload}
                onChange={(e) => setSelectedMatterForUpload(e.target.value)}
                className="w-full px-3 py-3 border border-slate-300 rounded-2xl text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent"
              >
                <option value="">No Matter (Unassigned)</option>
                {matters.map(m => (
                  <option key={m.id} value={m.id}>
                    {m.caseNumber} - {m.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="px-6 py-4 border-t border-slate-200 flex justify-end gap-3 bg-white">
              <button
                onClick={() => {
                  setShowMatterModal(false);
                  setPendingFile(null);
                  setSelectedMatterForUpload('');
                }}
                disabled={isUploading}
                className="px-4 py-2 text-slate-600 hover:text-slate-800 text-sm font-medium disabled:opacity-60"
              >
                Cancel
              </button>
              <button
                onClick={handleConfirmUpload}
                disabled={isUploading}
                className="px-4 py-2 bg-primary-600 text-white rounded-2xl text-sm font-semibold hover:bg-primary-700 disabled:opacity-60"
              >
                {isUploading ? 'Uploading...' : 'Upload'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default Documents;
