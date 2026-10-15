import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createCustomer, deleteCustomer, errorMessage, getCustomer, getCustomers, updateCustomer,
} from '@/lib/api';
import type { CustomerSummary } from '@/lib/types';
import { cn } from '@/lib/utils';
import { canSeeCommercial, isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, Panel, tdCls, thCls,
} from '@/components/forms';

export function CustomerSection({
  selectedId,
  onSelect,
  onDeleted,
}: {
  selectedId: string | null;
  onSelect: (id: string) => void;
  onDeleted?: (id: string) => void;
}) {
  const { t } = useTranslation();
  const me = useMe();
  const [customers, setCustomers] = useState<CustomerSummary[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [f, setF] = useState({ name: '', externalReference: '', notes: '', isActive: true });

  const reload = useCallback(
    () => getCustomers().then(setCustomers).catch(() => setCustomers([])),
    [],
  );
  useEffect(() => { void reload(); }, [reload]);

  if (!canSeeCommercial(me)) {
    return <p className="text-sm text-slate-500">{t('common.noAccess')}</p>;
  }

  const openNew = () => {
    setEditId(null);
    setF({ name: '', externalReference: '', notes: '', isActive: true });
    setFormOpen(true);
  };

  const openEdit = async (id: string) => {
    const c = await getCustomer(id);
    setEditId(id);
    setF({
      name: c.name,
      externalReference: c.externalReference ?? '',
      notes: c.notes ?? '',
      isActive: c.isActive,
    });
    setFormOpen(true);
  };

  const save = async () => {
    const body = {
      name: f.name.trim(),
      externalReference: f.externalReference.trim() || null,
      notes: f.notes.trim() || null,
      isActive: f.isActive,
    };
    if (editId) await updateCustomer(editId, body);
    else await createCustomer(body);
    setFormOpen(false);
    await reload();
  };

  const remove = async (id: string) => {
    if (!window.confirm(t('common.confirmDelete'))) return;
    setError(null);
    try {
      await deleteCustomer(id);
      onDeleted?.(id);
      await reload();
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <Panel
      title={t('customers.title')}
      actions={isAdmin(me) && !formOpen && (
        <button className={btnSecondaryCls} onClick={openNew}>{t('customers.newCustomer')}</button>
      )}
    >
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
      {formOpen && (
        <div className="mb-4 rounded border border-slate-200 bg-slate-50 p-3">
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <Field label={t('common.name')}>
              <input className={inputCls} value={f.name}
                onChange={(e) => setF({ ...f, name: e.target.value })} />
            </Field>
            <Field label={t('customers.externalRef')}>
              <input className={inputCls} value={f.externalReference}
                onChange={(e) => setF({ ...f, externalReference: e.target.value })} />
            </Field>
            <Field label={t('common.notes')}>
              <input className={inputCls} value={f.notes}
                onChange={(e) => setF({ ...f, notes: e.target.value })} />
            </Field>
            <Field label={t('common.active')}>
              <input type="checkbox" className="h-4 w-4" checked={f.isActive}
                onChange={(e) => setF({ ...f, isActive: e.target.checked })} />
            </Field>
          </div>
          <div className="mt-3 flex gap-2">
            <button className={btnCls} disabled={!f.name.trim()} onClick={() => void save()}>
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
            <th className={thCls}>{t('common.name')}</th>
            <th className={thCls}>{t('customers.externalRef')}</th>
            <th className={thCls}>{t('customers.contractCount')}</th>
            <th className={thCls}>{t('common.active')}</th>
            {isAdmin(me) && <th className={thCls}>{t('common.actions')}</th>}
          </tr>
        </thead>
        <tbody>
          {customers.map((c) => (
            <tr
              key={c.id}
              onClick={() => onSelect(c.id)}
              className={cn(
                'cursor-pointer border-b border-slate-100 hover:bg-blue-50/50',
                selectedId === c.id && 'bg-blue-50',
              )}
            >
              <td className={cn(tdCls, 'font-medium')}>{c.name}</td>
              <td className={tdCls}>{c.externalReference ?? '—'}</td>
              <td className={tdCls}>{c.contractCount}</td>
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
          {customers.length === 0 && (
            <tr><td className={tdCls} colSpan={5}>{t('common.none')}</td></tr>
          )}
        </tbody>
      </table>
    </Panel>
  );
}
