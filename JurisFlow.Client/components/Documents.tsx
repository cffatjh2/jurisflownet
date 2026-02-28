import React, { useRef, useState, useEffect } from 'react';
import { DocumentFile, DocumentCategory, DocumentStatus } from '../types';
import { Folder, FileText, Search, Plus, Filter, X, Trash2 } from './Icons';
import { useTranslation } from '../contexts/LanguageContext';
import { useData } from '../contexts/DataContext';
import { api } from '../services/api';
import mammoth from 'mammoth';
import { googleDocsService } from '../services/googleDocsService';
import { toast } from './Toast';
import { useConfirm } from './ConfirmDialog';
import { getGoogleClientId } from '../services/googleConfig';
import { clearOAuthTokens, getOAuthAccessToken, requestOAuthState } from '../services/oauthSecurity';
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
  const [selectedMatterForUpload, setSelectedMatterForUpload] = useState<string>('');
  const [editingDoc, setEditingDoc] = useState<DocumentFile | null>(null);
  const [editMatterId, setEditMatterId] = useState<string>('');
  const [editTags, setEditTags] = useState<string>('');
  const [isGoogleDocsConnected, setIsGoogleDocsConnected] = useState(false);
  const [googleDocsAccessToken, setGoogleDocsAccessToken] = useState<string | null>(
    getOAuthAccessToken('google-docs')
  );
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
      const state = await requestOAuthState('google', 'google-docs', '/#documents');
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
    if (!googleDocsAccessToken) return;

    try {
      const docs = await googleDocsService.getDocuments(googleDocsAccessToken);
      docs.forEach(doc => {
        addDocument({
          id: doc.id,
          name: doc.name,
          type: 'docx',
          size: 'Google Doc',
          updatedAt: doc.modifiedTime,
          content: doc.webViewLink
        });
      });
      toast.success('Google Docs synced successfully!');
    } catch (error) {
      console.error('Google Docs sync error:', error);
      toast.error('Failed to sync Google Docs. Please reconnect.');
      clearOAuthTokens('google-docs');
      setGoogleDocsAccessToken(null);
    }
  };

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      const file = e.target.files[0];
      setPendingFile(file);
      setShowMatterModal(true);
    }
  };

  const handleConfirmUpload = async () => {
    if (!pendingFile) return;

    try {
      // Upload to server
      const uploadedDoc = await api.uploadDocument(
        pendingFile,
        selectedMatterForUpload || undefined,
        undefined
      );

      if (uploadedDoc) {
        // Add to local state immediately
        const doc: DocumentFile = {
          id: uploadedDoc.id,
          name: uploadedDoc.name,
          type: uploadedDoc.mimeType?.includes('pdf') ? 'pdf' :
            uploadedDoc.mimeType?.includes('word') ? 'docx' :
              uploadedDoc.mimeType?.includes('text') ? 'txt' : 'img',
          size: `${(uploadedDoc.fileSize / 1024 / 1024).toFixed(2)} MB`,
          updatedAt: uploadedDoc.createdAt,
          matterId: uploadedDoc.matterId || undefined,
          filePath: uploadedDoc.filePath,
          category: uploadedDoc.category || undefined,
          status: uploadedDoc.status || undefined,
          version: uploadedDoc.version || undefined,
          legalHoldReason: uploadedDoc.legalHoldReason || undefined,
          legalHoldPlacedAt: uploadedDoc.legalHoldPlacedAt || undefined,
          legalHoldReleasedAt: uploadedDoc.legalHoldReleasedAt || undefined,
          legalHoldPlacedBy: uploadedDoc.legalHoldPlacedBy || undefined
        };

        // Add to context state - this will persist
        addDocument(doc);

        // Close modal and reset
        setShowMatterModal(false);
        setPendingFile(null);
        setSelectedMatterForUpload('');
        toast.success('File uploaded successfully.');
      }
    } catch (error: any) {
      console.error('Upload error:', error);
      toast.error('File upload failed: ' + (error.message || 'Unknown error'));
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
    setViewingDoc(doc);
    setLoadingContent(true);
    setDocContent('');
    if (docObjectUrl) {
      URL.revokeObjectURL(docObjectUrl);
      setDocObjectUrl(null);
    }

    try {
      // If file has filePath, load from server
      if (doc.filePath) {
        const response = await api.downloadDocument(doc.id);
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
    if (!doc.filePath) {
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
          filePath: updated.filePath,
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
          filePath: updated.filePath,
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
          const mapped: DocumentFile[] = res.map((d: any) => ({
            id: d.id,
            name: d.name,
            description: d.description,
            tags: parseTags(d.tags),
            type: d.mimeType?.includes('pdf') ? 'pdf' :
              d.mimeType?.includes('word') ? 'docx' :
                d.mimeType?.includes('text') ? 'txt' : 'img',
            size: typeof d.fileSize === 'number' ? `${(d.fileSize / 1024 / 1024).toFixed(2)} MB` : undefined,
            fileSize: d.fileSize,
            updatedAt: d.updatedAt || d.createdAt,
            matterId: d.matterId || undefined,
            filePath: d.filePath,
            category: d.category || undefined,
            status: d.status || undefined,
            version: d.version || undefined,
            legalHoldReason: d.legalHoldReason || undefined,
            legalHoldPlacedAt: d.legalHoldPlacedAt || undefined,
            legalHoldReleasedAt: d.legalHoldReleasedAt || undefined,
            legalHoldPlacedBy: d.legalHoldPlacedBy || undefined
          }));
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
      if (doc.filePath) {
        const response = await api.downloadDocument(doc.id);
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

  return (
    <div className="h-full flex flex-col bg-white">
      {/* Header */}
      <div className="px-6 py-4 border-b border-gray-100 flex justify-between items-center bg-white">
        <div>
          <h1 className="text-2xl font-bold text-slate-800">{t('docs_title')}</h1>
          <p className="text-sm text-gray-500 mt-1">{t('docs_subtitle')}</p>
        </div>
        <div className="flex gap-3 relative">
          <div className="hidden md:flex flex-col gap-1">
            <div className="flex items-center gap-2 px-3 py-2 bg-gray-50 border border-gray-200 rounded-lg">
              <Search className="w-4 h-4 text-gray-400" />
              <input
                value={searchQuery}
                onChange={e => setSearchQuery(e.target.value)}
                placeholder="Search by name, tag, or description..."
                className="bg-transparent outline-none text-sm text-slate-700 placeholder:text-gray-400 w-64"
              />
            </div>
            <label className="flex items-center gap-2 text-xs text-gray-500">
              <input
                type="checkbox"
                className="rounded border-gray-300"
                checked={searchInContent}
                onChange={e => setSearchInContent(e.target.checked)}
              />
              Search document text (indexed)
              {searchInContent && isSearchingContent && (
                <span className="text-gray-400">Searching...</span>
              )}
            </label>
          </div>
          <button
            onClick={() => setShowFilter(!showFilter)}
            className={`flex items-center gap-2 px-4 py-2 bg-white border rounded-lg text-sm font-medium transition-colors ${showFilter ? 'border-primary-500 text-primary-600' : 'border-gray-200 text-gray-700 hover:bg-gray-50'}`}>
            <Filter className="w-4 h-4" /> {t('filter')}
          </button>

          {showFilter && (
            <div className="absolute top-full right-0 mt-2 w-48 bg-white shadow-xl rounded-lg border border-gray-100 z-10 p-2">
              <div className="text-xs font-bold text-gray-400 px-2 py-1 uppercase">Type</div>
              <button onClick={() => { setFilterType('all'); setShowFilter(false); }} className="w-full text-left px-2 py-1.5 text-sm hover:bg-gray-50 rounded">All Files</button>
              <button onClick={() => { setFilterType('pdf'); setShowFilter(false); }} className="w-full text-left px-2 py-1.5 text-sm hover:bg-gray-50 rounded">PDFs</button>
              <button onClick={() => { setFilterType('docx'); setShowFilter(false); }} className="w-full text-left px-2 py-1.5 text-sm hover:bg-gray-50 rounded">Documents</button>

              <div className="text-xs font-bold text-gray-400 px-2 py-1 uppercase mt-2">Category</div>
              {Object.values(DocumentCategory).slice(0, 6).map(cat => (
                <button key={cat} onClick={() => { setFilterType(cat); setShowFilter(false); }} className="w-full text-left px-2 py-1.5 text-sm hover:bg-gray-50 rounded">{cat}</button>
              ))}
            </div>
          )}

          <input type="file" className="hidden" ref={fileInputRef} onChange={handleFileChange} />
          {isGoogleDocsConnected && (
            <button
              onClick={handleGoogleDocsSync}
              className="flex items-center gap-2 px-4 py-2 bg-green-600 text-white rounded-lg text-sm font-medium hover:bg-green-700 transition-colors shadow-sm">
              <FileText className="w-4 h-4" /> Sync Google Docs
            </button>
          )}
          {!isGoogleDocsConnected && (
            <button
              onClick={handleGoogleDocsConnect}
              className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors shadow-sm">
              <FileText className="w-4 h-4" /> Connect Google Docs
            </button>
          )}
          <button
            onClick={() => fileInputRef.current?.click()}
            className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white rounded-lg text-sm font-medium hover:bg-primary-700 transition-colors shadow-sm">
            <Plus className="w-4 h-4" /> {t('upload')}
          </button>
        </div>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* Sidebar Tree */}
        <div className="w-64 border-r border-gray-100 bg-gray-50 p-4 flex flex-col gap-1 overflow-y-auto">
          <div className="text-xs font-bold text-gray-400 uppercase tracking-wider mb-2 px-2">Locations</div>
          <button
            onClick={() => setSelectedMatter(null)}
            className={`flex items-center gap-2 px-3 py-2 rounded-md text-sm font-medium transition-colors text-left ${selectedMatter === null
              ? 'bg-white border border-gray-200 text-primary-600 shadow-sm'
              : 'hover:bg-gray-100 text-gray-600'
              }`}
          >
            <Folder className="w-4 h-4" /> {t('my_files')}
          </button>

          <div className="text-xs font-bold text-gray-400 uppercase tracking-wider mt-6 mb-2 px-2">{t('nav_matters')}</div>
          {matters.length === 0 && <div className="px-2 text-xs text-gray-400 italic">No matters created.</div>}
          {matters.map(m => (
            <button
              key={m.id}
              onClick={() => setSelectedMatter(m.id)}
              className={`flex items-center gap-2 px-3 py-2 rounded-md text-sm transition-colors text-left truncate ${selectedMatter === m.id
                ? 'bg-white border border-gray-200 text-primary-600 shadow-sm'
                : 'hover:bg-gray-100 text-gray-600'
                }`}
            >
              <Folder className="w-4 h-4 text-gray-400 shrink-0" />
              <span className="truncate">{m.caseNumber}</span>
            </button>
          ))}
        </div>

        {/* File Grid */}
        <div className="flex-1 p-6 overflow-y-auto">
          {selectedIds.length > 0 && (
            <div className="mb-4 p-3 bg-indigo-50 border border-indigo-100 rounded-xl flex flex-col md:flex-row md:items-center md:justify-between gap-3">
              <div className="text-sm text-indigo-900 font-semibold">
                {selectedIds.length} documents selected
              </div>
              <div className="flex items-center gap-2">
                <button onClick={selectAll} className="text-xs px-2 py-1 bg-white border border-indigo-200 rounded text-indigo-600 font-bold hover:bg-indigo-50">
                  {selectedIds.length === filteredDocs.length ? 'Deselect' : 'Select All'}
                </button>
                <select
                  value={bulkMatterId}
                  onChange={e => setBulkMatterId(e.target.value)}
                  className="px-3 py-2 border border-indigo-200 rounded-lg bg-white text-sm"
                >
                  <option value="">-- Unassigned --</option>
                  {matters.map(m => (
                    <option key={m.id} value={m.id}>{m.caseNumber} - {m.name}</option>
                  ))}
                </select>
                <button
                  onClick={applyBulkAssign}
                  className="px-3 py-2 bg-indigo-600 text-white rounded-lg text-sm font-bold hover:bg-indigo-700"
                >
                  Assign to Matter
                </button>
                <button
                  onClick={clearSelected}
                  className="px-3 py-2 bg-white border border-indigo-200 text-indigo-700 rounded-lg text-sm font-bold hover:bg-indigo-50"
                >
                  Clear
                </button>
              </div>
            </div>
          )}
          {filteredDocs.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-64 text-gray-400">
              <Folder className="w-16 h-16 opacity-20 mb-4" />
              <p>No documents found.</p>
              <div className="flex gap-3 mt-4">
                <button onClick={() => fileInputRef.current?.click()} className="px-4 py-2 bg-primary-600 text-white text-sm font-bold rounded-lg hover:bg-primary-700">
                  Upload a file
                </button>
                {isGoogleDocsConnected ? (
                  <button onClick={handleGoogleDocsSync} className="px-4 py-2 bg-green-600 text-white text-sm font-bold rounded-lg hover:bg-green-700">
                    Sync Google Docs
                  </button>
                ) : (
                  <button onClick={handleGoogleDocsConnect} className="px-4 py-2 bg-blue-600 text-white text-sm font-bold rounded-lg hover:bg-blue-700">
                    Connect Google Docs
                  </button>
                )}
              </div>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
              {filteredDocs.map(doc => (
                <div key={doc.id} className="group p-4 bg-white border border-gray-200 rounded-xl hover:shadow-card hover:border-primary-200 transition-all">
                  <div className="flex flex-col gap-2 mb-3">
                    <div className="flex justify-between items-start gap-3">
                      <label className="flex items-center gap-2 text-xs text-gray-500">
                        <input
                          type="checkbox"
                          checked={selectedIds.includes(doc.id)}
                          onChange={() => toggleSelected(doc.id)}
                          className="rounded border-gray-300"
                        />
                        Select
                      </label>
                      <div className={`w-10 h-10 rounded-lg flex items-center justify-center ${doc.type === 'folder' ? 'bg-blue-50 text-blue-500' :
                        doc.type === 'pdf' ? 'bg-red-50 text-red-500' : 'bg-blue-50 text-blue-600'
                        }`}>
                        {doc.type === 'folder' ? <Folder className="w-6 h-6" /> : <FileText className="w-6 h-6" />}
                      </div>
                    </div>
                    <div className="flex flex-wrap gap-2">
                      <button onClick={() => handleOpen(doc)} className="px-2 py-1 text-xs text-primary-600 hover:bg-primary-50 rounded">
                        Open
                      </button>
                      <button onClick={() => handleDownload(doc)} className="px-2 py-1 text-xs text-gray-600 hover:bg-gray-50 rounded">
                        Download
                      </button>
                      <button
                        onClick={() => handleRequestSignature(doc)}
                        className="px-2 py-1 text-xs text-indigo-600 hover:bg-indigo-50 rounded"
                      >
                        Request Signature
                      </button>
                      <button
                        onClick={() => openVersionHistory(doc)}
                        className="px-2 py-1 text-xs text-slate-600 hover:bg-slate-50 rounded"
                      >
                        Versions
                      </button>
                      <button
                        onClick={() => {
                          setEditingDoc(doc);
                          setEditMatterId(doc.matterId || '');
                          setEditTags((doc.tags || []).join(', '));
                          setEditCategory(doc.category || '');
                          setEditStatus(doc.status || '');
                          setEditLegalHoldReason(doc.legalHoldReason || '');
                        }}
                        className="px-2 py-1 text-xs text-gray-600 hover:bg-gray-50 rounded"
                        title="Assign to matter"
                      >
                        Assign
                      </button>
                      <button
                        onClick={() => handleDelete(doc)}
                        disabled={doc.status === DocumentStatus.OnLegalHold}
                        className="px-2 py-1 text-xs text-red-600 hover:bg-red-50 rounded flex items-center gap-1 disabled:opacity-50 disabled:hover:bg-transparent"
                        title="Delete"
                      >
                        <Trash2 className="w-3 h-3" /> Delete
                      </button>
                    </div>
                  </div>
                  <h3 className="font-medium text-slate-800 truncate text-sm" title={doc.name}>{doc.name}</h3>
                  <div className="mt-2 space-y-1">
                    {doc.matterId && (
                      <div className="text-xs text-primary-600 font-medium truncate" title={getMatterName(doc.matterId)}>
                        📁 {getMatterName(doc.matterId)}
                      </div>
                    )}
                    {doc.tags && doc.tags.length > 0 && (
                      <div className="text-[11px] text-gray-500 truncate" title={doc.tags.join(', ')}>
                        🏷️ {doc.tags.join(', ')}
                      </div>
                    )}
                    <div className="flex justify-between items-center text-xs text-gray-500">
                      <span>{doc.size || 'Unknown'}</span>
                      <span>{formatDate(doc.updatedAt)}</span>
                    </div>
                    {doc.version && (
                      <div className="text-[10px] text-gray-400 mt-1">Version v{doc.version}</div>
                    )}
                    {(doc.category || doc.status) && (
                      <div className="mt-1 flex items-center gap-1">
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
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-md">
            <div className="px-6 py-4 border-b border-gray-200">
              <h3 className="font-bold text-lg text-slate-800">Select Matter</h3>
              <p className="text-sm text-gray-500 mt-1">Choose a matter to associate with this document</p>
            </div>

            <div className="px-6 py-4">
              <label className="block text-sm font-medium text-gray-700 mb-2">Matter</label>
              <select
                value={selectedMatterForUpload}
                onChange={(e) => setSelectedMatterForUpload(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent"
              >
                <option value="">-- No Matter (Unassigned) --</option>
                {matters.map(m => (
                  <option key={m.id} value={m.id}>
                    {m.caseNumber} - {m.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="px-6 py-4 border-t border-gray-200 flex justify-end gap-3">
              <button
                onClick={() => {
                  setShowMatterModal(false);
                  setPendingFile(null);
                  setSelectedMatterForUpload('');
                }}
                className="px-4 py-2 text-gray-600 hover:text-gray-800 text-sm font-medium"
              >
                Cancel
              </button>
              <button
                onClick={handleConfirmUpload}
                className="px-4 py-2 bg-primary-600 text-white rounded-lg text-sm font-medium hover:bg-primary-700"
              >
                Upload
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default Documents;
