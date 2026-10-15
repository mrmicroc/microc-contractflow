import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { deleteDocument, documentBlob, errorMessage, getDocuments, uploadDocument } from '@/lib/api';
import type { DocumentAttachment, DocumentOwnerType } from '@/lib/types';
import { cn } from '@/lib/utils';

const kb = (n: number) => (n > 1024 * 1024 ? `${(n / 1048576).toFixed(1)} MB` : `${Math.ceil(n / 1024)} kB`);

/** Receipt/evidence list + upload for one owner (expense, booking…). The
 * parent decides writability via `canWrite` (e.g. unlocked timesheet row). */
export function Attachments({ ownerType, ownerId, canWrite }: {
  ownerType: DocumentOwnerType; ownerId: string; canWrite: boolean;
}) {
  const { t } = useTranslation();
  const [docs, setDocs] = useState<DocumentAttachment[]>([]);
  const [error, setError] = useState<string | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const reload = useCallback(() => {
    getDocuments(ownerType, ownerId).then(setDocs).catch(() => {});
  }, [ownerType, ownerId]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  const upload = async (file: File) => {
    setError(null);
    try { await uploadDocument(ownerType, ownerId, file); reload(); }
    catch (e) { setError(errorMessage(e)); }
  };

  const download = async (d: DocumentAttachment) => {
    const blob = await documentBlob(d.id);
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = d.fileName; a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="space-y-1">
      {error && <p className="rounded bg-red-50 px-2 py-1 text-xs text-red-700">{error}</p>}
      {docs.map((d) => (
        <div key={d.id} className="flex items-center gap-2 text-xs">
          <button className="text-blue-700 underline" onClick={() => void download(d)}>
            {d.fileName}
          </button>
          <span className="text-slate-400">{kb(d.sizeBytes)}</span>
          {canWrite && (
            <button className="text-red-600"
              onClick={() => void deleteDocument(d.id).then(reload).catch((e) => setError(errorMessage(e)))}>
              {t('common.delete')}
            </button>
          )}
        </div>
      ))}
      {docs.length === 0 && !canWrite && (
        <span className="text-xs text-slate-400">{t('documents.none')}</span>
      )}
      {canWrite && (
        <>
          <input ref={inputRef} type="file" className="hidden"
            accept=".pdf,.png,.jpg,.jpeg,.webp,.txt"
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) void upload(f);
              e.target.value = '';
            }} />
          <button className={cn('rounded border border-slate-300 px-2 py-0.5 text-xs text-slate-600 hover:bg-slate-50')}
            onClick={() => inputRef.current?.click()}>
            {t('documents.attach')}
          </button>
        </>
      )}
    </div>
  );
}
