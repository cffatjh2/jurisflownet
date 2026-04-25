// Google Docs API Service
// Note: This requires OAuth2 setup in Google Cloud Console
// 1. Enable Google Docs API in your project
// 2. Use the same OAuth2 credentials as Gmail

const DOCS_API_BASE = 'https://docs.googleapis.com/v1';

export interface GoogleDoc {
  id: string;
  name: string;
  mimeType: string;
  createdTime: string;
  modifiedTime: string;
  webViewLink: string;
}

export const googleDocsService = {
  // Get documents list
  getDocuments: async (accessToken: string): Promise<GoogleDoc[]> => {
    const params = new URLSearchParams({
      q: "mimeType='application/vnd.google-apps.document' and trashed=false",
      orderBy: 'modifiedTime desc',
      pageSize: '50',
      includeItemsFromAllDrives: 'true',
      supportsAllDrives: 'true',
      fields: 'files(id,name,mimeType,createdTime,modifiedTime,webViewLink)'
    });

    const response = await fetch(`https://www.googleapis.com/drive/v3/files?${params.toString()}`, {
      headers: {
        'Authorization': `Bearer ${accessToken}`
      }
    });

    if (!response.ok) {
      throw new Error(`Google Docs listing failed with status ${response.status}`);
    }

    const data = await response.json();
    return Array.isArray(data.files) ? data.files : [];
  },

  // Get document content
  getDocumentContent: async (accessToken: string, documentId: string): Promise<string> => {
    const response = await fetch(`${DOCS_API_BASE}/documents/${documentId}`, {
      headers: {
        'Authorization': `Bearer ${accessToken}`
      }
    });
    const doc = await response.json();
    
    // Convert Google Docs format to HTML
    let html = '';
    if (doc.body?.content) {
      doc.body.content.forEach((element: any) => {
        if (element.paragraph) {
          element.paragraph.elements?.forEach((el: any) => {
            if (el.textRun) {
              html += el.textRun.text;
            }
          });
          html += '<br>';
        }
      });
    }
    
    return html;
  },

  // Create a new document
  createDocument: async (accessToken: string, title: string): Promise<GoogleDoc> => {
    const response = await fetch(`${DOCS_API_BASE}/documents`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${accessToken}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ title })
    });
    return response.json();
  },

  // Export document as PDF/DOCX
  exportDocument: async (accessToken: string, documentId: string, mimeType: 'application/pdf' | 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'): Promise<Blob> => {
    const response = await fetch(
      `https://www.googleapis.com/drive/v3/files/${documentId}/export?mimeType=${mimeType}`,
      {
        headers: {
          'Authorization': `Bearer ${accessToken}`
        }
      }
    );
    if (!response.ok) {
      throw new Error(`Google Docs export failed with status ${response.status}`);
    }
    return response.blob();
  }
};

