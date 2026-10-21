import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  bookingAction,
  completeBooking,
  createBooking,
  createPadRequest,
  deleteBooking,
  describeError,
  errorCode,
  getBookings,
  getContract,
  getProjects,
  getProjectWorkPackages,
  getResources,
  rescheduleBooking,
  updateBooking,
  type BookingActionResult,
} from '@/lib/api';
import { errorBody } from '@/lib/errors';
import {
  additionalPadShortfall, overrunParts,
  addDays, availableActions, bookingTooltip, bookingUpdateBody, monthGrid, padEffectKey,
  rescheduleOutcomeKey, statusClasses, viewRange, weekDates,
  type BookingAction, type CalendarView,
} from '@/lib/bookings';
import { confirmAction } from '@/lib/confirm';
import {
  actualHoursBody, perProjectLine, shareProjectOptions, sharesBody, shortfallsOf, type ShareRow,
} from '@/lib/bookingShares';
import { representativeNames } from '@/lib/contractExtras';
import { hhmm } from '@/lib/terms';
import { localMonth, localTodayIso as todayIso, monthKey, shiftMonth } from '@/lib/dates';
import { numOrNull } from '@/lib/versioned';
import { invalidNumericFields } from '@/lib/validation';
import type { Booking, BookingRequestBody, ProjectSummary, ResourceSummary, WorkPackage } from '@/lib/types';
import { cn } from '@/lib/utils';
import { canManage as canManageRole, canSeeCommercial, useDevUserReload, useMe } from '@/lib/useMe';
import { useFlash } from '@/lib/useFlash';
import {
  btnCls, btnSecondaryCls, ErrorBox, Field, inputCls, NumInput, OkBox, Panel, tdCls, thCls,
} from '@/components/forms';
import { Attachments } from '@/components/reporting/Attachments';

type LaneBy = 'resource' | 'project';
type PreStatus = BookingRequestBody['status'];

const WEEKDAYS = ['mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun'] as const;
const emptyForm = {
  projectId: '', resourceId: '', workPackageId: '', date: '', plannedHours: '8',
  activityRef: '', workDescription: '', customerContact: '', evidenceRef: '',
  status: 'forecast' as PreStatus,
  instructedBy: '', startTime: '', shares: [] as ShareRow[],
};

export function Plan() {
  const { t } = useTranslation();
  const me = useMe();
  const [view, setView] = useState<CalendarView>('month');
  const [laneBy, setLaneBy] = useState<LaneBy>('resource');
  const [anchor, setAnchor] = useState(todayIso());
  const [ym, setYm] = useState(() => localMonth());
  const [bookings, setBookings] = useState<Booking[]>([]);
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [resources, setResources] = useState<ResourceSummary[]>([]);
  const [projectFilter, setProjectFilter] = useState('');
  const [resourceFilter, setResourceFilter] = useState('');
  const [selected, setSelected] = useState<Booking | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [reschedId, setReschedId] = useState<string | null>(null);
  const [reschedDate, setReschedDate] = useState('');
  const [completeId, setCompleteId] = useState<string | null>(null);
  const [actualHours, setActualHours] = useState('');
  const [actualByProject, setActualByProject] = useState<Record<string, string>>({});
  const [padNeed, setPadNeed] = useState<{
    bookingId: string; projectId: string; quantity: string; reason: string; representative: string;
  } | null>(null);
  const [repsByContract, setRepsByContract] = useState<Record<string, string[]>>({});
  const [editing, setEditing] = useState(false);
  const [ef, setEf] = useState({
    date: '', plannedHours: '', workDescription: '', activityRef: '',
    customerContact: '', evidenceRef: '', status: 'requested' as PreStatus,
    instructedBy: '', startTime: '',
  });
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useFlash(8000);
  const [f, setF] = useState(emptyForm);
  const [pkgMap, setPkgMap] = useState<Record<string, WorkPackage[]>>({});
  const pkgs = f.projectId ? (pkgMap[f.projectId] ?? []) : [];

  const week = weekDates(anchor);
  const warningLabel = (w: string) => t(`booking.warning.${w}`);

  // Work packages of the booking form's project — the picker only appears
  // when the project has packages to target.
  useEffect(() => {
    if (!f.projectId) return;
    let live = true;
    getProjectWorkPackages(f.projectId)
      .then((p) => { if (live) setPkgMap((m) => ({ ...m, [f.projectId]: p })); })
      .catch(() => {});
    return () => { live = false; };
  }, [f.projectId]);

  // Authorised representatives of the form's / selected booking's contract
  // feed the "ordered by" picker (free text stays possible).
  const repsContractId = projects.find((p) => p.id === (f.projectId || selected?.projectId))?.contractId;
  useEffect(() => {
    if (!repsContractId || repsByContract[repsContractId]) return;
    let live = true;
    getContract(repsContractId)
      .then((c) => { if (live) setRepsByContract((m) => ({ ...m, [repsContractId]: representativeNames(c.representatives) })); })
      .catch(() => {});
    return () => { live = false; };
  }, [repsContractId, repsByContract]);
  const reps = repsContractId ? repsByContract[repsContractId] ?? [] : [];
  const projectNames = Object.fromEntries(projects.map((p) => [p.id, p.name]));

  const reload = useCallback(() => {
    getBookings({
      ...viewRange(view, ym, anchor),
      projectId: projectFilter || undefined,
      resourceId: resourceFilter || undefined,
    }).then(setBookings).catch((e) => { setBookings([]); setError(describeError(e, t)); });
  }, [view, ym, anchor, projectFilter, resourceFilter, t]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  const loadLists = useCallback(() => {
    getProjects().then(setProjects).catch(() => setProjects([]));
    getResources().then(setResources).catch(() => setResources([]));
  }, []);
  useEffect(() => { void Promise.resolve().then(loadLists); }, [loadLists]);
  useDevUserReload(useCallback(() => { setSelected(null); loadLists(); reload(); }, [loadLists, reload]));

  const canManage = canManageRole(me);
  const userResourceIds = new Set(resources.filter((r) => r.userId === me?.id).map((r) => r.id));

  const shift = (d: number) => {
    if (view === 'month') setYm(({ year, month }) => shiftMonth(year, month, d));
    else if (view === 'week') setAnchor((a) => addDays(a, d * 7));
    else setAnchor((a) => addDays(a, d));
  };

  const periodLabel = view === 'month' ? monthKey(ym.year, ym.month)
    : view === 'week' ? `${week[0]} – ${week[6]}`
    : anchor;

  const lanes: { id: string; name: string }[] = laneBy === 'resource'
    ? resources
        .filter((r) => r.isActive && (!resourceFilter || r.id === resourceFilter))
        .map((r) => ({ id: r.id, name: r.displayName }))
    : projects
        .filter((p) => p.isActive && (!projectFilter || p.id === projectFilter))
        .map((p) => ({ id: p.id, name: p.name }));

  const byDate = new Map<string, Booking[]>();
  for (const b of bookings) {
    const list = byDate.get(b.date) ?? [];
    list.push(b);
    byDate.set(b.date, list);
  }
  const laneDays = view === 'week' ? week : [anchor];
  const bookingsFor = (laneId: string, day: string) =>
    (byDate.get(day) ?? []).filter((b) =>
      (laneBy === 'resource' ? b.resourceId : b.projectId) === laneId);
  const bookingById = (id: string | null) => (id ? bookings.find((b) => b.id === id) ?? null : null);

  const runAction = async (b: Booking, action: BookingAction) => {
    setError(null);
    if (action === 'reschedule') { setReschedId(b.id); return; }
    if (action === 'edit') {
      setEf({
        date: b.date,
        plannedHours: String(b.plannedHours),
        workDescription: b.workDescription ?? '',
        activityRef: b.activityRef ?? '',
        customerContact: b.customerContact ?? '',
        evidenceRef: b.evidenceRef ?? '',
        status: (['forecast', 'tentative', 'requested'] as const).includes(b.status as PreStatus)
          ? b.status as PreStatus : 'requested',
        instructedBy: b.instructedBy ?? '',
        startTime: hhmm(b.startTime),
      });
      setEditing(true);
      return;
    }
    try {
      if (action === 'delete') {
        if (!confirmAction(t('common.confirmDelete'))) return;
        await deleteBooking(b.id);
      } else if (action === 'cancel') {
        if (!confirmAction(t('booking.confirmCancel'))) return;
        const res = await bookingAction(b.id, 'cancel') as BookingActionResult;
        setNotice(t('booking.cancelled', {
          effect: t(padEffectKey(res.padEffect)), amount: res.padAmount ?? 0,
        }));
      } else if (action === 'complete') {
        // Optional actual hours first — the panel below confirms and submits.
        setCompleteId(b.id);
        setActualHours('');
        setActualByProject({});
        return;
      } else {
        await bookingAction(b.id, action);
      }
      setSelected(null);
      reload();
    } catch (e) {
      // A refused confirm can wait on Additional PAD (§4.3) — offer to
      // prepare the request for exactly this booking.
      const body = errorBody(e);
      if (action === 'confirm' && errorCode(e) === 'insufficientPad') {
        // Shared day: the project short of PAD may not be the primary one.
        const shortfalls = shortfallsOf(body);
        const first = shortfalls[0];
        const short = first
          ? first.shortfallPad
          : additionalPadShortfall(Number(body?.neededPad ?? 1), Number(body?.freePad ?? 0));
        setPadNeed({
          bookingId: b.id, projectId: first?.projectId ?? b.projectId,
          quantity: String(short || 1), reason: '', representative: '',
        });
        setError(shortfalls.length > 1
          ? `${describeError(e, t)} ${t('booking.shortfalls', {
              list: perProjectLine(shortfalls.map((x) => ({ projectId: x.projectId, amount: x.shortfallPad })), projectNames),
            })}`
          : describeError(e, t));
        return;
      }
      setError(describeError(e, t));
    }
  };

  const doComplete = async () => {
    if (!completeId) return;
    const bad = invalidNumericFields({ [t('booking.actualHours')]: actualHours });
    const byProject = actualHoursBody(actualByProject);
    if (byProject === null) bad.push(t('booking.actualByProject'));
    if (bad.length) { setError(t('common.invalidNumbers', { fields: bad.join(', ') })); return; }
    if (!confirmAction(t('booking.confirmComplete'))) return;
    setError(null);
    try {
      const res = await completeBooking(completeId, numOrNull(actualHours), byProject);
      const perProject = (res.perProject ?? []).length > 1
        ? ` ${t('booking.perProjectResult', {
            list: perProjectLine((res.perProject ?? []).map((p) => ({ projectId: p.projectId, amount: p.padUtilised })), projectNames),
          })}`
        : '';
      const parts = overrunParts(res.overrun);
      setNotice((parts.length
        ? t('booking.completedWithOverrun', {
            parts: parts.map((p) => `${t(`booking.overrun.${p.source}`)} ${p.amount}`).join(', '),
          }) + (res.overrun?.nextMonthConflict ? ` ${t('booking.overrun.nextMonthConflict')}` : '')
        : t('booking.completed', { pad: res.padUtilised })) + perProject);
      setCompleteId(null);
      setSelected(null);
      reload();
    } catch (e) {
      setError(describeError(e, t));
    }
  };

  const requestAdditionalPad = async () => {
    if (!padNeed) return;
    const b = bookingById(padNeed.bookingId);
    const quantity = numOrNull(padNeed.quantity);
    if (!b || quantity === null || quantity <= 0) {
      setError(t('common.invalidNumbers', { fields: t('monthEnd.quantity') }));
      return;
    }
    setError(null);
    try {
      const [year, month] = b.date.split('-').map(Number);
      await createPadRequest({
        projectId: padNeed.projectId, year, month, quantity,
        reason: padNeed.reason.trim() || null, bookingId: b.id,
        reference: padNeed.representative.trim() || null,
      });
      setNotice(t('booking.padRequestCreated'));
      setPadNeed(null);
    } catch (e) {
      setError(describeError(e, t));
    }
  };

  const saveEdit = async () => {
    if (!selected) return;
    const bad = invalidNumericFields({ [t('booking.plannedHours')]: ef.plannedHours });
    if (bad.length) { setError(t('common.invalidNumbers', { fields: bad.join(', ') })); return; }
    setError(null);
    try {
      await updateBooking(selected.id, bookingUpdateBody(selected, {
        ...ef, plannedHours: numOrNull(ef.plannedHours),
      }));
      setEditing(false);
      setSelected(null);
      reload();
    } catch (e) {
      setError(describeError(e, t));
    }
  };

  const doReschedule = async () => {
    if (!reschedId || !reschedDate) return;
    setError(null);
    try {
      const res = await rescheduleBooking(reschedId, reschedDate);
      setNotice(`${t('booking.rescheduledNotice', {
        date: res.replacement.date, effect: t(padEffectKey(res.padEffect)),
      })} ${t(rescheduleOutcomeKey(res))}`);
      setReschedId(null);
      setReschedDate('');
      setSelected(null);
      reload();
    } catch (e) {
      setError(describeError(e, t));
    }
  };

  const save = async () => {
    const bad = invalidNumericFields({ [t('booking.plannedHours')]: f.plannedHours });
    const split = sharesBody(f.projectId, f.plannedHours || '8', f.shares);
    if (!split) bad.push(t('booking.shares'));
    if (bad.length || !split) { setError(t('common.invalidNumbers', { fields: bad.join(', ') })); return; }
    setError(null);
    try {
      const created = await createBooking({
        projectId: f.projectId,
        resourceId: f.resourceId,
        workPackageId: f.workPackageId || null,
        date: f.date,
        plannedHours: split.plannedHours,
        shares: split.shares,
        startTime: f.startTime ? `${f.startTime}:00` : null,
        instructedBy: f.instructedBy.trim() || null,
        instructedAtUtc: f.instructedBy.trim() ? new Date().toISOString() : null,
        activityRef: f.activityRef.trim() || null,
        workDescription: f.workDescription.trim() || null,
        customerContact: f.customerContact.trim() || null,
        evidenceRef: f.evidenceRef.trim() || null,
        status: f.status,
      });
      // Conflict warnings come back on create — keep them in front of the
      // planner instead of discarding them.
      if (created.warnings.length)
        setNotice(t('booking.createdWithWarnings', {
          warnings: created.warnings.map(warningLabel).join(', '),
        }));
      setFormOpen(false);
      setF(emptyForm);
      reload();
    } catch (e) {
      setError(describeError(e, t));
    }
  };

  if (me && !canSeeCommercial(me) && me.role !== 'Resource') {
    return <div className="mx-auto max-w-6xl px-4 py-8"><p className="text-sm text-slate-500">{t('common.noAccess')}</p></div>;
  }
  if (me?.role === 'Resource') {
    return <div className="mx-auto max-w-6xl px-4 py-8"><p className="text-sm text-slate-500">{t('plan.resourceHint')}</p></div>;
  }

  const selActions = selected
    ? availableActions(selected.status, canManage, userResourceIds.has(selected.resourceId))
    : [];
  const replaces = bookingById(selected?.replacesBookingId ?? null);
  const replacedBy = bookingById(selected?.rescheduledToBookingId ?? null);

  const chip = (b: Booking, label: string, extra = '') => (
    <button
      key={b.id}
      onClick={() => { setSelected(b); setEditing(false); setReschedId(null); }}
      className={cn(
        'block w-full truncate rounded px-1 py-0.5 text-left text-[11px] font-medium',
        extra, statusClasses(b.status),
        b.warnings.length > 0 && 'ring-1 ring-amber-400',
      )}
      title={bookingTooltip(b, warningLabel)}
    >
      {label}
      {b.warnings.length > 0 && ' ⚠'}
    </button>
  );

  return (
    <div className="mx-auto max-w-6xl space-y-4 px-4 py-6">
      <Panel
        title={t('plan.title')}
        actions={
          <div className="flex items-center gap-2">
            <select className={inputCls} value={projectFilter}
              onChange={(e) => setProjectFilter(e.target.value)}>
              <option value="">{t('plan.allProjects')}</option>
              {projects.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
            <select className={inputCls} value={resourceFilter}
              onChange={(e) => setResourceFilter(e.target.value)}>
              <option value="">{t('plan.allResources')}</option>
              {resources.map((r) => <option key={r.id} value={r.id}>{r.displayName}</option>)}
            </select>
            <span className="flex overflow-hidden rounded border border-slate-300 text-sm">
              {(['month', 'week', 'day'] as const).map((v) => (
                <button key={v}
                  className={cn('px-2 py-1', view === v ? 'bg-blue-600 text-white' : 'bg-white text-slate-600 hover:bg-slate-50')}
                  onClick={() => setView(v)}>
                  {t(`plan.view.${v}`)}
                </button>
              ))}
            </span>
            {view !== 'month' && (
              <span className="flex overflow-hidden rounded border border-slate-300 text-sm">
                {(['resource', 'project'] as const).map((l) => (
                  <button key={l}
                    className={cn('px-2 py-1', laneBy === l ? 'bg-slate-700 text-white' : 'bg-white text-slate-600 hover:bg-slate-50')}
                    onClick={() => setLaneBy(l)}>
                    {t(`plan.lanes.${l}`)}
                  </button>
                ))}
              </span>
            )}
            <button className={btnSecondaryCls} onClick={() => shift(-1)} aria-label={t('plan.previous')}>←</button>
            <span className="min-w-24 text-center text-sm font-medium">{periodLabel}</span>
            <button className={btnSecondaryCls} onClick={() => shift(1)} aria-label={t('plan.next')}>→</button>
            {canManage && (
              <button className={btnCls} onClick={() => setFormOpen((v) => !v)}>
                {t('plan.newBooking')}
              </button>
            )}
          </div>
        }
      >
        <ErrorBox message={error} />
        <OkBox message={notice} />

        {formOpen && (
          <div className="mb-4 rounded border border-slate-200 bg-slate-50 p-3">
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              <Field label={t('booking.project')}>
                <select className={inputCls} value={f.projectId}
                  onChange={(e) => setF({ ...f, projectId: e.target.value, workPackageId: '' })}>
                  <option value="">—</option>
                  {projects.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
              </Field>
              {pkgs.length > 0 && (
                <Field label={t('booking.workPackage')}>
                  <select className={inputCls} value={f.workPackageId}
                    onChange={(e) => setF({ ...f, workPackageId: e.target.value })}>
                    <option value="">—</option>
                    {pkgs.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                  </select>
                </Field>
              )}
              <Field label={t('booking.resource')}>
                <select className={inputCls} value={f.resourceId}
                  onChange={(e) => setF({ ...f, resourceId: e.target.value })}>
                  <option value="">—</option>
                  {resources.filter((r) => r.isActive).map((r) =>
                    <option key={r.id} value={r.id}>{r.displayName}</option>)}
                </select>
              </Field>
              <Field label={t('booking.date')}>
                <input type="date" className={inputCls} value={f.date}
                  onChange={(e) => setF({ ...f, date: e.target.value })} />
              </Field>
              <Field label={t('booking.plannedHours')}>
                <NumInput className={inputCls} value={f.plannedHours}
                  onChange={(v) => setF({ ...f, plannedHours: v })} />
              </Field>
              <Field label={t('booking.activityRef')}>
                <input className={inputCls} value={f.activityRef}
                  onChange={(e) => setF({ ...f, activityRef: e.target.value })} />
              </Field>
              <Field label={t('booking.workDescription')}>
                <input className={inputCls} value={f.workDescription}
                  onChange={(e) => setF({ ...f, workDescription: e.target.value })} />
              </Field>
              <Field label={t('booking.customerContact')}>
                <input className={inputCls} value={f.customerContact}
                  onChange={(e) => setF({ ...f, customerContact: e.target.value })} />
              </Field>
              <Field label={t('booking.evidenceRef')}>
                <input className={inputCls} value={f.evidenceRef}
                  onChange={(e) => setF({ ...f, evidenceRef: e.target.value })} />
              </Field>
              <Field label={t('booking.statusLabel')}>
                <select className={inputCls} value={f.status}
                  onChange={(e) => setF({ ...f, status: e.target.value as PreStatus })}>
                  <option value="forecast">{t('booking.status.forecast')}</option>
                  <option value="tentative">{t('booking.status.tentative')}</option>
                  <option value="requested">{t('booking.status.requested')}</option>
                </select>
              </Field>
              <Field label={t('booking.startTime')} help={t('booking.hStartTime')}>
                <input type="time" className={inputCls} value={f.startTime}
                  onChange={(e) => setF({ ...f, startTime: e.target.value })} />
              </Field>
              <Field label={t('booking.instructedBy')} help={t('booking.hInstructedBy')}>
                <input className={inputCls} list="booking-reps" value={f.instructedBy}
                  onChange={(e) => setF({ ...f, instructedBy: e.target.value })} />
              </Field>
            </div>
            <datalist id="booking-reps">
              {reps.map((r) => <option key={r} value={r} />)}
            </datalist>
            {f.projectId && shareProjectOptions(projects, f.projectId).length > 0 && (
              <div className="mt-2">
                <p className="mb-1 flex items-center gap-1.5 text-xs font-medium text-slate-600">
                  {t('booking.shares')}
                  <span className="cursor-help rounded-full border border-slate-300 px-1 text-[10px] text-slate-500"
                    title={t('booking.hShares')}>?</span>
                </p>
                {f.shares.map((sh, i) => (
                  <div key={i} className="mb-1 flex items-center gap-2">
                    <select className={cn(inputCls, 'w-56')} value={sh.projectId}
                      onChange={(e) => setF({ ...f, shares: f.shares.map((x, j) => (j === i ? { ...x, projectId: e.target.value } : x)) })}>
                      <option value="">—</option>
                      {shareProjectOptions(projects, f.projectId).map((p) =>
                        <option key={p.id} value={p.id}>{p.name}</option>)}
                    </select>
                    <NumInput className={cn(inputCls, 'w-28')} placeholder={t('booking.plannedHours')}
                      value={sh.hours}
                      onChange={(v) => setF({ ...f, shares: f.shares.map((x, j) => (j === i ? { ...x, hours: v } : x)) })} />
                    <button className={btnSecondaryCls}
                      onClick={() => setF({ ...f, shares: f.shares.filter((_, j) => j !== i) })}>
                      {t('common.delete')}
                    </button>
                  </div>
                ))}
                <button className={btnSecondaryCls}
                  onClick={() => setF({ ...f, shares: [...f.shares, { projectId: '', hours: '' }] })}>
                  {t('booking.addShare')}
                </button>
                {f.shares.length > 0 && (
                  <span className="ml-2 text-xs text-slate-500">{t('booking.sharesPrimaryHint')}</span>
                )}
              </div>
            )}
            <div className="mt-3 flex gap-2">
              <button className={btnCls}
                disabled={!f.projectId || !f.resourceId || !f.date}
                onClick={() => void save()}>
                {t('common.save')}
              </button>
              <button className={btnSecondaryCls} onClick={() => setFormOpen(false)}>
                {t('common.cancel')}
              </button>
            </div>
          </div>
        )}

        {view === 'month' ? (
          <div className="grid grid-cols-7 gap-px overflow-hidden rounded border border-slate-200 bg-slate-200">
            {WEEKDAYS.map((d) => (
              <div key={d} className="bg-slate-50 px-2 py-1 text-center text-xs font-semibold text-slate-500">
                {t(`plan.weekday.${d}`)}
              </div>
            ))}
            {monthGrid(ym.year, ym.month).flat().map((day, i) => (
              <div key={i} className={cn('min-h-20 bg-white p-1', !day && 'bg-slate-50')}>
                {day && (
                  <>
                    <div className={cn('px-1 text-xs font-medium',
                      day === todayIso() ? 'text-blue-600' : 'text-slate-500')}>
                      {Number(day.slice(8))}
                    </div>
                    <div className="mt-0.5 space-y-0.5">
                      {(byDate.get(day) ?? []).map((b) => chip(b, b.resourceName))}
                    </div>
                  </>
                )}
              </div>
            ))}
          </div>
        ) : (
          <table className="w-full border-collapse">
            <thead>
              <tr className="border-b border-slate-200">
                <th className={cn(thCls, 'w-40 text-left')}>
                  {laneBy === 'resource' ? t('booking.resource') : t('booking.project')}
                </th>
                {laneDays.map((d, i) => (
                  <th key={d} className={cn(thCls, 'text-center',
                    d === todayIso() && 'text-blue-600')}>
                    {view === 'week' && `${t(`plan.weekday.${WEEKDAYS[i]}`)} `}
                    {Number(d.slice(8))}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {lanes.map((lane) => (
                <tr key={lane.id} className="border-b border-slate-100 align-top">
                  <td className={cn(tdCls, 'font-medium')}>{lane.name}</td>
                  {laneDays.map((d) => (
                    <td key={d} className={cn(tdCls, 'min-w-24')}>
                      {bookingsFor(lane.id, d).map((b) =>
                        chip(b, laneBy === 'resource' ? b.projectName : b.resourceName, 'mb-0.5'))}
                    </td>
                  ))}
                </tr>
              ))}
              {lanes.length === 0 && (
                <tr><td className={tdCls} colSpan={8}>{t('common.none')}</td></tr>
              )}
            </tbody>
          </table>
        )}
      </Panel>

      {selected && (
        <Panel title={`${selected.resourceName} · ${selected.date}`}
          help={t('plan.detailHelp')}
          actions={<button className={btnSecondaryCls} onClick={() => setSelected(null)} aria-label={t('common.close')}>✕</button>}>
          <div className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
            <div><span className="text-slate-500">{t('booking.project')}:</span> {selected.projectName}</div>
            <div><span className="text-slate-500">{t('booking.customer')}:</span> {selected.customerName}</div>
            <div><span className="text-slate-500">{t('booking.statusLabel')}:</span> {t(`booking.status.${selected.status}`)}</div>
            <div><span className="text-slate-500">{t('booking.plannedHours')}:</span> {t('common.hoursValue', { value: selected.plannedHours })}</div>
            <div><span className="text-slate-500">{t('booking.expectedPad')}:</span> {selected.expectedPad ?? '—'}</div>
            <div><span className="text-slate-500">{t('booking.workPackage')}:</span> {selected.workPackageName ?? '—'}</div>
            <div><span className="text-slate-500">{t('booking.activityRef')}:</span> {selected.activityRef ?? '—'}</div>
            <div><span className="text-slate-500">{t('booking.workDescription')}:</span> {selected.workDescription ?? '—'}</div>
            <div><span className="text-slate-500">{t('booking.customerContact')}:</span> {selected.customerContact ?? '—'}</div>
            <div><span className="text-slate-500">{t('booking.evidenceRef')}:</span> {selected.evidenceRef ?? '—'}</div>
            <div><span className="text-slate-500">{t('booking.instructedBy')}:</span> {selected.instructedBy
              ? `${selected.instructedBy}${selected.instructedAtUtc ? ` (${selected.instructedAtUtc.slice(0, 10)})` : ''}`
              : '—'}</div>
            <div><span className="text-slate-500">{t('booking.startTime')}:</span> {hhmm(selected.startTime) || '—'}</div>
          </div>
          {selected.effectiveStartTime && (
            <p className="mt-1 text-xs text-slate-600">
              {t('booking.effectiveStart', { time: hhmm(selected.effectiveStartTime) })}
            </p>
          )}
          {(selected.shares ?? []).length > 0 && (
            <div className="mt-2 text-sm">
              <span className="text-slate-500">{t('booking.shares')}:</span>{' '}
              {(selected.shares ?? []).map((sh) =>
                `${sh.projectName} ${t('common.hoursValue', { value: sh.plannedHours })}`
                + (sh.expectedPad !== null ? ` · ${sh.expectedPad} PAD` : '')).join(' | ')}
            </div>
          )}
          {(selected.replacesBookingId || selected.rescheduledToBookingId) && (
            <p className="mt-2 text-xs text-slate-600">
              {selected.replacesBookingId && (
                replaces
                  ? <button className="underline" onClick={() => setSelected(replaces)}>
                      {t('booking.replacesOn', { date: replaces.date })}
                    </button>
                  : t('booking.replacesOther')
              )}
              {selected.rescheduledToBookingId && (
                replacedBy
                  ? <button className="underline" onClick={() => setSelected(replacedBy)}>
                      {t('booking.rescheduledToOn', { date: replacedBy.date })}
                    </button>
                  : t('booking.rescheduledToOther')
              )}
            </p>
          )}
          {selected.warnings.length > 0 && (
            <div className="mt-2 flex gap-2">
              {selected.warnings.map((w) => (
                <span key={w} className="rounded bg-amber-100 px-2 py-0.5 text-xs text-amber-800">
                  {warningLabel(w)}
                </span>
              ))}
            </div>
          )}
          <div className="mt-3">
            <h4 className="mb-1 text-xs font-semibold text-slate-500">{t('booking.evidence')}</h4>
            <Attachments ownerType="booking" ownerId={selected.id} canWrite={canManage} />
          </div>
          {editing && (
            <div className="mt-3 grid grid-cols-2 gap-2 rounded border border-slate-200 bg-slate-50 p-3 sm:grid-cols-4">
              <Field label={t('booking.date')}>
                <input type="date" className={inputCls} value={ef.date}
                  onChange={(e) => setEf({ ...ef, date: e.target.value })} />
              </Field>
              <Field label={t('booking.plannedHours')}>
                <NumInput className={inputCls} value={ef.plannedHours}
                  onChange={(v) => setEf({ ...ef, plannedHours: v })} />
              </Field>
              <Field label={t('booking.statusLabel')}>
                <select className={inputCls} value={ef.status}
                  onChange={(e) => setEf({ ...ef, status: e.target.value as PreStatus })}>
                  <option value="forecast">{t('booking.status.forecast')}</option>
                  <option value="tentative">{t('booking.status.tentative')}</option>
                  <option value="requested">{t('booking.status.requested')}</option>
                </select>
              </Field>
              <Field label={t('booking.activityRef')}>
                <input className={inputCls} value={ef.activityRef}
                  onChange={(e) => setEf({ ...ef, activityRef: e.target.value })} />
              </Field>
              <Field label={t('booking.workDescription')}>
                <input className={inputCls} value={ef.workDescription}
                  onChange={(e) => setEf({ ...ef, workDescription: e.target.value })} />
              </Field>
              <Field label={t('booking.customerContact')}>
                <input className={inputCls} value={ef.customerContact}
                  onChange={(e) => setEf({ ...ef, customerContact: e.target.value })} />
              </Field>
              <Field label={t('booking.evidenceRef')}>
                <input className={inputCls} value={ef.evidenceRef}
                  onChange={(e) => setEf({ ...ef, evidenceRef: e.target.value })} />
              </Field>
              <Field label={t('booking.startTime')} help={t('booking.hStartTime')}>
                <input type="time" className={inputCls} value={ef.startTime}
                  onChange={(e) => setEf({ ...ef, startTime: e.target.value })} />
              </Field>
              <Field label={t('booking.instructedBy')} help={t('booking.hInstructedBy')}>
                <input className={inputCls} list="booking-reps-edit" value={ef.instructedBy}
                  onChange={(e) => setEf({ ...ef, instructedBy: e.target.value })} />
                <datalist id="booking-reps-edit">
                  {reps.map((r) => <option key={r} value={r} />)}
                </datalist>
              </Field>
              <div className="col-span-2 flex gap-2 sm:col-span-4">
                <button className={btnCls} onClick={() => void saveEdit()}>{t('common.save')}</button>
                <button className={btnSecondaryCls} onClick={() => setEditing(false)}>{t('common.cancel')}</button>
              </div>
            </div>
          )}
          {reschedId === selected.id && (
            <div className="mt-3 flex items-end gap-2 rounded border border-slate-200 bg-slate-50 p-2">
              <Field label={t('booking.newDate')}>
                <input type="date" className={inputCls} value={reschedDate}
                  onChange={(e) => setReschedDate(e.target.value)} />
              </Field>
              <button className={btnCls} disabled={!reschedDate} onClick={() => void doReschedule()}>
                {t('booking.action.reschedule')}
              </button>
              <button className={btnSecondaryCls} onClick={() => setReschedId(null)}>
                {t('common.cancel')}
              </button>
              <span className="text-xs text-slate-500">{t('booking.rescheduleHint')}</span>
            </div>
          )}
          {completeId === selected.id && (
            <div className="mt-3 flex flex-wrap items-end gap-2 rounded border border-slate-200 bg-slate-50 p-2">
              {(selected.shares ?? []).length > 0 ? (
                (selected.shares ?? []).map((sh) => (
                  <Field key={sh.projectId} label={`${t('booking.actualHours')} · ${sh.projectName}`}>
                    <NumInput className={inputCls} value={actualByProject[sh.projectId] ?? ''}
                      onChange={(v) => setActualByProject({ ...actualByProject, [sh.projectId]: v })} />
                  </Field>
                ))
              ) : (
                <Field label={t('booking.actualHours')}>
                  <NumInput className={inputCls} value={actualHours}
                    onChange={(v) => setActualHours(v)} />
                </Field>
              )}
              <button className={btnCls} onClick={() => void doComplete()}>
                {t('booking.action.complete')}
              </button>
              <button className={btnSecondaryCls} onClick={() => setCompleteId(null)}>
                {t('common.cancel')}
              </button>
              <span className="text-xs text-slate-500">{t('booking.actualHoursHint')}</span>
            </div>
          )}
          {padNeed?.bookingId === selected.id && (
            <div className="mt-3 flex flex-wrap items-end gap-2 rounded border border-amber-200 bg-amber-50 p-2">
              <span className="w-full text-sm text-amber-900">
                {t('booking.padNeedIntro')}
                {padNeed.projectId !== selected.projectId && ` (${projectNames[padNeed.projectId] ?? ''})`}
              </span>
              <Field label={t('monthEnd.quantity')}>
                <NumInput className={inputCls} value={padNeed.quantity}
                  onChange={(v) => setPadNeed({ ...padNeed, quantity: v })} />
              </Field>
              <Field label={t('monthEnd.reason')}>
                <input className={inputCls} value={padNeed.reason}
                  onChange={(e) => setPadNeed({ ...padNeed, reason: e.target.value })} />
              </Field>
              <Field label={t('booking.requestedByRep')} help={t('booking.hRequestedByRep')}>
                <input className={inputCls} list="booking-reps" value={padNeed.representative}
                  onChange={(e) => setPadNeed({ ...padNeed, representative: e.target.value })} />
              </Field>
              <button className={btnCls} onClick={() => void requestAdditionalPad()}>
                {t('booking.requestAdditionalPad')}
              </button>
              <button className={btnSecondaryCls} onClick={() => setPadNeed(null)}>
                {t('common.cancel')}
              </button>
            </div>
          )}
          <div className="mt-3 flex flex-wrap gap-2">
            {selActions.map((a) => (
              <button key={a}
                className={a === 'cancel' || a === 'delete' ? 'rounded border border-red-300 px-3 py-1 text-sm text-red-600 hover:bg-red-50' : btnSecondaryCls}
                onClick={() => void runAction(selected, a)}>
                {t(`booking.action.${a}`)}
              </button>
            ))}
          </div>
        </Panel>
      )}
    </div>
  );
}
