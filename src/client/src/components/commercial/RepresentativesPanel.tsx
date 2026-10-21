import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { updateContract } from '@/lib/api';
import { repToRow, representativesBody, type RepresentativeRow } from '@/lib/contractExtras';
import { describeError } from '@/lib/errors';
import type { ContractDetail } from '@/lib/types';
import {
  btnCls, btnDangerCls, btnSecondaryCls, ErrorBox, inputCls, Panel, tdCls, thCls,
} from '@/components/forms';

const emptyRow: RepresentativeRow = { name: '', email: '', phone: '', authority: '' };

/** The customer people authorised to order/cancel booked days and approve
 * Additional PAD (§14). Saved with the contract (replace-all list). */
export function RepresentativesPanel({ contract, canEdit, onSaved }: {
  contract: ContractDetail;
  canEdit: boolean;
  onSaved: () => void;
}) {
  const { t } = useTranslation();
  const [rows, setRows] = useState<RepresentativeRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const reps = contract.representatives ?? [];

  const setRow = (i: number, patch: Partial<RepresentativeRow>) =>
    setRows((rs) => (rs ?? []).map((r, j) => (j === i ? { ...r, ...patch } : r)));

  const save = async () => {
    if (!rows) return;
    setError(null);
    try {
      await updateContract(contract.id, {
        customerId: contract.customerId,
        endClientId: contract.endClientId,
        endClientReference: contract.endClientReference,
        name: contract.name,
        externalReference: contract.externalReference,
        startDate: contract.startDate,
        endDate: contract.endDate,
        isActive: contract.isActive,
        representatives: representativesBody(rows),
      });
    } catch (e) { setError(describeError(e, t)); return; }
    setRows(null);
    onSaved();
  };

  return (
    <Panel
      title={t('representatives.title')}
      help={t('representatives.help')}
      actions={canEdit && !rows && (
        <button className={btnSecondaryCls} onClick={() => setRows(reps.length ? reps.map(repToRow) : [emptyRow])}>
          {t('common.edit')}
        </button>
      )}
    >
      <ErrorBox message={error} />
      {rows ? (
        <div className="rounded border border-slate-200 bg-slate-50 p-3">
          {rows.map((r, i) => (
            <div key={i} className="mb-1 grid grid-cols-2 gap-2 sm:grid-cols-5">
              <input className={inputCls} placeholder={t('representatives.name')} value={r.name}
                onChange={(e) => setRow(i, { name: e.target.value })} />
              <input className={inputCls} placeholder={t('representatives.email')} value={r.email}
                onChange={(e) => setRow(i, { email: e.target.value })} />
              <input className={inputCls} placeholder={t('representatives.phone')} value={r.phone}
                onChange={(e) => setRow(i, { phone: e.target.value })} />
              <input className={inputCls} placeholder={t('representatives.authority')}
                title={t('representatives.hAuthority')} value={r.authority}
                onChange={(e) => setRow(i, { authority: e.target.value })} />
              <button className={btnDangerCls} onClick={() => setRows(rows.filter((_, j) => j !== i))}>
                {t('common.delete')}
              </button>
            </div>
          ))}
          <div className="mt-2 flex gap-2">
            <button className={btnSecondaryCls} onClick={() => setRows([...rows, emptyRow])}>
              {t('representatives.add')}
            </button>
            <button className={btnCls} onClick={() => void save()}>{t('common.save')}</button>
            <button className={btnSecondaryCls} onClick={() => setRows(null)}>{t('common.cancel')}</button>
          </div>
        </div>
      ) : (
        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
              <th className={thCls}>{t('representatives.name')}</th>
              <th className={thCls}>{t('representatives.email')}</th>
              <th className={thCls}>{t('representatives.phone')}</th>
              <th className={thCls}>{t('representatives.authority')}</th>
            </tr>
          </thead>
          <tbody>
            {reps.map((r) => (
              <tr key={r.name} className="border-b border-slate-100">
                <td className={tdCls}>{r.name}</td>
                <td className={tdCls}>{r.email ?? '—'}</td>
                <td className={tdCls}>{r.phone ?? '—'}</td>
                <td className={tdCls}>{r.authority ?? '—'}</td>
              </tr>
            ))}
            {reps.length === 0 && (
              <tr><td className={tdCls} colSpan={4}>{t('common.none')}</td></tr>
            )}
          </tbody>
        </table>
      )}
    </Panel>
  );
}
