import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createAgreement,
  createCompetency,
  createLeave,
  createResource,
  deleteAgreement,
  deleteCompetency,
  deleteLeave,
  deleteResource,
  errorMessage,
  getAgreements,
  getContracts,
  getContractWorkPackages,
  getCustomers,
  getFeedUrl,
  getResource,
  getResources,
  getUsers,
  rotateFeedUrl,
  updateCompetency,
  updateResource,
  type UserDto,
} from '@/lib/api';
import { numOrNull } from '@/lib/versioned';
import type {
  CompensationAgreement,
  Competency,
  ContractSummary,
  ContractWorkPackage,
  CustomerSummary,
  LeaveType,
  PerDiemPolicy,
  ResourceDetail,
  ResourceSummary,
  ResourceType,
} from '@/lib/types';
import { cn } from '@/lib/utils';
import { invalidNumericFields } from '@/lib/validation';
import { canSeeAgreements, canSeeCommercial, isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, NumInput, Panel, SearchSelect,
  tdCls, thCls,
} from '@/components/forms';

export function Resources() {
  const { t } = useTranslation();
  const me = useMe();
  const [resources, setResources] = useState<ResourceSummary[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<ResourceDetail | null>(null);
  const [users, setUsers] = useState<UserDto[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [editId, setEditId] = useState<string | null>(null);
  const [f, setF] = useState({
    displayName: '', type: 'subcontractor' as ResourceType, email: '', phone: '',
    userId: '', isActive: true,
  });
  const [lf, setLf] = useState({ from: '', to: '', type: 'vacation' as LeaveType, notes: '' });
  const [leaveFormOpen, setLeaveFormOpen] = useState(false);
  const [cf, setCf] = useState({ label: '', expiryDate: '', reminderDays: '' });
  const [compFormOpen, setCompFormOpen] = useState(false);
  const [compEditId, setCompEditId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(
    () => getResources().then(setResources).catch(() => setResources([])),
    [],
  );
  useEffect(() => { void reload(); }, [reload]);
  useEffect(() => { void getUsers().then(setUsers).catch(() => setUsers([])); }, []);

  const reloadDetail = useCallback(() => {
    if (!selectedId) return;
    getResource(selectedId).then(setDetail).catch(() => setDetail(null));
  }, [selectedId]);
  useEffect(() => { void reloadDetail(); }, [reloadDetail]);

  const selectResource = (id: string) => {
    if (id === selectedId) {
      setSelectedId(null);
      setDetail(null);
      return;
    }
    setDetail(null);
    setSelectedId(id);
    setFormOpen(false);
    setLeaveFormOpen(false);
  };

  if (me && !canSeeCommercial(me)) {
    return (
      <div className="mx-auto max-w-6xl px-4 py-8">
        <p className="text-sm text-slate-500">{t('common.noAccess')}</p>
      </div>
    );
  }

  const openNew = () => {
    setEditId(null);
    setF({
      displayName: '', type: 'subcontractor', email: '', phone: '',
      userId: '', isActive: true,
    });
    setFormOpen(true);
  };

  const openEdit = (r: ResourceDetail) => {
    setEditId(r.id);
    setF({
      displayName: r.displayName, type: r.type, email: r.email ?? '', phone: r.phone ?? '',
      userId: r.userId ?? '', isActive: r.isActive,
    });
    setFormOpen(true);
  };

  const save = async () => {
    const body = {
      displayName: f.displayName.trim(),
      type: f.type,
      email: f.email.trim() || null,
      phone: f.phone.trim() || null,
      userId: f.userId || null,
      isActive: f.isActive,
    };
    if (editId) await updateResource(editId, body);
    else await createResource(body);
    setFormOpen(false);
    await reload();
    reloadDetail();
  };

  const removeResource = async (id: string) => {
    if (!window.confirm(t('common.confirmDelete'))) return;
    setError(null);
    try {
      await deleteResource(id);
      if (id === selectedId) { setSelectedId(null); setDetail(null); }
      await reload();
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  const saveLeave = async () => {
    if (!selectedId) return;
    await createLeave(selectedId, {
      from: lf.from, to: lf.to, type: lf.type, notes: lf.notes.trim() || null,
    });
    setLf({ from: '', to: '', type: 'vacation', notes: '' });
    setLeaveFormOpen(false);
    reloadDetail();
  };

  const saveCompetency = async () => {
    if (!selectedId) return;
    const bad = cf.reminderDays.trim() && Number.isNaN(Number(cf.reminderDays))
      ? [t('resources.competencyReminder')] : [];
    if (bad.length) { setError(t('common.invalidNumbers', { fields: bad.join(', ') })); return; }
    const body = {
      label: cf.label.trim(),
      expiryDate: cf.expiryDate || null,
      reminderDays: cf.reminderDays.trim() ? Number(cf.reminderDays) : null,
    };
    if (compEditId) await updateCompetency(selectedId, compEditId, body);
    else await createCompetency(selectedId, body);
    setCf({ label: '', expiryDate: '', reminderDays: '' });
    setCompEditId(null);
    setCompFormOpen(false);
    reloadDetail();
    void reload();
  };

  const openCompEdit = (c: Competency) => {
    setCompEditId(c.id);
    setCf({
      label: c.label,
      expiryDate: c.expiryDate ?? '',
      reminderDays: c.reminderDays?.toString() ?? '',
    });
    setCompFormOpen(true);
  };

  const statusChip = (status: Competency['status']) => {
    const cls = {
      expired: 'bg-red-100 text-red-700',
      due: 'bg-amber-100 text-amber-700',
      valid: 'bg-green-100 text-green-700',
      unbounded: 'bg-slate-100 text-slate-500',
    }[status];
    return (
      <span className={cn('rounded px-1.5 py-0.5 text-[10px] font-medium', cls)}>
        {t(`resources.compStatus.${status}`)}
      </span>
    );
  };

  const typeLabel = (type: ResourceType) =>
    type === 'employee' ? t('resources.typeEmployee')
    : type === 'subcontractor' ? t('resources.typeSubcontractor')
    : t('resources.typeDirectlyContracted');

  const leaveLabel = (type: LeaveType) =>
    type === 'vacation' ? t('leave.vacation')
    : type === 'leave' ? t('leave.leave')
    : t('leave.unavailable');

  const resourceForm = formOpen && (
    <div className="mb-4 rounded border border-slate-200 bg-slate-50 p-3">
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
        <Field label={t('common.name')}>
          <input className={inputCls} value={f.displayName}
            onChange={(e) => setF({ ...f, displayName: e.target.value })} />
        </Field>
        <Field label={t('resources.type')}>
          <select className={inputCls} value={f.type}
            onChange={(e) => setF({ ...f, type: e.target.value as ResourceType })}>
            <option value="employee">{t('resources.typeEmployee')}</option>
            <option value="subcontractor">{t('resources.typeSubcontractor')}</option>
            <option value="directlyContracted">{t('resources.typeDirectlyContracted')}</option>
          </select>
        </Field>
        <Field label={t('resources.linkedUser')}>
          <select className={inputCls} value={f.userId}
            onChange={(e) => setF({ ...f, userId: e.target.value })}>
            <option value="">{t('resources.noLinkedUser')}</option>
            {users.map((u) => (
              <option key={u.id} value={u.id}>{u.displayName} · {u.role}</option>
            ))}
          </select>
        </Field>
        <Field label={t('resources.email')}>
          <input className={inputCls} value={f.email}
            onChange={(e) => setF({ ...f, email: e.target.value })} />
        </Field>
        <Field label={t('resources.phone')}>
          <input className={inputCls} value={f.phone}
            onChange={(e) => setF({ ...f, phone: e.target.value })} />
        </Field>
      </div>
      <div className="mt-3 flex gap-2">
        <button className={btnCls} disabled={!f.displayName.trim()} onClick={() => void save()}>
          {t('common.save')}
        </button>
        <button className={btnSecondaryCls} onClick={() => setFormOpen(false)}>
          {t('common.cancel')}
        </button>
      </div>
    </div>
  );

  return (
    <div className="mx-auto max-w-6xl space-y-4 px-4 py-6">
      <Panel
        title={t('resources.title')}
        help={t('resources.help')}
        actions={isAdmin(me) && !formOpen && (
          <button className={btnSecondaryCls} onClick={openNew}>{t('resources.newResource')}</button>
        )}
      >
        {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
        {resourceForm}
        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
              <th className={cn(thCls, 'w-6')}></th>
              <th className={thCls}>{t('common.name')}</th>
              <th className={thCls}>{t('resources.type')}</th>
              <th className={thCls}>{t('common.active')}</th>
            </tr>
          </thead>
          <tbody>
            {resources.map((r) => (
              <tr
                key={r.id}
                onClick={() => selectResource(r.id)}
                className={cn(
                  'cursor-pointer border-b border-slate-100 hover:bg-blue-50/50',
                  selectedId === r.id && 'bg-blue-50',
                )}
              >
                <td className={cn(tdCls, 'w-6 text-slate-400')}>
                  {selectedId === r.id ? '▾' : '▸'}
                </td>
                <td className={cn(tdCls, 'font-medium')}>
                  {r.displayName}
                  {r.competencyWarning && (
                    <span className="ml-1.5 inline-block rounded bg-amber-100 px-1.5 py-0.5 align-middle text-[10px] font-semibold text-amber-700"
                      title={t('resources.competencyWarningHint')}>
                      {t('resources.competencyWarning')}
                    </span>
                  )}
                </td>
                <td className={tdCls}>{typeLabel(r.type)}</td>
                <td className={tdCls}>{r.isActive ? t('common.active') : t('common.inactive')}</td>
              </tr>
            ))}
            {resources.length === 0 && (
              <tr><td className={tdCls} colSpan={4}>{t('common.none')}</td></tr>
            )}
          </tbody>
        </table>
      </Panel>

      {detail && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <Panel
            title={detail.displayName}
            help={t('resources.detailHelp')}
            actions={isAdmin(me) && !formOpen && (
              <span className="flex gap-1">
                <button className={btnSecondaryCls} onClick={() => openEdit(detail)}>
                  {t('common.edit')}
                </button>
                <button className={btnDangerCls} onClick={() => void removeResource(detail.id)}>
                  {t('common.delete')}
                </button>
              </span>
            )}
          >
            <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
              <dt className="text-slate-500">{t('resources.type')}</dt>
              <dd>{typeLabel(detail.type)}</dd>
              <dt className="text-slate-500">{t('resources.email')}</dt>
              <dd>{detail.email ?? '—'}</dd>
              <dt className="text-slate-500">{t('resources.phone')}</dt>
              <dd>{detail.phone ?? '—'}</dd>
              <dt className="text-slate-500">{t('resources.calendarFeed')}</dt>
              <dd><FeedUrl resourceId={detail.id} /></dd>
            </dl>

            <div className="mt-4 border-t border-slate-100 pt-3">
              <div className="mb-2 flex items-center justify-between">
                <h3 className="text-xs font-semibold text-slate-600">{t('resources.competencies')}</h3>
                {isAdmin(me) && !compFormOpen && (
                  <button className={btnSecondaryCls}
                    onClick={() => { setCompEditId(null); setCf({ label: '', expiryDate: '', reminderDays: '' }); setCompFormOpen(true); }}>
                    {t('resources.addCompetency')}
                  </button>
                )}
              </div>
              {compFormOpen && (
                <div className="mb-3 flex flex-wrap items-end gap-2">
                  <Field label={t('resources.competencyLabel')}>
                    <input className={inputCls} value={cf.label}
                      onChange={(e) => setCf({ ...cf, label: e.target.value })} />
                  </Field>
                  <Field label={t('resources.competencyExpiry')}>
                    <input type="date" className={inputCls} value={cf.expiryDate}
                      onChange={(e) => setCf({ ...cf, expiryDate: e.target.value })} />
                  </Field>
                  <Field label={t('resources.competencyReminder')}>
                    <NumInput className={inputCls} value={cf.reminderDays}
                      onChange={(v) => setCf({ ...cf, reminderDays: v })} />
                  </Field>
                  <button className={btnCls} disabled={!cf.label.trim()}
                    onClick={() => void saveCompetency()}>
                    {t('common.save')}
                  </button>
                  <button className={btnSecondaryCls}
                    onClick={() => { setCompFormOpen(false); setCompEditId(null); }}>
                    {t('common.cancel')}
                  </button>
                </div>
              )}
              <table className="w-full border-collapse">
                <tbody>
                  {detail.competencies.map((c) => (
                    <tr key={c.id} className="border-b border-slate-100">
                      <td className={tdCls}>{c.label}</td>
                      <td className={tdCls}>{c.expiryDate ?? '—'}</td>
                      <td className={tdCls}>
                        {c.reminderDays !== null
                          ? t('resources.competencyReminderShort', { days: c.reminderDays })
                          : '—'}
                      </td>
                      <td className={tdCls}>{statusChip(c.status)}</td>
                      {isAdmin(me) && (
                        <td className={tdCls}>
                          <button className={btnSecondaryCls}
                            onClick={() => openCompEdit(c)}>
                            {t('common.edit')}
                          </button>
                          <button className={btnDangerCls}
                            onClick={() => void deleteCompetency(detail.id, c.id)
                              .then(() => { reloadDetail(); void reload(); })}>
                            {t('common.delete')}
                          </button>
                        </td>
                      )}
                    </tr>
                  ))}
                  {detail.competencies.length === 0 && (
                    <tr><td className={tdCls} colSpan={5}>{t('common.none')}</td></tr>
                  )}
                </tbody>
              </table>
            </div>

            <div className="mt-4 border-t border-slate-100 pt-3">
              <div className="mb-2 flex items-center justify-between">
                <h3 className="text-xs font-semibold text-slate-600">{t('leave.title')}</h3>
                {(isAdmin(me) || me?.role === 'ProjectManager') && !leaveFormOpen && (
                  <button className={btnSecondaryCls} onClick={() => setLeaveFormOpen(true)}>
                    {t('leave.add')}
                  </button>
                )}
              </div>
              {leaveFormOpen && (
                <div className="mb-3 flex flex-wrap items-end gap-2">
                  <Field label={t('leave.from')}>
                    <input type="date" className={inputCls} value={lf.from}
                      onChange={(e) => setLf({ ...lf, from: e.target.value })} />
                  </Field>
                  <Field label={t('leave.to')}>
                    <input type="date" className={inputCls} value={lf.to}
                      onChange={(e) => setLf({ ...lf, to: e.target.value })} />
                  </Field>
                  <Field label={t('resources.type')}>
                    <select className={inputCls} value={lf.type}
                      onChange={(e) => setLf({ ...lf, type: e.target.value as LeaveType })}>
                      <option value="vacation">{t('leave.vacation')}</option>
                      <option value="leave">{t('leave.leave')}</option>
                      <option value="unavailable">{t('leave.unavailable')}</option>
                    </select>
                  </Field>
                  <button className={btnCls} disabled={!lf.from || !lf.to}
                    onClick={() => void saveLeave()}>
                    {t('common.save')}
                  </button>
                  <button className={btnSecondaryCls} onClick={() => setLeaveFormOpen(false)}>
                    {t('common.cancel')}
                  </button>
                </div>
              )}
              <table className="w-full border-collapse">
                <tbody>
                  {detail.leave.map((l) => (
                    <tr key={l.id} className="border-b border-slate-100">
                      <td className={tdCls}>{l.from} → {l.to}</td>
                      <td className={tdCls}>{leaveLabel(l.type)}</td>
                      <td className={tdCls}>{l.notes ?? ''}</td>
                      {(isAdmin(me) || me?.role === 'ProjectManager') && (
                        <td className={tdCls}>
                          <button className={btnDangerCls}
                            onClick={() => void deleteLeave(detail.id, l.id).then(reloadDetail)}>
                            {t('common.delete')}
                          </button>
                        </td>
                      )}
                    </tr>
                  ))}
                  {detail.leave.length === 0 && (
                    <tr><td className={tdCls}>{t('common.none')}</td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </Panel>

          {canSeeAgreements(me) && <AgreementsPanel resourceId={detail.id} />}
        </div>
      )}
    </div>
  );
}

/** The resource's tokened .ics subscription URL, with rotate for admins. */
function FeedUrl({ resourceId }: { resourceId: string }) {
  const { t } = useTranslation();
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    let on = true;
    getFeedUrl(resourceId).then((r) => { if (on) setUrl(r.url); }).catch(() => {});
    return () => { on = false; };
  }, [resourceId]);
  if (!url) return <span className="text-slate-400">—</span>;
  return (
    <span className="flex items-center gap-2">
      <code className="max-w-72 truncate text-xs">{window.location.origin}{url}</code>
      <button className={btnSecondaryCls}
        onClick={() => void rotateFeedUrl(resourceId).then((r) => setUrl(r.url))}>
        {t('resources.regenerateFeed')}
      </button>
    </span>
  );
}

function AgreementsPanel({ resourceId }: { resourceId: string }) {
  const { t } = useTranslation();
  const me = useMe();
  const [agreements, setAgreements] = useState<CompensationAgreement[]>([]);
  const [contracts, setContracts] = useState<ContractSummary[]>([]);
  const [customers, setCustomers] = useState<CustomerSummary[]>([]);
  // Work packages per contract — the third scope level only appears once a
  // contract is picked, and only lists that contract's packages.
  const [wpMap, setWpMap] = useState<Record<string, ContractWorkPackage[]>>({});
  const [formOpen, setFormOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [af, setAf] = useState({
    customerId: '', contractId: '', workPackageId: '', effectiveFrom: '', currency: 'SEK',
    hourlyRate: '', saturdayRate: '',
    sundayRate: '', holidayRate: '', overtimeAfterHours: '8', overtimePercent: '150',
    travelTimePercent: '', mileageRate: '', perDiem: 'none' as PerDiemPolicy,
    perDiemAmount: '', expensePolicy: '', notes: '',
  });
  const contractPackages = af.contractId ? (wpMap[af.contractId] ?? []) : [];
  const scopedContracts = af.customerId
    ? contracts.filter((c) => c.customerId === af.customerId)
    : contracts;

  const reload = useCallback(
    () => getAgreements(resourceId).then(setAgreements).catch(() => setAgreements([])),
    [resourceId],
  );
  useEffect(() => {
    void reload();
    void getContracts().then(setContracts).catch(() => setContracts([]));
    void getCustomers().then(setCustomers).catch(() => setCustomers([]));
  }, [reload]);
  useEffect(() => {
    if (!af.contractId) return;
    let live = true;
    getContractWorkPackages(af.contractId)
      .then((w) => { if (live) setWpMap((m) => ({ ...m, [af.contractId]: w })); })
      .catch(() => {});
    return () => { live = false; };
  }, [af.contractId]);

  const save = async () => {
    const bad = invalidNumericFields(
      {
        [t('agreements.hourlyRate')]: af.hourlyRate,
        [t('agreements.saturdayRate')]: af.saturdayRate,
        [t('agreements.sundayRate')]: af.sundayRate,
        [t('agreements.holidayRate')]: af.holidayRate,
        [t('agreements.overtimePercent')]: af.overtimePercent,
        [t('agreements.travelTimePercent')]: af.travelTimePercent,
        [t('agreements.mileageRate')]: af.mileageRate,
        [t('agreements.perDiemAmount')]: af.perDiemAmount,
      },
      { [t('agreements.overtimeAfterHours')]: af.overtimeAfterHours },
    );
    if (bad.length) { setError(t('common.invalidNumbers', { fields: bad.join(', ') })); return; }
    if (!af.effectiveFrom) { setError(t('common.invalidNumbers', { fields: t('common.effectiveFrom') })); return; }
    setError(null);
    await createAgreement(resourceId, {
      contractId: af.contractId || null,
      workPackageId: af.contractId ? (af.workPackageId || null) : null,
      effectiveFrom: af.effectiveFrom,
      currency: af.currency,
      hourlyRate: numOrNull(af.hourlyRate),
      saturdayRate: numOrNull(af.saturdayRate),
      sundayRate: numOrNull(af.sundayRate),
      holidayRate: numOrNull(af.holidayRate),
      overtimeAfterHours: Number(af.overtimeAfterHours) || 8,
      overtimePercent: Number(af.overtimePercent) || 150,
      travelTimePercent: numOrNull(af.travelTimePercent),
      mileageRate: numOrNull(af.mileageRate),
      perDiem: af.perDiem,
      perDiemAmount: numOrNull(af.perDiemAmount),
      expensePolicy: af.expensePolicy || null,
      notes: af.notes || null,
    });
    setFormOpen(false);
    await reload();
  };

  const remove = async (agreementId: string) => {
    if (!window.confirm(t('common.confirmDelete'))) return;
    setError(null);
    try { await deleteAgreement(resourceId, agreementId); await reload(); }
    catch (e) { setError(errorMessage(e)); }
  };

  const perDiemLabel = (p: PerDiemPolicy) =>
    p === 'statutory' ? t('agreements.perDiemStatutory')
    : p === 'custom' ? t('agreements.perDiemCustom')
    : t('agreements.perDiemNone');

  return (
    <Panel
      title={t('agreements.title')}
      actions={isAdmin(me) && !formOpen && (
        <button className={btnSecondaryCls} onClick={() => setFormOpen(true)}>
          {t('agreements.addVersion')}
        </button>
      )}
    >
      <p className="mb-2 text-[11px] text-slate-400">{t('agreements.internalOnly')}</p>
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
      {formOpen && (
        <div className="mb-4 rounded border border-slate-200 bg-slate-50 p-3">
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
            <Field label={t('agreements.customer')}>
              <SearchSelect
                value={af.customerId}
                options={customers.map((c) => ({ value: c.id, label: c.name }))}
                emptyLabel={t('agreements.allCustomers')}
                onChange={(v) =>
                  setAf({ ...af, customerId: v, contractId: '', workPackageId: '' })}
              />
            </Field>
            <Field label={t('agreements.scope')}>
              <SearchSelect
                value={af.contractId}
                options={scopedContracts.map((c) => ({ value: c.id, label: c.name }))}
                emptyLabel={t('agreements.scopeDefault')}
                onChange={(v) => setAf({ ...af, contractId: v, workPackageId: '' })}
              />
            </Field>
            {af.contractId && (
              <Field label={t('agreements.workPackage')}>
                <select className={inputCls} value={af.workPackageId}
                  onChange={(e) => setAf({ ...af, workPackageId: e.target.value })}>
                  <option value="">{t('agreements.scopeWholeContract')}</option>
                  {contractPackages.map((w) => (
                    <option key={w.id} value={w.id}>
                      {w.projectName} · {w.name}
                    </option>
                  ))}
                </select>
              </Field>
            )}
            <Field label={t('common.effectiveFrom')}>
              <input type="date" className={inputCls} value={af.effectiveFrom}
                onChange={(e) => setAf({ ...af, effectiveFrom: e.target.value })} />
            </Field>
            <Field label={t('common.currency')}>
              <input className={inputCls} value={af.currency} maxLength={3}
                onChange={(e) => setAf({ ...af, currency: e.target.value })} />
            </Field>
            <Field label={t('agreements.hourlyRate')}>
              <NumInput className={inputCls} value={af.hourlyRate}
                onChange={(v) => setAf({ ...af, hourlyRate: v })} />
            </Field>
            <Field label={t('agreements.saturdayRate')}>
              <NumInput className={inputCls} value={af.saturdayRate}
                onChange={(v) => setAf({ ...af, saturdayRate: v })} />
            </Field>
            <Field label={t('agreements.sundayRate')}>
              <NumInput className={inputCls} value={af.sundayRate}
                onChange={(v) => setAf({ ...af, sundayRate: v })} />
            </Field>
            <Field label={t('agreements.travelTimePercent')}>
              <NumInput className={inputCls} value={af.travelTimePercent}
                onChange={(v) => setAf({ ...af, travelTimePercent: v })} />
            </Field>
            <Field label={t('agreements.mileageRate')}>
              <NumInput className={inputCls} value={af.mileageRate}
                onChange={(v) => setAf({ ...af, mileageRate: v })} />
            </Field>
            <Field label={t('agreements.perDiem')}>
              <select className={inputCls} value={af.perDiem}
                onChange={(e) => setAf({ ...af, perDiem: e.target.value as PerDiemPolicy })}>
                <option value="none">{t('agreements.perDiemNone')}</option>
                <option value="statutory">{t('agreements.perDiemStatutory')}</option>
                <option value="custom">{t('agreements.perDiemCustom')}</option>
              </select>
            </Field>
            {af.perDiem === 'custom' && (
              <Field label={t('agreements.perDiemAmount')}>
                <NumInput className={inputCls} value={af.perDiemAmount}
                  onChange={(v) => setAf({ ...af, perDiemAmount: v })} />
              </Field>
            )}
          </div>
          <div className="mt-3 flex gap-2">
            <button className={btnCls} disabled={!af.effectiveFrom || !af.currency.trim()}
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
            <th className={thCls}>{t('common.version')}</th>
            <th className={thCls}>{t('agreements.scope')}</th>
            <th className={thCls}>{t('common.effectiveFrom')}</th>
            <th className={thCls}>{t('agreements.hourlyRate')}</th>
            <th className={thCls}>{t('agreements.perDiem')}</th>
            {isAdmin(me) && <th className={thCls}>{t('common.actions')}</th>}
          </tr>
        </thead>
        <tbody>
          {agreements.map((a) => (
            <tr key={a.id} className="border-b border-slate-100">
              <td className={tdCls}>v{a.version}</td>
              <td className={tdCls}>
                {a.workPackageId
                  ? `${a.contractName} › ${a.workPackageProjectName} · ${a.workPackageName}`
                  : (a.contractName ?? t('agreements.scopeDefault'))}
              </td>
              <td className={tdCls}>{a.effectiveFrom}</td>
              <td className={tdCls}>{a.hourlyRate === null ? '—' : `${a.hourlyRate} ${a.currency}`}</td>
              <td className={tdCls}>{perDiemLabel(a.perDiem)}</td>
              {isAdmin(me) && (
                <td className={tdCls}>
                  <button className={btnDangerCls} onClick={() => void remove(a.id)}>
                    {t('common.delete')}
                  </button>
                </td>
              )}
            </tr>
          ))}
          {agreements.length === 0 && (
            <tr><td className={tdCls} colSpan={6}>{t('common.none')}</td></tr>
          )}
        </tbody>
      </table>
    </Panel>
  );
}
