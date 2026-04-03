import React, { useEffect, useState } from 'react';
import { MatterNote } from '../types';
import { api } from '../services/api';
import { toast } from './Toast';

type MatterNotesPanelProps = {
  matterId: string;
};

const emptyDraft = { title: '', body: '' };

const MatterNotesPanel: React.FC<MatterNotesPanelProps> = ({ matterId }) => {
  const [notes, setNotes] = useState<MatterNote[]>([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [editingNoteId, setEditingNoteId] = useState<string | null>(null);
  const [draft, setDraft] = useState(emptyDraft);

  useEffect(() => {
    let disposed = false;

    const loadNotes = async () => {
      setLoading(true);
      try {
        const items = await api.getMatterNotes(matterId);
        if (!disposed) {
          setNotes(Array.isArray(items) ? items : []);
        }
      } catch (error) {
        console.error('Failed to load matter notes', error);
        if (!disposed) {
          setNotes([]);
          toast.error('Unable to load matter notes.');
        }
      } finally {
        if (!disposed) {
          setLoading(false);
        }
      }
    };

    setEditingNoteId(null);
    setDraft(emptyDraft);
    loadNotes();

    return () => {
      disposed = true;
    };
  }, [matterId]);

  const resetDraft = () => {
    setEditingNoteId(null);
    setDraft(emptyDraft);
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!draft.body.trim()) {
      toast.error('Note body is required.');
      return;
    }

    setSaving(true);
    try {
      if (editingNoteId) {
        const updated = await api.updateMatterNote(matterId, editingNoteId, {
          title: draft.title.trim() || undefined,
          body: draft.body.trim()
        });
        if (updated) {
          setNotes((prev) => prev.map((note) => (note.id === editingNoteId ? updated : note)));
          toast.success('Note updated.');
        }
      } else {
        const created = await api.createMatterNote(matterId, {
          title: draft.title.trim() || undefined,
          body: draft.body.trim()
        });
        if (created) {
          setNotes((prev) => [created, ...prev]);
          toast.success('Note saved.');
        }
      }

      resetDraft();
    } catch (error) {
      console.error('Failed to save matter note', error);
      toast.error('Unable to save note.');
    } finally {
      setSaving(false);
    }
  };

  const startEdit = (note: MatterNote) => {
    setEditingNoteId(note.id);
    setDraft({
      title: note.title || '',
      body: note.body || ''
    });
  };

  const handleDelete = async (noteId: string) => {
    if (!window.confirm('Delete this note?')) {
      return;
    }

    try {
      await api.deleteMatterNote(matterId, noteId);
      setNotes((prev) => prev.filter((note) => note.id !== noteId));
      if (editingNoteId === noteId) {
        resetDraft();
      }
      toast.success('Note deleted.');
    } catch (error) {
      console.error('Failed to delete matter note', error);
      toast.error('Unable to delete note.');
    }
  };

  return (
    <div className="px-6 py-5 border-b border-gray-100 bg-white">
      <div className="flex items-start justify-between gap-3 mb-4">
        <div>
          <h3 className="text-sm font-bold text-slate-800 uppercase tracking-wide">Matter Notes</h3>
          <p className="text-xs text-gray-500 mt-1">
            Internal notes for strategy, status, and team context. These notes are not shown in the client portal.
          </p>
        </div>
      </div>

      <form onSubmit={handleSubmit} className="rounded-xl border border-slate-200 bg-slate-50 p-4 space-y-3">
        <div>
          <label className="block text-xs font-semibold text-gray-600 mb-1 uppercase">Title (optional)</label>
          <input
            type="text"
            value={draft.title}
            onChange={(e) => setDraft((prev) => ({ ...prev, title: e.target.value }))}
            placeholder="Status update, hearing prep, negotiation note..."
            className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
          />
        </div>
        <div>
          <label className="block text-xs font-semibold text-gray-600 mb-1 uppercase">Note</label>
          <textarea
            rows={4}
            value={draft.body}
            onChange={(e) => setDraft((prev) => ({ ...prev, body: e.target.value }))}
            placeholder="Add an internal note for this matter..."
            className="w-full border border-gray-300 rounded-lg p-2.5 text-sm bg-white text-slate-900 outline-none"
          />
        </div>
        <div className="flex justify-end gap-2">
          {editingNoteId && (
            <button
              type="button"
              onClick={resetDraft}
              className="px-3 py-2 text-sm font-semibold text-gray-600 rounded-lg hover:bg-gray-100"
            >
              Cancel
            </button>
          )}
          <button
            type="submit"
            disabled={saving}
            className="px-4 py-2 text-sm font-bold text-white bg-slate-800 hover:bg-slate-900 rounded-lg disabled:opacity-50"
          >
            {saving ? 'Saving...' : editingNoteId ? 'Update Note' : 'Save Note'}
          </button>
        </div>
      </form>

      <div className="mt-4 space-y-3">
        {loading ? (
          <div className="text-xs text-gray-500">Loading notes...</div>
        ) : notes.length === 0 ? (
          <div className="rounded-lg border border-dashed border-gray-200 bg-gray-50 px-4 py-6 text-sm text-gray-500">
            No notes yet for this matter.
          </div>
        ) : (
          notes.map((note) => (
            <div key={note.id} className="rounded-xl border border-gray-200 bg-white p-4">
              <div className="flex items-start justify-between gap-3">
                <div>
                  {note.title && <h4 className="text-sm font-bold text-slate-800">{note.title}</h4>}
                  <p className="text-xs text-gray-500 mt-1">
                    {note.updatedByName || note.createdByName || 'Staff'} - {new Date(note.updatedAt || note.createdAt).toLocaleString('en-US')}
                  </p>
                </div>
                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => startEdit(note)}
                    className="text-xs font-semibold text-primary-600 hover:underline"
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    onClick={() => handleDelete(note.id)}
                    className="text-xs font-semibold text-red-600 hover:underline"
                  >
                    Delete
                  </button>
                </div>
              </div>
              <p className="mt-3 whitespace-pre-wrap text-sm text-slate-700">{note.body}</p>
            </div>
          ))
        )}
      </div>
    </div>
  );
};

export default MatterNotesPanel;
