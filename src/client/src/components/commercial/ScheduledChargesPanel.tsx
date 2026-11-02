import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createScheduledCharge, deleteScheduledCharge, getScheduledCharges, updateScheduledCharge,
} from '@/lib/api';
import { confirmAction } from '@/lib/confirm';
import {
  billedProjectName, canChangeCharge, chargeBody, chargeToForm, emptyCharge, type ChargeForm,
} from '@/lib/contractExtras';
import { describeError } from '@/lib/errors';
import type { ProjectSummary, ScheduledCharge } from '@/lib/types';
import {
  btnCls, btnDangerCls, btnSecondaryCls, ErrorBox, Field, inputCls, NumInput, Panel, tdCls, thCls,
} from '@/components/forms';

/** One-off dated amounts on the contract (replaces the old "fixed price"
 * terms component) — each bills once, on its project's spec for its month. */
export function ScheduledChargesPanel({ contractId, projects, currency, canEdit }: {
  contractId: string;
  projects: ProjectSummary[];
  currency: string;
  canEdit: boolean;
}) {
  const { t } = useTranslation();
  const [charges, setCharges] = useState<ScheduledCharge[]>([]);
  const [form, setForm] = useState<ChargeForm | null>(null);
  const [editId, setEditId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(
    () => getScheduledCharges(contractId).then(setCharges).catch(() => setCharges([])),
    [contractId],
  );
  useEffect(() => { void reload(); }, [reload]);

  const body = form ? chargeBody(form) : null;

  const save = async () => {
    if (!body) return;
    setError(null);
    try {
      if (editId) await updateScheduledCharge(contractId, editId, body);
      else await createScheduledCharge(contractId, body);
    } catch (e) { setError(describeError(e, t)); return; }
    setForm(null);
    setEditId(null);
    await reload();
  };

  const remove = async (c: ScheduledCharge) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    setError(null);
    try { await deleteScheduledCharge(contractId, c.id); await reload(); }
    catch (e) { setError(describeError(e, t)); }
  };

  return (
    <Panel
      title={t('charges.title')}
      help={t('charges.help')}
      actions={canEdit && !form && (
        <button className={btnSecondaryCls} onClick={() => { setEditId(null); setForm(emptyCharge); }}>
          {t('charges.add')}
        </button>
      )}
    >
      <ErrorBox message={error} />
      {form && (
        <div className="mb-3 rounded border border-slate-200 bg-slate-50 p-3">
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <Field label={t('charges.date')} help={t('charges.hDate')}>
              <input type="date" className={inputCls} value={form.date}
                onChange={(e) => setForm({ ...form, date: e.target.value })} />
            </Field>
            <Field label={`${t('charges.amount')} (${currency})`} help={t('charges.hAmount')}>
              <NumInput className={inputCls} value={form.amount}
                onChange={(v) => setForm({ ...form, amount: v })} />
            </Field>
            <Field label={t('charges.description')} help={t('charges.hDescription')}>
              <input className={inputCls} value={form.description}
                onChange={(e) => setForm({ ...form, description: e.target.value })} />
            </Field>
            <Field label={t('charges.project')} help={t('charges.hProject')}>
              <select className={inputCls} value={form.projectId}
                onChange={(e) => setForm({ ...form, projectId: e.target.value })}>
                <option value="">{t('charges.projectDefault')}</option>
                {projects.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </Field>
          </div>
          <div className="mt-3 flex gap-2">
            <button className={btnCls} disabled={!body} onClick={() => void save()}>{t('common.save')}</button>
            <button className={btnSecondaryCls} onClick={() => { setForm(null); setEditId(null); }}>
              {t('common.cancel')}
            </button>
          </div>
        </div>
      )}
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('charges.date')}</th>
            <th className={thCls}>{t('charges.description')}</th>
            <th className={thCls}>{t('charges.amount')}</th>
            <th className={thCls}>{t('charges.billedOn')}</th>
            <th className={thCls}>{t('common.status')}</th>
            {canEdit && <th className={thCls}>{t('common.actions')}</th>}
          </tr>
        </thead>
        <tbody>
          {charges.map((c) => (
            <tr key={c.id} className="border-b border-slate-100">
              <td className={tdCls}>{c.date}</td>
              <td className={tdCls}>{c.description}</td>
              <td className={tdCls}>{c.amount} {currency}</td>
              <td className={tdCls}>{billedProjectName(c, projects) ?? '—'}</td>
              <td className={tdCls}>{c.invoiced ? t('charges.invoiced') : t('charges.open')}</td>
              {canEdit && (
                <td className={tdCls}>
                  {canChangeCharge(c) && (
                    <span className="flex gap-1">
                      <button className={btnSecondaryCls}
                        onClick={() => { setEditId(c.id); setForm(chargeToForm(c)); }}>
                        {t('common.edit')}
                      </button>
                      <button className={btnDangerCls} onClick={() => void remove(c)}>
                        {t('common.delete')}
                      </button>
                    </span>
                  )}
                </td>
              )}
            </tr>
          ))}
          {charges.length === 0 && (
            <tr><td className={tdCls} colSpan={6}>{t('common.none')}</td></tr>
          )}
        </tbody>
      </table>
    </Panel>
  );
}
