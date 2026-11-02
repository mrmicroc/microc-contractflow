import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createCustomer, deleteCustomer, errorCode, errorMessage, getCustomer, getCustomers, updateCustomer,
} from '@/lib/api';
import { confirmAction } from '@/lib/confirm';
import { countryName } from '@/lib/countries';
import type { ContactPerson, CustomerSummary } from '@/lib/types';
import { cn } from '@/lib/utils';
import { canSeeCommercial, isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, CountrySelect, Field, inputCls, Panel, tdCls, thCls,
} from '@/components/forms';

const grpCls =
  'mb-1.5 mt-3 border-b border-slate-200 pb-0.5 text-[11px] font-semibold uppercase tracking-wide text-slate-500';

type ContactRow = { name: string; role: string; email: string; phone: string };
type CustomerForm = {
  name: string; externalReference: string; vatNumber: string;
  addressLine: string; postalCode: string; city: string; country: string;
  contacts: ContactRow[]; notes: string; isActive: boolean;
};

const emptyForm: CustomerForm = {
  name: '', externalReference: '', vatNumber: '',
  addressLine: '', postalCode: '', city: '', country: '',
  contacts: [], notes: '', isActive: true,
};

export function CustomerSection({
  selectedId,
  onSelect,
  onChanged,
  onDeleted,
}: {
  selectedId: string | null;
  onSelect: (id: string) => void;
  /** The register changed (create/edit/delete) — party pickers reload. */
  onChanged?: () => void;
  onDeleted?: (id: string) => void;
}) {
  const { t, i18n } = useTranslation();
  const me = useMe();
  const [customers, setCustomers] = useState<CustomerSummary[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [f, setF] = useState<CustomerForm>(emptyForm);

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
    setF(emptyForm);
    setFormOpen(true);
  };

  const openEdit = async (id: string) => {
    setError(null);
    let c;
    try { c = await getCustomer(id); }
    catch (e) { setError(errorMessage(e)); return; }
    setEditId(id);
    setF({
      name: c.name,
      externalReference: c.externalReference ?? '',
      vatNumber: c.vatNumber ?? '',
      addressLine: c.addressLine ?? '',
      postalCode: c.postalCode ?? '',
      city: c.city ?? '',
      country: c.country ?? '',
      contacts: c.contacts.map((p: ContactPerson) => ({
        name: p.name, role: p.role ?? '', email: p.email ?? '', phone: p.phone ?? '',
      })),
      notes: c.notes ?? '',
      isActive: c.isActive,
    });
    setFormOpen(true);
  };

  const setContact = (i: number, patch: Partial<ContactRow>) =>
    setF({ ...f, contacts: f.contacts.map((c, j) => (j === i ? { ...c, ...patch } : c)) });

  const save = async () => {
    const body = {
      name: f.name.trim(),
      externalReference: f.externalReference.trim() || null,
      vatNumber: f.vatNumber.trim() || null,
      addressLine: f.addressLine.trim() || null,
      postalCode: f.postalCode.trim() || null,
      city: f.city.trim() || null,
      country: f.country || null,
      contacts: f.contacts
        .filter((c) => c.name.trim())
        .map((c) => ({
          name: c.name.trim(),
          role: c.role.trim() || null,
          email: c.email.trim() || null,
          phone: c.phone.trim() || null,
        })),
      notes: f.notes.trim() || null,
      isActive: f.isActive,
    };
    setError(null);
    try {
      if (editId) await updateCustomer(editId, body);
      else await createCustomer(body);
    } catch (e) {
      setError(errorMessage(e));
      return;
    }
    setFormOpen(false);
    await reload();
    onChanged?.();
  };

  const remove = async (id: string) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    setError(null);
    try {
      await deleteCustomer(id);
      onDeleted?.(id);
      await reload();
      onChanged?.();
    } catch (e) {
      // In use as contract party or end client — say which register rule bit.
      setError(errorCode(e) === 'hasDependents' ? t('errors.organisationInUse') : errorMessage(e));
    }
  };

  return (
    <Panel
      title={t('customers.title')}
      help={t('customers.help')}
      actions={isAdmin(me) && !formOpen && (
        <button className={btnSecondaryCls} onClick={openNew}>{t('customers.newCustomer')}</button>
      )}
    >
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
      {formOpen && (
        <div className="mb-4 rounded border border-slate-200 bg-slate-50 p-3">
          <h4 className={grpCls}>{t('customers.groupIdentity')}</h4>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
            <Field label={t('common.name')} help={t('customers.hName')}>
              <input className={inputCls} value={f.name}
                onChange={(e) => setF({ ...f, name: e.target.value })} />
            </Field>
            <Field label={t('customers.externalRef')} help={t('customers.hExternalRef')}>
              <input className={inputCls} value={f.externalReference}
                onChange={(e) => setF({ ...f, externalReference: e.target.value })} />
            </Field>
            <Field label={t('customers.vatNumber')} help={t('customers.hVatNumber')}>
              <input className={inputCls} value={f.vatNumber}
                onChange={(e) => setF({ ...f, vatNumber: e.target.value })} />
            </Field>
          </div>

          <h4 className={grpCls}>{t('customers.groupAddress')}</h4>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-4">
            <Field label={t('customers.addressLine')} help={t('customers.hAddress')}>
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
            <Field label={t('customers.country')} help={t('customers.hCountry')}>
              <CountrySelect value={f.country}
                onChange={(c) => setF({ ...f, country: c })} />
            </Field>
          </div>

          <h4 className={grpCls}>{t('customers.groupContacts')}</h4>
          {f.contacts.map((c, i) => (
            <div key={i} className="mb-1 grid grid-cols-2 gap-2 sm:grid-cols-5">
              <input className={inputCls} placeholder={t('common.name')} value={c.name}
                onChange={(e) => setContact(i, { name: e.target.value })} />
              <input className={inputCls} placeholder={t('customers.contactRole')} value={c.role}
                onChange={(e) => setContact(i, { role: e.target.value })} />
              <input className={inputCls} placeholder={t('common.email')} value={c.email}
                onChange={(e) => setContact(i, { email: e.target.value })} />
              <input className={inputCls} placeholder={t('common.phone')} value={c.phone}
                onChange={(e) => setContact(i, { phone: e.target.value })} />
              <button className={btnDangerCls}
                onClick={() => setF({ ...f, contacts: f.contacts.filter((_, j) => j !== i) })}>
                {t('common.delete')}
              </button>
            </div>
          ))}
          <button className={btnSecondaryCls}
            onClick={() => setF({ ...f, contacts: [...f.contacts, { name: '', role: '', email: '', phone: '' }] })}>
            {t('customers.addContact')}
          </button>
          <p className="mt-1 text-xs text-slate-500">{t('customers.hContacts')}</p>

          <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2">
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
            <th className={cn(thCls, 'w-6')}></th>
            <th className={thCls}>{t('common.name')}</th>
            <th className={thCls}>{t('customers.vatNumber')}</th>
            <th className={thCls}>{t('customers.country')}</th>
            <th className={thCls}>{t('customers.externalRef')}</th>
            <th className={thCls}>{t('customers.contractCount')}</th>
            <th className={thCls}>{t('customers.endClientContractCount')}</th>
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
              <td className={cn(tdCls, 'w-6 text-slate-400')}>
                {selectedId === c.id ? '▾' : '▸'}
              </td>
              <td className={cn(tdCls, 'font-medium')}>{c.name}</td>
              <td className={tdCls}>{c.vatNumber ?? '—'}</td>
              <td className={tdCls}>{countryName(c.country, i18n.language)}</td>
              <td className={tdCls}>{c.externalReference ?? '—'}</td>
              <td className={tdCls}>{c.contractCount}</td>
              <td className={tdCls}>{c.endClientContractCount}</td>
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
            <tr><td className={tdCls} colSpan={9}>{t('common.none')}</td></tr>
          )}
        </tbody>
      </table>
    </Panel>
  );
}
