import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  bookingAction,
  createBooking,
  getBookings,
  getProjects,
  getResources,
  rescheduleBooking,
  updateBooking,
} from '@/lib/api';
import {
  availableActions, monthGrid, statusClasses, type BookingAction,
} from '@/lib/bookings';
import { numOrNull } from '@/lib/versioned';
import type { Booking, ProjectSummary, ResourceSummary } from '@/lib/types';
import { cn } from '@/lib/utils';
import { canSeeCommercial, useMe } from '@/lib/useMe';
import {
  btnCls, btnSecondaryCls, Field, inputCls, Panel,
} from '@/components/forms';

const pad2 = (n: number) => String(n).padStart(2, '0');
const todayIso = () => new Date().toISOString().slice(0, 10);
const monthKey = (y: number, m: number) => `${y}-${pad2(m)}`;

export function Plan() {
  const { t } = useTranslation();
  const me = useMe();
  const [ym, setYm] = useState(() => {
    const d = new Date();
    return { year: d.getFullYear(), month: d.getMonth() + 1 };
  });
  const [bookings, setBookings] = useState<Booking[]>([]);
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [resources, setResources] = useState<ResourceSummary[]>([]);
  const [projectFilter, setProjectFilter] = useState('');
  const [resourceFilter, setResourceFilter] = useState('');
  const [selected, setSelected] = useState<Booking | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [reschedId, setReschedId] = useState<string | null>(null);
  const [reschedDate, setReschedDate] = useState('');
  const [editing, setEditing] = useState(false);
  const [ef, setEf] = useState({ date: '', plannedHours: '', workDescription: '', activityRef: '', status: '' });
  const [error, setError] = useState<string | null>(null);
  const [f, setF] = useState({
    projectId: '', resourceId: '', locationId: '', date: '', plannedHours: '8',
    activityRef: '', workDescription: '', status: 'forecast' as 'forecast' | 'tentative' | 'requested',
  });

  const from = `${monthKey(ym.year, ym.month)}-01`;
  const to = `${monthKey(ym.year, ym.month)}-31`;

  const reload = useCallback(() => {
    getBookings({ from, to, projectId: projectFilter || undefined, resourceId: resourceFilter || undefined })
      .then(setBookings).catch(() => setBookings([]));
  }, [from, to, projectFilter, resourceFilter]);
  useEffect(() => { void reload(); }, [reload]);

  useEffect(() => {
    void getProjects().then(setProjects).catch(() => setProjects([]));
    void getResources().then(setResources).catch(() => setResources([]));
  }, []);

  const canManage = me !== null && ['Administrator', 'ProjectManager'].includes(me.role);
  const userResourceIds = new Set(resources.filter((r) => r.userId === me?.id).map((r) => r.id));

  const shiftMonth = (d: number) =>
    setYm(({ year, month }) => {
      const next = new Date(year, month - 1 + d, 1);
      return { year: next.getFullYear(), month: next.getMonth() + 1 };
    });

  const byDate = new Map<string, Booking[]>();
  for (const b of bookings) {
    const list = byDate.get(b.date) ?? [];
    list.push(b);
    byDate.set(b.date, list);
  }

  const runAction = async (id: string, action: BookingAction) => {
    setError(null);
    if (action === 'reschedule') { setReschedId(id); return; }
    if (action === 'edit') {
      if (selected) {
        setEf({
          date: selected.date,
          plannedHours: String(selected.plannedHours),
          workDescription: selected.workDescription ?? '',
          activityRef: selected.activityRef ?? '',
          status: selected.status,
        });
        setEditing(true);
      }
      return;
    }
    try {
      await bookingAction(id, action);
      setSelected(null);
      reload();
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      setError(msg.includes('insufficientPad') ? t('booking.insufficientPadError')
        : msg.includes('monthClosed') ? t('booking.monthClosedError')
        : msg);
    }
  };

  const saveEdit = async () => {
    if (!selected) return;
    setError(null);
    try {
      await updateBooking(selected.id, {
        date: ef.date,
        plannedHours: numOrNull(ef.plannedHours) ?? selected.plannedHours,
        workDescription: ef.workDescription || null,
        activityRef: ef.activityRef || null,
        status: ef.status as 'forecast' | 'tentative' | 'requested',
      });
      setEditing(false);
      setSelected(null);
      reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const doReschedule = async () => {
    if (!reschedId || !reschedDate) return;
    setError(null);
    try {
      await rescheduleBooking(reschedId, reschedDate);
      setReschedId(null);
      setReschedDate('');
      setSelected(null);
      reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const save = async () => {
    setError(null);
    try {
      await createBooking({
        projectId: f.projectId,
        resourceId: f.resourceId,
        locationId: f.locationId || null,
        date: f.date,
        plannedHours: numOrNull(f.plannedHours) ?? 8,
        activityRef: f.activityRef || null,
        workDescription: f.workDescription || null,
        customerContact: null,
        evidenceRef: null,
        status: f.status,
      });
      setFormOpen(false);
      setF({ projectId: '', resourceId: '', locationId: '', date: '', plannedHours: '8', activityRef: '', workDescription: '', status: 'forecast' });
      reload();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
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
            <button className={btnSecondaryCls} onClick={() => shiftMonth(-1)}>←</button>
            <span className="min-w-24 text-center text-sm font-medium">{monthKey(ym.year, ym.month)}</span>
            <button className={btnSecondaryCls} onClick={() => shiftMonth(1)}>→</button>
            {canManage && (
              <button className={btnCls} onClick={() => setFormOpen((v) => !v)}>
                {t('plan.newBooking')}
              </button>
            )}
          </div>
        }
      >
        {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}

        {formOpen && (
          <div className="mb-4 rounded border border-slate-200 bg-slate-50 p-3">
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              <Field label={t('booking.project')}>
                <select className={inputCls} value={f.projectId}
                  onChange={(e) => setF({ ...f, projectId: e.target.value })}>
                  <option value="">—</option>
                  {projects.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
              </Field>
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
                <input className={inputCls} inputMode="decimal" value={f.plannedHours}
                  onChange={(e) => setF({ ...f, plannedHours: e.target.value })} />
              </Field>
              <Field label={t('booking.activityRef')}>
                <input className={inputCls} value={f.activityRef}
                  onChange={(e) => setF({ ...f, activityRef: e.target.value })} />
              </Field>
              <Field label={t('booking.workDescription')}>
                <input className={inputCls} value={f.workDescription}
                  onChange={(e) => setF({ ...f, workDescription: e.target.value })} />
              </Field>
              <Field label={t('booking.status')}>
                <select className={inputCls} value={f.status}
                  onChange={(e) => setF({ ...f, status: e.target.value as typeof f.status })}>
                  <option value="forecast">{t('booking.statusForecast')}</option>
                  <option value="tentative">{t('booking.statusTentative')}</option>
                  <option value="requested">{t('booking.statusRequested')}</option>
                </select>
              </Field>
            </div>
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

        <div className="grid grid-cols-7 gap-px overflow-hidden rounded border border-slate-200 bg-slate-200">
          {['mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun'].map((d) => (
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
                    {(byDate.get(day) ?? []).map((b) => (
                      <button
                        key={b.id}
                        onClick={() => setSelected(b)}
                        className={cn(
                          'block w-full truncate rounded px-1 py-0.5 text-left text-[11px] font-medium',
                          statusClasses(b.status),
                        )}
                        title={`${b.resourceName} · ${b.projectName}`}
                      >
                        {b.resourceName}
                        {b.warnings.length > 0 && ' ⚠'}
                      </button>
                    ))}
                  </div>
                </>
              )}
            </div>
          ))}
        </div>
      </Panel>

      {selected && (
        <Panel title={`${selected.resourceName} · ${selected.date}`}
          actions={<button className={btnSecondaryCls} onClick={() => setSelected(null)}>✕</button>}>
          <div className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
            <div><span className="text-slate-500">{t('booking.project')}:</span> {selected.projectName}</div>
            <div><span className="text-slate-500">{t('booking.customer')}:</span> {selected.customerName}</div>
            <div><span className="text-slate-500">{t('booking.statusLabel')}:</span> {t(`booking.status.${selected.status}`)}</div>
            <div><span className="text-slate-500">{t('booking.plannedHours')}:</span> {selected.plannedHours} h</div>
            <div><span className="text-slate-500">{t('booking.expectedPad')}:</span> {selected.expectedPad ?? '—'}</div>
            <div><span className="text-slate-500">{t('booking.location')}:</span> {selected.locationName ?? '—'}</div>
            <div><span className="text-slate-500">{t('booking.activityRef')}:</span> {selected.activityRef ?? '—'}</div>
            <div><span className="text-slate-500">{t('booking.workDescription')}:</span> {selected.workDescription ?? '—'}</div>
          </div>
          {selected.warnings.length > 0 && (
            <div className="mt-2 flex gap-2">
              {selected.warnings.map((w) => (
                <span key={w} className="rounded bg-amber-100 px-2 py-0.5 text-xs text-amber-800">
                  {t(`booking.warning.${w}`)}
                </span>
              ))}
            </div>
          )}
          {editing && (
            <div className="mt-3 grid grid-cols-2 gap-2 rounded border border-slate-200 bg-slate-50 p-3 sm:grid-cols-5">
              <Field label={t('booking.date')}>
                <input type="date" className={inputCls} value={ef.date}
                  onChange={(e) => setEf({ ...ef, date: e.target.value })} />
              </Field>
              <Field label={t('booking.plannedHours')}>
                <input className={inputCls} inputMode="decimal" value={ef.plannedHours}
                  onChange={(e) => setEf({ ...ef, plannedHours: e.target.value })} />
              </Field>
              <Field label={t('booking.statusLabel')}>
                <select className={inputCls} value={ef.status}
                  onChange={(e) => setEf({ ...ef, status: e.target.value })}>
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
              <div className="col-span-2 flex gap-2 sm:col-span-5">
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
                {t('booking.reschedule')}
              </button>
              <button className={btnSecondaryCls} onClick={() => setReschedId(null)}>
                {t('common.cancel')}
              </button>
            </div>
          )}
          <div className="mt-3 flex flex-wrap gap-2">
            {selActions.map((a) => (
              <button key={a}
                className={a === 'cancel' ? 'rounded border border-red-300 px-3 py-1 text-sm text-red-600 hover:bg-red-50' : btnSecondaryCls}
                onClick={() => void runAction(selected.id, a)}>
                {t(`booking.action.${a}`)}
              </button>
            ))}
          </div>
        </Panel>
      )}
    </div>
  );
}
