import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createAgreement,
  createLeave,
  createResource,
  deleteAgreement,
  deleteLeave,
  deleteResource,
  errorMessage,
  getAgreements,
  getContracts,
  getFeedUrl,
  getResource,
  getResources,
  getUsers,
  rotateFeedUrl,
  updateResource,
  type UserDto,
} from '@/lib/api';
import { numOrNull } from '@/lib/versioned';
import type {
  CompensationAgreement,
  ContractSummary,
  LeaveType,
  PerDiemPolicy,
  ResourceDetail,
  ResourceSummary,
  ResourceType,
} from '@/lib/types';
import { cn } from '@/lib/utils';
import { canSeeAgreements, canSeeCommercial, isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, Panel, tdCls, thCls,
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
    competencies: '', userId: '', isActive: true,
  });
  const [lf, setLf] = useState({ from: '', to: '', type: 'vacation' as LeaveType, notes: '' });
  const [leaveFormOpen, setLeaveFormOpen] = useState(false);
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
    if (id !== selectedId) setDetail(null);
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
      competencies: '', userId: '', isActive: true,
    });
    setFormOpen(true);
  };

  const openEdit = (r: ResourceDetail) => {
    setEditId(r.id);
    setF({
      displayName: r.displayName, type: r.type, email: r.email ?? '', phone: r.phone ?? '',
      competencies: r.competencies ?? '', userId: r.userId ?? '', isActive: r.isActive,
    });
    setFormOpen(true);
  };

  const save = async () => {
    const body = {
      displayName: f.displayName.trim(),
      type: f.type,
      email: f.email.trim() || null,
      phone: f.phone.trim() || null,
      competencies: f.competencies.trim() || null,
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
        <Field label={t('resources.competencies')}>
          <input className={inputCls} value={f.competencies}
            onChange={(e) => setF({ ...f, competencies: e.target.value })} />
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
        actions={isAdmin(me) && !formOpen && (
          <button className={btnSecondaryCls} onClick={openNew}>{t('resources.newResource')}</button>
        )}
      >
        {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
        {resourceForm}
        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
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
                <td className={cn(tdCls, 'font-medium')}>{r.displayName}</td>
                <td className={tdCls}>{typeLabel(r.type)}</td>
                <td className={tdCls}>{r.isActive ? t('common.active') : t('common.inactive')}</td>
              </tr>
            ))}
            {resources.length === 0 && (
              <tr><td className={tdCls} colSpan={3}>{t('common.none')}</td></tr>
            )}
          </tbody>
        </table>
      </Panel>

      {detail && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <Panel
            title={detail.displayName}
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
            {resourceForm}
            {!formOpen && (
              <dl className="grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
                <dt className="text-slate-500">{t('resources.type')}</dt>
                <dd>{typeLabel(detail.type)}</dd>
                <dt className="text-slate-500">{t('resources.email')}</dt>
                <dd>{detail.email ?? '—'}</dd>
                <dt className="text-slate-500">{t('resources.phone')}</dt>
                <dd>{detail.phone ?? '—'}</dd>
                <dt className="text-slate-500">{t('resources.competencies')}</dt>
                <dd>{detail.competencies ?? '—'}</dd>
                <dt className="text-slate-500">{t('resources.calendarFeed')}</dt>
                <dd><FeedUrl resourceId={detail.id} /></dd>
              </dl>
            )}

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
      <code className="max-w-72 truncate text-xs">{location.origin}{url}</code>
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
  const [formOpen, setFormOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [af, setAf] = useState({
    contractId: '', effectiveFrom: '', currency: 'SEK', hourlyRate: '', saturdayRate: '',
    sundayRate: '', holidayRate: '', overtimeAfterHours: '8', overtimePercent: '150',
    travelTimePercent: '', mileageRate: '', perDiem: 'none' as PerDiemPolicy,
    perDiemAmount: '', expensePolicy: '', notes: '',
  });

  const reload = useCallback(
    () => getAgreements(resourceId).then(setAgreements).catch(() => setAgreements([])),
    [resourceId],
  );
  useEffect(() => {
    void reload();
    void getContracts().then(setContracts).catch(() => setContracts([]));
  }, [reload]);

  const save = async () => {
    await createAgreement(resourceId, {
      contractId: af.contractId || null,
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
            <Field label={t('agreements.scope')}>
              <select className={inputCls} value={af.contractId}
                onChange={(e) => setAf({ ...af, contractId: e.target.value })}>
                <option value="">{t('agreements.scopeDefault')}</option>
                {contracts.map((c) => (
                  <option key={c.id} value={c.id}>{c.name}</option>
                ))}
              </select>
            </Field>
            <Field label={t('common.effectiveFrom')}>
              <input type="date" className={inputCls} value={af.effectiveFrom}
                onChange={(e) => setAf({ ...af, effectiveFrom: e.target.value })} />
            </Field>
            <Field label={t('common.currency')}>
              <input className={inputCls} value={af.currency} maxLength={3}
                onChange={(e) => setAf({ ...af, currency: e.target.value })} />
            </Field>
            <Field label={t('agreements.hourlyRate')}>
              <input className={inputCls} inputMode="decimal" value={af.hourlyRate}
                onChange={(e) => setAf({ ...af, hourlyRate: e.target.value })} />
            </Field>
            <Field label={t('agreements.saturdayRate')}>
              <input className={inputCls} inputMode="decimal" value={af.saturdayRate}
                onChange={(e) => setAf({ ...af, saturdayRate: e.target.value })} />
            </Field>
            <Field label={t('agreements.sundayRate')}>
              <input className={inputCls} inputMode="decimal" value={af.sundayRate}
                onChange={(e) => setAf({ ...af, sundayRate: e.target.value })} />
            </Field>
            <Field label={t('agreements.travelTimePercent')}>
              <input className={inputCls} inputMode="decimal" value={af.travelTimePercent}
                onChange={(e) => setAf({ ...af, travelTimePercent: e.target.value })} />
            </Field>
            <Field label={t('agreements.mileageRate')}>
              <input className={inputCls} inputMode="decimal" value={af.mileageRate}
                onChange={(e) => setAf({ ...af, mileageRate: e.target.value })} />
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
                <input className={inputCls} inputMode="decimal" value={af.perDiemAmount}
                  onChange={(e) => setAf({ ...af, perDiemAmount: e.target.value })} />
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
              <td className={tdCls}>{a.contractName ?? t('agreements.scopeDefault')}</td>
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
