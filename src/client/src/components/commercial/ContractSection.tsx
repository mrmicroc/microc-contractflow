import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createContract, deleteContract, errorMessage, getContract, updateContract,
} from '@/lib/api';
import { confirmAction } from '@/lib/confirm';
import { contractPartiesBody, endClientOptions } from '@/lib/organisations';
import type {
  AuthorisedRepresentative, ContractSummary, CustomerDetail, CustomerSummary,
} from '@/lib/types';
import { cn } from '@/lib/utils';
import { isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, Panel, tdCls, thCls,
} from '@/components/forms';

export function ContractSection({
  customer,
  organisations,
  selectedId,
  onSelect,
  onChanged,
  onDeleted,
}: {
  customer: CustomerDetail;
  /** The whole organisation register — party and end-client choices. */
  organisations: CustomerSummary[];
  selectedId: string | null;
  onSelect: (id: string) => void;
  onChanged: () => void;
  onDeleted?: (id: string) => void;
}) {
  const { t } = useTranslation();
  const me = useMe();
  const [formOpen, setFormOpen] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const emptyForm = () => ({
    customerId: customer.id, name: '', externalReference: '',
    startDate: '', endDate: '', endClientId: '', endClientReference: '', isActive: true,
    // Kept as loaded — the representatives panel edits them; an update is
    // replace-all, so editing the header must carry them through unchanged.
    representatives: [] as AuthorisedRepresentative[],
  });
  const [f, setF] = useState(emptyForm);

  const openNew = () => {
    setEditId(null);
    setF(emptyForm());
    setFormOpen(true);
  };

  const openEdit = async (id: string) => {
    setError(null);
    let c;
    try { c = await getContract(id); }
    catch (e) { setError(errorMessage(e)); return; }
    setEditId(id);
    setF({
      customerId: c.customerId,
      name: c.name,
      externalReference: c.externalReference ?? '',
      startDate: c.startDate,
      endDate: c.endDate ?? '',
      endClientId: c.endClientId ?? '',
      endClientReference: c.endClientReference ?? '',
      isActive: c.isActive,
      representatives: c.representatives ?? [],
    });
    setFormOpen(true);
  };

  const save = async () => {
    const body = {
      ...contractPartiesBody(f),
      name: f.name.trim(),
      externalReference: f.externalReference.trim() || null,
      startDate: f.startDate,
      endDate: f.endDate || null,
      isActive: f.isActive,
      representatives: f.representatives,
    };
    setError(null);
    try {
      if (editId) await updateContract(editId, body);
      else await createContract(body);
    } catch (e) {
      setError(errorMessage(e));
      return;
    }
    setFormOpen(false);
    onChanged();
  };

  const remove = async (id: string) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    setError(null);
    try {
      await deleteContract(id);
      onDeleted?.(id);
      onChanged();
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <Panel
      title={`${t('contracts.title')} — ${customer.name}`}
      help={t('contracts.help')}
      actions={isAdmin(me) && !formOpen && (
        <button className={btnSecondaryCls} onClick={openNew}>{t('contracts.newContract')}</button>
      )}
    >
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
      {formOpen && (
        <div className="mb-4 rounded border border-slate-200 bg-slate-50 p-3">
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
            <Field label={t('common.name')}>
              <input className={inputCls} value={f.name}
                onChange={(e) => setF({ ...f, name: e.target.value })} />
            </Field>
            <Field label={t('contracts.externalRef')}>
              <input className={inputCls} value={f.externalReference}
                onChange={(e) => setF({ ...f, externalReference: e.target.value })} />
            </Field>
            <Field label={t('contracts.startDate')}>
              <input type="date" className={inputCls} value={f.startDate}
                onChange={(e) => setF({ ...f, startDate: e.target.value })} />
            </Field>
            <Field label={t('contracts.endDate')}>
              <input type="date" className={inputCls} value={f.endDate}
                onChange={(e) => setF({ ...f, endDate: e.target.value })} />
            </Field>
            <Field label={t('contracts.party')} help={t('contracts.hParty')}>
              <select className={inputCls} value={f.customerId}
                onChange={(e) => setF({
                  ...f,
                  customerId: e.target.value,
                  // The new party can't also be the end client.
                  endClientId: f.endClientId === e.target.value ? '' : f.endClientId,
                })}>
                {organisations.map((o) => (
                  <option key={o.id} value={o.id}>{o.name}</option>
                ))}
              </select>
            </Field>
            <Field label={t('contracts.endClient')} help={t('contracts.hEndClient')}>
              <select className={inputCls} value={f.endClientId}
                onChange={(e) => setF({ ...f, endClientId: e.target.value })}>
                <option value="">{t('contracts.endClientDirect')}</option>
                {endClientOptions(organisations, f.customerId).map((o) => (
                  <option key={o.id} value={o.id}>{o.name}</option>
                ))}
              </select>
            </Field>
            {f.endClientId && (
              <Field label={t('contracts.endClientReference')} help={t('contracts.hEndClientReference')}>
                <input className={inputCls} maxLength={100} value={f.endClientReference}
                  onChange={(e) => setF({ ...f, endClientReference: e.target.value })} />
              </Field>
            )}
          </div>
          <div className="mt-3 flex gap-2">
            <button className={btnCls} disabled={!f.name.trim() || !f.startDate}
              onClick={() => void save()}>
              {t('common.save')}
            </button>
            <button className={btnSecondaryCls} onClick={() => setFormOpen(false)}>
              {t('common.cancel')}
            </button>
          </div>
        </div>
      )}

      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={cn(thCls, 'w-6')}></th>
            <th className={thCls}>{t('common.name')}</th>
            <th className={thCls}>{t('contracts.components')}</th>
            <th className={thCls}>{t('contracts.startDate')}</th>
            <th className={thCls}>{t('contracts.endDate')}</th>
            <th className={thCls}>{t('common.active')}</th>
            {isAdmin(me) && <th className={thCls}>{t('common.actions')}</th>}
          </tr>
        </thead>
        <tbody>
          {customer.contracts.map((c: ContractSummary) => (
            <tr
              key={c.id}
              onClick={() => onSelect(c.id)}
              className={cn(
                'cursor-pointer border-b border-slate-100 hover:bg-blue-50/50',
                selectedId === c.id && 'bg-blue-50',
              )}
            >
              <td className={cn(tdCls, 'w-6 text-slate-400')}>
                {selectedId === c.id ? '▾' : '▸'}
              </td>
              <td className={cn(tdCls, 'font-medium')}>{c.name}</td>
              <td className={tdCls}>
                <span className="flex flex-wrap gap-1">
                  {c.components.length === 0 && '—'}
                  {c.components.map((k) => (
                    <span key={k}
                      className="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-700">
                      {t(`contracts.comp.${k}`)}
                    </span>
                  ))}
                </span>
              </td>
              <td className={tdCls}>{c.startDate}</td>
              <td className={tdCls}>{c.endDate ?? '—'}</td>
              <td className={tdCls}>{c.isActive ? t('common.active') : t('common.inactive')}</td>
              {isAdmin(me) && (
                <td className={tdCls}>
                  <span className="flex gap-1">
                    <button
                      className={btnSecondaryCls}
                      onClick={(e) => { e.stopPropagation(); void openEdit(c.id); }}
                    >
                      {t('common.edit')}
                    </button>
                    <button
                      className={btnDangerCls}
                      onClick={(e) => { e.stopPropagation(); void remove(c.id); }}
                    >
                      {t('common.delete')}
                    </button>
                  </span>
                </td>
              )}
            </tr>
          ))}
          {customer.contracts.length === 0 && (
            <tr><td className={tdCls} colSpan={7}>{t('common.none')}</td></tr>
          )}
        </tbody>
      </table>

      {customer.endClientContracts.length > 0 && (
        <div className="mt-5">
          <h4 className="text-sm font-semibold text-slate-700">{t('contracts.asEndClientTitle')}</h4>
          <p className="mb-1.5 text-xs text-slate-500">{t('contracts.asEndClientHelp')}</p>
          <table className="w-full border-collapse">
            <thead>
              <tr className="border-b border-slate-200">
                <th className={cn(thCls, 'w-6')}></th>
                <th className={thCls}>{t('common.name')}</th>
                <th className={thCls}>{t('contracts.viaIntermediary')}</th>
                <th className={thCls}>{t('contracts.endClientReference')}</th>
                <th className={thCls}>{t('contracts.startDate')}</th>
                <th className={thCls}>{t('contracts.endDate')}</th>
                <th className={thCls}>{t('common.active')}</th>
              </tr>
            </thead>
            <tbody>
              {customer.endClientContracts.map((c) => (
                <tr
                  key={c.id}
                  onClick={() => onSelect(c.id)}
                  className={cn(
                    'cursor-pointer border-b border-slate-100 hover:bg-blue-50/50',
                    selectedId === c.id && 'bg-blue-50',
                  )}
                >
                  <td className={cn(tdCls, 'w-6 text-slate-400')}>
                    {selectedId === c.id ? '▾' : '▸'}
                  </td>
                  <td className={cn(tdCls, 'font-medium')}>{c.name}</td>
                  <td className={tdCls}>{c.customerName}</td>
                  <td className={tdCls}>{c.endClientReference ?? '—'}</td>
                  <td className={tdCls}>{c.startDate}</td>
                  <td className={tdCls}>{c.endDate ?? '—'}</td>
                  <td className={tdCls}>{c.isActive ? t('common.active') : t('common.inactive')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}
