import React, { useState, useEffect, useRef } from 'react';
import { DocumentFile } from '../../types';
import { Folder, FileText, Download, Plus, X, Search } from '../Icons';
import mammoth from 'mammoth';
import { googleDocsService } from '../../services/googleDocsService';
import { toast } from '../Toast';
import { getGoogleClientId } from '../../services/googleConfig';
import { useClientAuth } from '../../contexts/ClientAuthContext';
import { clientApi } from '../../services/clientApi';
import { clearOAuthTokens, getOAuthAccessToken, requestOAuthState } from '../../services/oauthSecurity';
import { getCurrentAppReturnPath } from '../../services/returnPath';

const ClientDocuments: React.FC = () => {
  const { client } = useClientAuth();
  const [documents, setDocuments] = useState<DocumentFile[]>([]);
  const [matters, setMatters] = useState<any[]>([]);
  const [selectedMatter, setSelectedMatter] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [searchQuery, setSearchQuery] = useState('');
  const [filterSource, setFilterSource] = useState<'all' | 'firm' | 'client'>('all');
  const [sortBy, setSortBy] = useState<'recent' | 'name'>('recent');
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [pendingFile, setPendingFile] = useState<File | null>(null);
  const [selectedMatterForUpload, setSelectedMatterForUpload] = useState<string>('');
  const [uploadDescription, setUploadDescription] = useState('');
  const [viewingDoc, setViewingDoc] = useState<DocumentFile | null>(null);
  const [docContent, setDocContent] = useState<string>('');
  const [loadingContent, setLoadingContent] = useState(false);
  const [comments, setComments] = useState<any[]>([]);
  const [commentDraft, setCommentDraft] = useState('');
  const [commentsLoading, setCommentsLoading] = useState(false);
  const [commentSending, setCommentSending] = useState(false);
  const [docObjectUrl, setDocObjectUrl] = useState<string | null>(null);
  const [isGoogleDocsConnected, setIsGoogleDocsConnected] = useState(false);
  const [googleDocsAccessToken, setGoogleDocsAccessToken] = useState<string | null>(
    getOAuthAccessToken('google-docs')
  );
  const fileInputRef = useRef<HTMLInputElement>(null);

  const formatFileSize = (bytes?: number) => {
    if (!bytes || bytes <= 0) return 'Unknown size';
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), sizes.length - 1);
    const value = bytes / Math.pow(1024, i);
    return `${value.toFixed(value >= 10 || i === 0 ? 0 : 2)} ${sizes[i]}`;
  };

  const getSafeTimestamp = (dateString?: string | null) => {
    if (!dateString) return 0;
    const timestamp = new Date(dateString).getTime();
    return Number.isFinite(timestamp) ? timestamp : 0;
  };

  const getNormalizedDocumentDate = (dateString?: string | null) => {
    const timestamp = getSafeTimestamp(dateString);
    return timestamp > 0 ? new Date(timestamp).toISOString() : new Date().toISOString();
  };

  const formatDocumentDate = (dateString?: string | null) => {
    const timestamp = getSafeTimestamp(dateString);
    return timestamp > 0 ? new Date(timestamp).toLocaleDateString() : 'Unknown date';
  };

  const formatDateTime = (dateString?: string | null) => {
    const timestamp = getSafeTimestamp(dateString);
    return timestamp > 0 ? new Date(timestamp).toLocaleString() : 'Unknown date';
  };

  const inferDocType = (name: string, mime?: string): DocumentFile['type'] => {
    const ext = name.split('.').pop()?.toLowerCase();
    if (ext === 'pdf' || mime?.includes('pdf')) return 'pdf';
    if (ext === 'docx' || mime?.includes('wordprocessingml')) return 'docx';
    if (ext === 'txt' || ext === 'md') return 'txt';
    if (mime?.startsWith('image/')) return 'img';
    return 'img';
  };

  const normalizeFilePath = (path?: string) => {
    if (!path) return '';
    if (path.startsWith('http')) return path;
    return path.startsWith('/') ? path : `/${path}`;
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

  const mapServerDocument = (doc: any): DocumentFile => ({
    id: doc.id,
    name: doc.name || doc.fileName,
    type: inferDocType(doc.fileName || doc.name || '', doc.mimeType),
    size: formatFileSize(doc.fileSize),
    fileSize: doc.fileSize,
    updatedAt: getNormalizedDocumentDate(doc.updatedAt || doc.createdAt),
    matterId: doc.matterId,
    description: doc.description,
    tags: parseTags(doc.tags),
    category: doc.category,
    filePath: normalizeFilePath(doc.filePath),
    uploadedBy: doc.uploadedBy,
    permissions: doc.permissions
  });

  useEffect(() => {
    setIsGoogleDocsConnected(!!googleDocsAccessToken);
  }, [googleDocsAccessToken]);

  const getMatterLabel = (matterId?: string) => {
    if (!matterId) return 'General';
    const matter = matters.find(m => m.id === matterId);
    if (!matter) return 'Unknown case';
    return `${matter.caseNumber} - ${matter.name}`;
  };

  const isClientUpload = (doc: DocumentFile) => {
    if (doc.uploadedBy && client?.id) {
      return doc.uploadedBy === client.id;
    }
    if (doc.content && !doc.filePath) {
      return true;
    }
    return false;
  };

  const getPermissions = (doc: DocumentFile) => ({
    canView: doc.permissions?.canView ?? true,
    canDownload: doc.permissions?.canDownload ?? true,
    canComment: doc.permissions?.canComment ?? true
  });

  const getSearchHaystack = (doc: DocumentFile) => {
    return [
      doc.name,
      doc.description,
      ...(doc.tags || []),
      doc.category
    ]
      .filter(Boolean)
      .join(' ')
      .toLowerCase();
  };

  const closeViewer = () => {
    if (docObjectUrl) {
      URL.revokeObjectURL(docObjectUrl);
    }
    setDocObjectUrl(null);
    setViewingDoc(null);
    setDocContent('');
    setComments([]);
    setCommentDraft('');
  };

  const loadComments = async (doc: DocumentFile) => {
    if (!doc.filePath || !getPermissions(doc).canView) {
      setComments([]);
      return;
    }
    setCommentsLoading(true);
    try {
      const data = await clientApi.fetchJson(`/documents/${doc.id}/comments`);
      setComments(Array.isArray(data) ? data : []);
    } catch (error) {
      console.error('Failed to load comments:', error);
      setComments([]);
    } finally {
      setCommentsLoading(false);
    }
  };

  const handleAddComment = async () => {
    if (!viewingDoc || !getPermissions(viewingDoc).canComment) return;
    const body = commentDraft.trim();
    if (!body) return;
    setCommentSending(true);
    try {
      const created = await clientApi.fetchJson(`/documents/${viewingDoc.id}/comments`, {
        method: 'POST',
        body: JSON.stringify({ body })
      });
      if (created) {
        setComments(prev => [...prev, created]);
        setCommentDraft('');
      }
    } catch (error) {
      console.error('Failed to add comment:', error);
      toast.error('Unable to add comment. Please try again.');
    } finally {
      setCommentSending(false);
    }
  };

  const handleGoogleDocsConnect = async () => {
    const clientId = getGoogleClientId();

    if (!clientId) return;

    try {
      const state = await requestOAuthState('google', 'google-docs', getCurrentAppReturnPath('/client'));
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
      const newDocs = docs.map(doc => ({
        id: doc.id,
        name: doc.name,
        type: 'docx' as const,
        size: 'Google Doc',
        updatedAt: getNormalizedDocumentDate(doc.modifiedTime || doc.createdTime),
        matterId: undefined,
        content: doc.webViewLink || '',
        uploadedBy: client?.id
      }));

      const existingDocs = JSON.parse(localStorage.getItem('client_documents') || '[]');
      const updatedDocs = [...newDocs, ...existingDocs];
      localStorage.setItem('client_documents', JSON.stringify(updatedDocs));

      setDocuments(prev => [...newDocs, ...prev]);
      toast.success('Google Docs synced successfully!');
    } catch (error) {
      console.error('Google Docs sync error:', error);
      toast.error('Failed to sync Google Docs. Please reconnect.');
      clearOAuthTokens('google-docs');
      setGoogleDocsAccessToken(null);
      setIsGoogleDocsConnected(false);
    }
  };

  useEffect(() => {
    const loadData = async () => {
      try {
        const [mattersData, docsData] = await Promise.all([
          clientApi.fetchJson('/matters'),
          clientApi.fetchJson('/documents')
        ]);
        setMatters(mattersData);

        const serverDocs = Array.isArray(docsData) ? docsData.map(mapServerDocument) : [];

        const storedDocs = localStorage.getItem('client_documents') || localStorage.getItem('documents');
        const localDocs = storedDocs ? JSON.parse(storedDocs) : [];
        const mappedLocalDocs = Array.isArray(localDocs)
          ? localDocs.map((doc: any) => ({
            ...doc,
            updatedAt: getNormalizedDocumentDate(doc.updatedAt || doc.createdAt),
            tags: parseTags(doc.tags),
            uploadedBy: doc.uploadedBy || client?.id,
            filePath: doc.filePath ? normalizeFilePath(doc.filePath) : doc.filePath
          }))
          : [];

        setDocuments([...serverDocs, ...mappedLocalDocs]);
      } catch (error) {
        console.error('Error loading documents:', error);
      } finally {
        setLoading(false);
      }
    };

    loadData();
  }, [client?.id]);

  const query = searchQuery.trim().toLowerCase();
  const filteredDocs = documents.filter(doc => {
    if (selectedMatter && doc.matterId !== selectedMatter) return false;
    if (filterSource === 'client' && !isClientUpload(doc)) return false;
    if (filterSource === 'firm' && isClientUpload(doc)) return false;
    if (query.length >= 2 && !getSearchHaystack(doc).includes(query)) return false;
    return true;
  });

  const sortedDocs = [...filteredDocs].sort((a, b) => {
    if (sortBy === 'name') {
      return a.name.localeCompare(b.name);
    }
    return getSafeTimestamp(b.updatedAt) - getSafeTimestamp(a.updatedAt);
  });

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      const file = e.target.files[0];
      setPendingFile(file);
      setShowUploadModal(true);
    }
  };

  const handleConfirmUpload = async () => {
    if (!pendingFile) return;

    try {
      const formData = new FormData();
      formData.append('file', pendingFile);
      if (selectedMatterForUpload) {
        formData.append('matterId', selectedMatterForUpload);
      }
      if (uploadDescription.trim()) {
        formData.append('description', uploadDescription.trim());
      }

      const res = await clientApi.fetch('/documents/upload', {
        method: 'POST',
        body: formData
      });

      if (!res.ok) {
        throw new Error('Upload failed');
      }

      const data = await res.json();
      const mapped = mapServerDocument(data);
      setDocuments(prev => [mapped, ...prev]);
      toast.success('Document uploaded successfully!');
    } catch (error) {
      console.error('Error uploading document:', error);
      toast.error('Failed to upload document. Please try again.');
    } finally {
      setShowUploadModal(false);
      setPendingFile(null);
      setSelectedMatterForUpload('');
      setUploadDescription('');
    }
  };

  const handleOpen = async (doc: DocumentFile) => {
    const { canView } = getPermissions(doc);
    if (!canView) {
      toast.error('You do not have permission to view this document.');
      return;
    }

    if (doc.content && doc.content.startsWith('http')) {
      window.open(doc.content, '_blank');
      return;
    }

    if (!doc.content && !doc.filePath) {
      toast.warning('No content available for this file.');
      return;
    }

    setViewingDoc(doc);
    setLoadingContent(true);
    setDocContent('');
    setComments([]);
    setCommentDraft('');
    loadComments(doc);
    if (docObjectUrl) {
      URL.revokeObjectURL(docObjectUrl);
      setDocObjectUrl(null);
    }

    try {
      if (doc.content) {
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
        } else {
          setDocContent(doc.content as string);
        }
      } else if (doc.filePath) {
        const response = await clientApi.fetch(`/documents/${doc.id}/download`);
        if (!response.ok) {
          throw new Error('Document download failed');
        }
        const blob = await response.blob();
        if (doc.type === 'txt') {
          const text = await blob.text();
          setDocContent(text);
        } else if (doc.type === 'docx') {
          const arrayBuffer = await blob.arrayBuffer();
          const result = await mammoth.convertToHtml({ arrayBuffer });
          setDocContent(result.value);
        } else {
          const url = window.URL.createObjectURL(blob);
          setDocObjectUrl(url);
          setDocContent(url);
        }
      }
    } catch (error) {
      console.error('Error opening document:', error);
      toast.error('An error occurred while opening the file.');
      closeViewer();
    } finally {
      setLoadingContent(false);
    }
  };

  const handleDownload = async (doc: DocumentFile) => {
    try {
      const { canDownload } = getPermissions(doc);
      if (!canDownload) {
        toast.error('You do not have permission to download this document.');
        return;
      }

      if (doc.filePath) {
        const response = await clientApi.fetch(`/documents/${doc.id}/download`);
        if (!response.ok) {
          throw new Error('File download failed');
        }
        const blob = await response.blob();
        const url = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = doc.name;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        window.URL.revokeObjectURL(url);
        return;
      }

      if (doc.content) {
        const link = document.createElement('a');
        link.href = doc.content as string;
        link.download = doc.name;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        return;
      }

      toast.warning('No content available for this file.');
    } catch (error: any) {
      console.error('Download error:', error);
      toast.error('Failed to download file.');
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-gray-400">Loading...</div>
      </div>
    );
  }

  return (
    <div className="p-8 h-full overflow-y-auto">
      <div className="mb-6">
        <div className="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-4">
          <div>
            <h2 className="text-2xl font-bold text-slate-900">Documents</h2>
            <p className="text-gray-600 mt-1">Access and upload documents related to your cases</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <input type="file" className="hidden" ref={fileInputRef} onChange={handleFileChange} />
            {isGoogleDocsConnected ? (
              <button
                onClick={handleGoogleDocsSync}
                className="flex items-center gap-2 px-4 py-2 bg-green-600 text-white rounded-lg text-sm font-medium hover:bg-green-700 transition-colors shadow-sm"
              >
                <FileText className="w-4 h-4" /> Sync Google Docs
              </button>
            ) : (
              <button
                onClick={handleGoogleDocsConnect}
                className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors shadow-sm"
              >
                <FileText className="w-4 h-4" /> Connect Google Docs
              </button>
            )}
            <button
              onClick={() => fileInputRef.current?.click()}
              className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors shadow-sm"
            >
              <Plus className="w-4 h-4" /> Upload Document
            </button>
          </div>
        </div>

        <div className="mt-4 flex flex-col lg:flex-row lg:items-center gap-3">
          <div className="flex items-center gap-2 px-3 py-2 bg-white border border-gray-200 rounded-lg w-full lg:max-w-md">
            <Search className="w-4 h-4 text-gray-400" />
            <input
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder="Search by name, tag, or description..."
              className="w-full text-sm text-slate-700 placeholder:text-gray-400 outline-none"
            />
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {([
              { key: 'all', label: 'All' },
              { key: 'firm', label: 'Firm Shared' },
              { key: 'client', label: 'My Uploads' }
            ] as const).map(item => (
              <button
                key={item.key}
                onClick={() => setFilterSource(item.key)}
                className={`px-3 py-2 rounded-lg text-xs font-semibold border ${
                  filterSource === item.key
                    ? 'bg-blue-50 border-blue-200 text-blue-700'
                    : 'bg-white border-gray-200 text-gray-600 hover:border-gray-300'
                }`}
              >
                {item.label}
              </button>
            ))}
          </div>

          <div className="flex items-center gap-2">
            <span className="text-xs text-gray-500">Sort by</span>
            <select
              value={sortBy}
              onChange={(e) => setSortBy(e.target.value as 'recent' | 'name')}
              className="px-3 py-2 border border-gray-200 rounded-lg text-sm bg-white"
            >
              <option value="recent">Most Recent</option>
              <option value="name">Name</option>
            </select>
          </div>
        </div>
      </div>

      <div className="flex gap-6">
        {/* Matter Filter */}
        <div className="w-64 bg-white rounded-xl shadow-sm border border-gray-200 p-4">
          <h3 className="font-bold text-slate-900 mb-3">Filter by Case</h3>
          <button
            onClick={() => setSelectedMatter(null)}
            className={`w-full text-left px-3 py-2 rounded-lg mb-2 ${selectedMatter === null ? 'bg-blue-100 text-blue-700 font-medium' : 'hover:bg-gray-100'
              }`}
          >
            All Documents
          </button>
          {matters.map(matter => (
            <button
              key={matter.id}
              onClick={() => setSelectedMatter(matter.id)}
              className={`w-full text-left px-3 py-2 rounded-lg mb-2 ${selectedMatter === matter.id ? 'bg-blue-100 text-blue-700 font-medium' : 'hover:bg-gray-100'
                }`}
            >
              {matter.caseNumber}
            </button>
          ))}
        </div>

        {/* Documents Grid */}
        <div className="flex-1">
          {sortedDocs.length === 0 ? (
            <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-12 text-center">
              <Folder className="w-16 h-16 text-gray-300 mx-auto mb-4" />
              <p className="text-gray-400">No documents found</p>
              {searchQuery.trim().length > 0 && (
                <p className="text-xs text-gray-400 mt-2">Try adjusting your search or filters.</p>
              )}
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {sortedDocs.map(doc => {
                const isMine = isClientUpload(doc);
                const { canView, canDownload } = getPermissions(doc);
                return (
                  <div key={doc.id} className="bg-white rounded-xl shadow-sm border border-gray-200 p-4 hover:shadow-md transition-shadow">
                    <div className="flex items-start justify-between mb-3">
                      <div className={`w-10 h-10 rounded-lg flex items-center justify-center ${doc.type === 'pdf' ? 'bg-red-50 text-red-500' : 'bg-blue-50 text-blue-600'
                        }`}>
                        <FileText className="w-6 h-6" />
                      </div>
                      <div className="flex gap-1">
                        <button
                          onClick={() => handleOpen(doc)}
                          disabled={!canView}
                          className={`p-1 ${canView ? 'text-blue-600 hover:text-blue-800' : 'text-gray-300 cursor-not-allowed'}`}
                          title="View"
                        >
                          <FileText className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => handleDownload(doc)}
                          disabled={!canDownload}
                          className={`p-1 ${canDownload ? 'text-gray-400 hover:text-gray-600' : 'text-gray-300 cursor-not-allowed'}`}
                          title="Download"
                        >
                          <Download className="w-4 h-4" />
                        </button>
                      </div>
                    </div>
                    <div className="flex items-center justify-between gap-2">
                      <h4 className="font-semibold text-slate-900 truncate">{doc.name}</h4>
                      <span className={`text-[10px] px-2 py-0.5 rounded-full font-semibold ${isMine ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-600'}`}>
                        {isMine ? 'My Upload' : 'Firm Shared'}
                      </span>
                    </div>
                    {doc.description && (
                      <p className="text-xs text-gray-500 mt-1 line-clamp-2">{doc.description}</p>
                    )}
                    <div className="text-xs text-gray-500 mt-2">
                      {doc.size || 'Unknown size'} - {formatDocumentDate(doc.updatedAt)}
                    </div>
                    <div className="mt-2 text-[11px] text-gray-400 truncate">
                      {getMatterLabel(doc.matterId)}
                    </div>
                    {doc.tags && doc.tags.length > 0 && (
                      <div className="mt-2 flex flex-wrap gap-2">
                        {doc.tags.slice(0, 3).map(tag => (
                          <span key={tag} className="text-[10px] px-2 py-0.5 rounded-full bg-blue-50 text-blue-700 border border-blue-100">
                            {tag}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Upload Modal */}
      {showUploadModal && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-md">
            <div className="px-6 py-4 border-b border-gray-200">
              <h3 className="font-bold text-lg text-slate-800">Upload Document</h3>
              <p className="text-sm text-gray-500 mt-1">{pendingFile?.name}</p>
            </div>

            <div className="px-6 py-4">
              <label className="block text-sm font-medium text-gray-700 mb-2">Select Case (Optional)</label>
              <select
                value={selectedMatterForUpload}
                onChange={(e) => setSelectedMatterForUpload(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-transparent"
              >
                <option value="">-- No Case (General) --</option>
                {matters.map(m => (
                  <option key={m.id} value={m.id}>
                    {m.caseNumber} - {m.name}
                  </option>
                ))}
              </select>

              <label className="block text-sm font-medium text-gray-700 mb-2 mt-4">Description (Optional)</label>
              <textarea
                value={uploadDescription}
                onChange={(e) => setUploadDescription(e.target.value)}
                placeholder="Add a short description for this document"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-transparent resize-none"
                rows={3}
              />
            </div>

            <div className="px-6 py-4 border-t border-gray-200 flex justify-end gap-3">
              <button
                onClick={() => {
                  setShowUploadModal(false);
                  setPendingFile(null);
                  setSelectedMatterForUpload('');
                  setUploadDescription('');
                }}
                className="px-4 py-2 text-gray-600 hover:text-gray-800 text-sm font-medium"
              >
                Cancel
              </button>
              <button
                onClick={handleConfirmUpload}
                className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700"
              >
                Upload
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Document Viewer Modal */}
      {viewingDoc && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-4xl h-[90vh] flex flex-col">
            <div className="px-6 py-4 border-b border-gray-200 flex justify-between items-center bg-gray-50">
              <div>
                <h3 className="font-bold text-lg text-slate-800">{viewingDoc.name}</h3>
                <p className="text-xs text-gray-500 mt-1">
                  {viewingDoc.size} - {formatDocumentDate(viewingDoc.updatedAt)} - {getMatterLabel(viewingDoc.matterId)}
                </p>
              </div>
              <button
                onClick={closeViewer}
                className="text-gray-400 hover:text-gray-600"
              >
                <X className="w-6 h-6" />
              </button>
            </div>

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

            {viewingDoc.filePath && (
              <div className="border-t border-gray-200 bg-gray-50 p-6 space-y-4">
                <div className="flex items-center justify-between">
                  <h4 className="text-sm font-semibold text-slate-800">Comments</h4>
                  <span className="text-xs text-gray-500">{comments.length} total</span>
                </div>
                {commentsLoading ? (
                  <div className="text-sm text-gray-400">Loading comments...</div>
                ) : comments.length === 0 ? (
                  <div className="text-sm text-gray-400">No comments yet.</div>
                ) : (
                  <div className="space-y-3 max-h-48 overflow-y-auto">
                    {comments.map((comment: any) => (
                      <div key={comment.id} className="bg-white border border-gray-200 rounded-lg p-3">
                        <div className="flex items-center justify-between text-xs text-gray-500 mb-2">
                          <span className="font-semibold text-slate-700">
                            {comment.author?.name || 'Unknown'}
                          </span>
                          <span>{formatDateTime(comment.createdAt)}</span>
                        </div>
                        <p className="text-sm text-slate-700 whitespace-pre-wrap">{comment.body}</p>
                      </div>
                    ))}
                  </div>
                )}

                {getPermissions(viewingDoc).canComment ? (
                  <div className="flex items-center gap-3">
                    <input
                      value={commentDraft}
                      onChange={(e) => setCommentDraft(e.target.value)}
                      placeholder="Add a comment..."
                      className="flex-1 px-3 py-2 border border-gray-300 rounded-lg text-sm focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                    />
                    <button
                      onClick={handleAddComment}
                      disabled={commentSending || !commentDraft.trim()}
                      className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-semibold hover:bg-blue-700 disabled:opacity-60"
                    >
                      {commentSending ? 'Saving...' : 'Post'}
                    </button>
                  </div>
                ) : (
                  <div className="text-xs text-gray-400">Commenting is disabled for this document.</div>
                )}
              </div>
            )}

            <div className="px-6 py-4 border-t border-gray-200 bg-gray-50 flex justify-end gap-3">
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
    </div>
  );
};

export default ClientDocuments;

