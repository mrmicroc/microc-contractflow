import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createContract, deleteContract, errorMessage, getContract, updateContract,
} from '@/lib/api';
import type { ContractSummary, CustomerDetail, EndClient } from '@/lib/types';
import { cn } from '@/lib/utils';
import { isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, Panel, tdCls, thCls,
} from '@/components/forms';

export function ContractSection({
  customer,
  endClients,
  selectedId,
  onSelect,
  onChanged,
  onDeleted,
}: {
  customer: CustomerDetail;
  endClients: EndClient[];
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
  const [f, setF] = useState({
    name: '', externalReference: '',
    startDate: '', endDate: '', endClientId: '', isActive: true,
  });

  const openNew = () => {
    setEditId(null);
    setF({
      name: '', externalReference: '',
      startDate: '', endDate: '', endClientId: '', isActive: true,
    });
    setFormOpen(true);
  };

  const openEdit = async (id: string) => {
    const c = await getContract(id);
    setEditId(id);
    setF({
      name: c.name,
      externalReference: c.externalReference ?? '',
      startDate: c.startDate,
      endDate: c.endDate ?? '',
      endClientId: c.endClientId ?? '',
      isActive: c.isActive,
    });
    setFormOpen(true);
  };

  const save = async () => {
    const body = {
      customerId: customer.id,
      endClientId: f.endClientId || null,
      name: f.name.trim(),
      externalReference: f.externalReference.trim() || null,
      startDate: f.startDate,
      endDate: f.endDate || null,
      isActive: f.isActive,
    };
    if (editId) await updateContract(editId, body);
    else await createContract(body);
    setFormOpen(false);
    onChanged();
  };

  const remove = async (id: string) => {
    if (!window.confirm(t('common.confirmDelete'))) return;
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
            <Field label={t('contracts.endClient')} help={t('contracts.hEndClient')}>
              <select className={inputCls} value={f.endClientId}
                onChange={(e) => setF({ ...f, endClientId: e.target.value })}>
                <option value="">{t('contracts.endClientSameAsCustomer')}</option>
                {endClients.map((e) => (
                  <option key={e.id} value={e.id}>{e.name}</option>
                ))}
              </select>
            </Field>
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
    </Panel>
  );
}
