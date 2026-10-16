import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createEndClient, deleteEndClient, errorMessage, getEndClients, updateEndClient,
} from '@/lib/api';
import { countryName } from '@/lib/countries';
import type { EndClient } from '@/lib/types';
import { cn } from '@/lib/utils';
import { isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, CountrySelect, Field, inputCls, Panel, tdCls, thCls,
} from '@/components/forms';

const grpCls =
  'mb-1.5 mt-3 border-b border-slate-200 pb-0.5 text-[11px] font-semibold uppercase tracking-wide text-slate-500';

type EndClientForm = {
  name: string; country: string; addressLine: string;
  postalCode: string; city: string; notes: string;
};

const emptyForm: EndClientForm = {
  name: '', country: '', addressLine: '', postalCode: '', city: '', notes: '',
};

/** End clients — the final customer in a chain when the invoicing customer is
 * an intermediary. When a contract has no end client, the customer IS the end
 * client (handled on the contract form). */
export function EndClientSection({ onChanged }: { onChanged?: () => void }) {
  const { t, i18n } = useTranslation();
  const me = useMe();
  const [endClients, setEndClients] = useState<EndClient[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [f, setF] = useState<EndClientForm>(emptyForm);

  const reload = useCallback(
    () => getEndClients().then(setEndClients).catch(() => setEndClients([])),
    [],
  );
  useEffect(() => { void reload(); }, [reload]);

  const openNew = () => { setEditId(null); setF(emptyForm); setFormOpen(true); };
  const openEdit = (e: EndClient) => {
    setEditId(e.id);
    setF({
      name: e.name, country: e.country ?? '', addressLine: e.addressLine ?? '',
      postalCode: e.postalCode ?? '', city: e.city ?? '', notes: e.notes ?? '',
    });
    setFormOpen(true);
  };

  const save = async () => {
    const body = {
      name: f.name.trim(),
      country: f.country || null,
      addressLine: f.addressLine.trim() || null,
      postalCode: f.postalCode.trim() || null,
      city: f.city.trim() || null,
      notes: f.notes.trim() || null,
    };
    setError(null);
    try {
      if (editId) await updateEndClient(editId, body);
      else await createEndClient(body);
      setFormOpen(false);
      await reload();
      onChanged?.();
    } catch (e) { setError(errorMessage(e)); }
  };

  const remove = async (id: string) => {
    if (!window.confirm(t('common.confirmDelete'))) return;
    setError(null);
    try { await deleteEndClient(id); await reload(); onChanged?.(); }
    catch (e) { setError(errorMessage(e)); }
  };

  return (
    <Panel
      title={t('endClients.title')}
      help={t('endClients.help')}
      actions={isAdmin(me) && !formOpen && (
        <button className={btnSecondaryCls} onClick={openNew}>{t('endClients.newEndClient')}</button>
      )}
    >
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
      {formOpen && (
        <div className="mb-4 rounded border border-slate-200 bg-slate-50 p-3">
          <h4 className={grpCls}>{t('endClients.groupIdentity')}</h4>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <Field label={t('common.name')} help={t('endClients.hName')}>
              <input className={inputCls} value={f.name}
                onChange={(e) => setF({ ...f, name: e.target.value })} />
            </Field>
            <Field label={t('customers.country')} help={t('endClients.hCountry')}>
              <CountrySelect value={f.country} onChange={(c) => setF({ ...f, country: c })} />
            </Field>
          </div>
          <h4 className={grpCls}>{t('endClients.groupAddress')}</h4>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-4">
            <Field label={t('customers.addressLine')}>
              <input className={inputCls} value={f.addressLine}
                onChange={(e) => setF({ ...f, addressLine: e.target.value })} />
            </Field>
            <Field label={t('customers.postalCode')}>
              <input className={inputCls} value={f.postalCode}
                onChange={(e) => setF({ ...f, postalCode: e.target.value })} />
            </Field>
            <Field label={t('customers.city')}>
              <input className={inputCls} value={f.city}
                onChange={(e) => setF({ ...f, city: e.target.value })} />
            </Field>
            <Field label={t('common.notes')}>
              <input className={inputCls} value={f.notes}
                onChange={(e) => setF({ ...f, notes: e.target.value })} />
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
            <th className={thCls}>{t('customers.country')}</th>
            <th className={thCls}>{t('customers.city')}</th>
            {isAdmin(me) && <th className={thCls}>{t('common.actions')}</th>}
          </tr>
        </thead>
        <tbody>
          {endClients.map((e) => (
            <tr key={e.id} className="border-b border-slate-100">
              <td className={cn(tdCls, 'font-medium')}>{e.name}</td>
              <td className={tdCls}>{countryName(e.country, i18n.language)}</td>
              <td className={tdCls}>{e.city ?? '—'}</td>
              {isAdmin(me) && (
                <td className={tdCls}>
                  <span className="flex gap-1">
                    <button className={btnSecondaryCls} onClick={() => openEdit(e)}>
                      {t('common.edit')}
                    </button>
                    <button className={btnDangerCls} onClick={() => void remove(e.id)}>
                      {t('common.delete')}
                    </button>
                  </span>
                </td>
              )}
            </tr>
          ))}
          {endClients.length === 0 && (
            <tr><td className={tdCls} colSpan={4}>{t('common.none')}</td></tr>
          )}
        </tbody>
      </table>
    </Panel>
  );
}
