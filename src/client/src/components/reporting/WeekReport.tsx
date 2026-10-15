import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createAllowance, createExpense, createTimeEntry, createTravelRecord,
  deleteAllowance, deleteExpense, deleteTimeEntry, deleteTravelRecord,
  getAllowances, getExpenses, getTimeEntries, getTimesheets, getTravelRecords,
  submitTimesheet, updateExpense, updateTimeEntry, updateTravelRecord,
} from '@/lib/api';
import { isoWeekDates, isoWeekLabel, isoWeekMonday, isoWeekOf } from '@/lib/timekeeping';
import type {
  AllowanceClaim, Booking, Expense, TimeEntry, Timesheet, TravelRecord,
} from '@/lib/types';
import { btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, Panel, tdCls, thCls } from '@/components/forms';
import { Attachments } from '@/components/reporting/Attachments';
import { cn } from '@/lib/utils';

const today = () => new Date().toISOString().slice(0, 10);
const addDaysIso = (iso: string, n: number) =>
  new Date(`${iso}T00:00:00Z`).getTime() + n * 86_400_000;
const shiftIso = (iso: string, n: number) =>
  new Date(addDaysIso(iso, n)).toISOString().slice(0, 10);

/**
 * The resource's reporting week — time entries, travel, expenses and per-diem
 * drafts for one ISO week plus the submit-for-review action (design §6.2).
 */
export function WeekReport({ bookings }: { bookings: Booking[] }) {
  const { t } = useTranslation();
  const [anchor, setAnchor] = useState(today());
  const [entries, setEntries] = useState<TimeEntry[]>([]);
  const [travel, setTravel] = useState<TravelRecord[]>([]);
  const [expenses, setExpenses] = useState<Expense[]>([]);
  const [allowances, setAllowances] = useState<AllowanceClaim[]>([]);
  const [sheets, setSheets] = useState<Timesheet[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [banner, setBanner] = useState<string | null>(null);

  const monday = isoWeekMonday(anchor);
  const sunday = shiftIso(monday, 6);
  const { year, week } = isoWeekOf(monday);
  const days = isoWeekDates(year, week);
  const range = { from: monday, to: sunday };

  const reload = useCallback(() => {
    const r = { from: isoWeekMonday(anchor), to: shiftIso(isoWeekMonday(anchor), 6) };
    getTimeEntries(r).then(setEntries).catch(() => {});
    getTravelRecords(r).then(setTravel).catch(() => {});
    getExpenses(r).then(setExpenses).catch(() => {});
    getAllowances(r).then(setAllowances).catch(() => {});
    getTimesheets().then(setSheets).catch(() => {});
  }, [anchor]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  // Project picklist from the resource's own bookings (+ already-used ids).
  const projectOptions = useMemo(() => {
    const map = new Map<string, string>();
    for (const b of bookings) map.set(b.projectId, b.projectName);
    for (const e of entries) map.set(e.projectId, e.projectName);
    return [...map.entries()];
  }, [bookings, entries]);

  const weekSheet = sheets.find((s) => s.year === year && s.isoWeek === week
    && s.status !== 'returned' && s.status !== 'rejected');
  const locked = weekSheet?.status === 'submitted' || weekSheet?.status === 'approved';
  const draftCount = entries.filter((e) => !e.timesheetId).length
    + travel.filter((x) => !x.timesheetId).length
    + expenses.filter((x) => !x.timesheetId).length
    + allowances.filter((x) => !x.timesheetId).length;

  const fail = (e: unknown) => setError(e instanceof Error ? e.message : String(e));
  const submit = async () => {
    setError(null);
    try {
      await submitTimesheet(year, week);
      setBanner(t('myWork.submittedBanner'));
      reload();
    } catch (e) { fail(e); }
  };

  return (
    <Panel
      title={`${t('myWork.reportWeek')} — ${isoWeekLabel(year, week)}`}
      actions={
        <span className="flex items-center gap-1 text-xs">
          <button className={btnSecondaryCls} onClick={() => setAnchor(shiftIso(anchor, -7))}>
            {t('myWork.prevWeek')}
          </button>
          <button className={btnSecondaryCls} onClick={() => setAnchor(today())}>
            {t('myWork.thisWeek')}
          </button>
          <button className={btnSecondaryCls} onClick={() => setAnchor(shiftIso(anchor, 7))}>
            {t('myWork.nextWeek')}
          </button>
        </span>
      }
    >
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
      {banner && <p className="mb-2 rounded bg-emerald-50 px-3 py-1.5 text-sm text-emerald-700">{banner}</p>}
      {weekSheet && (
        <p className={cn('mb-2 rounded px-3 py-1.5 text-sm',
          weekSheet.status === 'approved' ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700')}>
          {t(`myWork.timesheetStatus.${weekSheet.status}`)}
        </p>
      )}

      <TimeSection key={`t-${range.from}`}
        days={days} range={range} projectOptions={projectOptions}
        entries={entries} locked={locked ?? false} onChanged={reload} onError={fail} />

      <TravelSection key={`r-${range.from}`}
        days={days} range={range} projectOptions={projectOptions}
        rows={travel} locked={locked ?? false} onChanged={reload} onError={fail} />

      <ExpenseSection key={`e-${range.from}`}
        days={days} range={range} projectOptions={projectOptions}
        rows={expenses} locked={locked ?? false} onChanged={reload} onError={fail} />

      <AllowanceSection key={`a-${range.from}`}
        days={days} range={range} projectOptions={projectOptions}
        rows={allowances} locked={locked ?? false} onChanged={reload} onError={fail} />

      <div className="mt-4 flex items-center gap-3 border-t border-slate-100 pt-3">
        <button className={btnCls} disabled={locked || draftCount === 0} onClick={() => void submit()}>
          {t('myWork.submitWeek')}
        </button>
        <span className="text-xs text-slate-500">{t('myWork.draftRowsNote')}</span>
      </div>

      {sheets.length > 0 && (
        <div className="mt-4">
          <h3 className="mb-1 text-xs font-semibold text-slate-500">{t('myWork.sheets')}</h3>
          <div className="flex flex-wrap gap-2">
            {sheets.slice(0, 12).map((s) => (
              <span key={s.id} className={cn('rounded px-2 py-1 text-xs',
                s.status === 'approved' ? 'bg-emerald-50 text-emerald-700'
                : s.status === 'submitted' ? 'bg-amber-50 text-amber-700'
                : 'bg-slate-100 text-slate-600')}>
                {isoWeekLabel(s.year, s.isoWeek)} · {t(`myWork.timesheetStatus.${s.status}`)}
              </span>
            ))}
          </div>
        </div>
      )}
    </Panel>
  );
}

type SectionProps = {
  days: string[];
  range: { from: string; to: string };
  projectOptions: [string, string][];
  locked: boolean;
  onChanged: () => void;
  onError: (e: unknown) => void;
};

const selectCls = inputCls;

function DayPicker({ days, value, onChange }: {
  days: string[]; value: string; onChange: (v: string) => void;
}) {
  return (
    <select className={selectCls} value={value} onChange={(e) => onChange(e.target.value)}>
      {days.map((d) => <option key={d} value={d}>{d}</option>)}
    </select>
  );
}

function ProjectPicker({ options, value, onChange, label }: {
  options: [string, string][]; value: string; onChange: (v: string) => void; label: string;
}) {
  return (
    <select className={selectCls} value={value} onChange={(e) => onChange(e.target.value)} required>
      <option value="">{label}</option>
      {options.map(([id, name]) => <option key={id} value={id}>{name}</option>)}
    </select>
  );
}

// ── Time entries ────────────────────────────────────────────────────────────

function TimeSection({ days, range, projectOptions, entries, locked, onChanged, onError }:
  SectionProps & { entries: TimeEntry[] }) {
  const { t } = useTranslation();
  const empty = { date: range.from, projectId: '', category: 'work', hours: '', description: '' };
  const [form, setForm] = useState(empty);
  const [editId, setEditId] = useState<string | null>(null);

  const save = async () => {
    try {
      const body = {
        projectId: form.projectId, date: form.date,
        category: form.category as TimeEntry['category'],
        actualHours: Number(form.hours), description: form.description || null,
      };
      if (editId) await updateTimeEntry(editId, body);
      else await createTimeEntry(body);
      setForm(empty); setEditId(null); onChanged();
    } catch (e) { onError(e); }
  };
  const startEdit = (e: TimeEntry) => {
    setEditId(e.id);
    setForm({
      date: e.date, projectId: e.projectId, category: e.category,
      hours: String(e.actualHours), description: e.description ?? '',
    });
  };

  return (
    <section className="mb-4">
      <h3 className="mb-1 text-xs font-semibold text-slate-500">{t('myWork.timeEntries')}</h3>
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('myWork.date')}</th>
            <th className={thCls}>{t('myWork.project')}</th>
            <th className={thCls}>{t('myWork.category')}</th>
            <th className={thCls}>{t('myWork.hours')}</th>
            <th className={thCls}>{t('myWork.payableHours')}</th>
            <th className={thCls}>{t('myWork.billableHours')}</th>
            <th className={thCls}>{t('myWork.description')}</th>
            <th className={thCls}></th>
          </tr>
        </thead>
        <tbody>
          {entries.map((e) => (
            <tr key={e.id} className="border-b border-slate-100">
              <td className={tdCls}>{e.date}</td>
              <td className={tdCls}>{e.projectName}</td>
              <td className={tdCls}>{t(`myWork.category${e.category[0].toUpperCase()}${e.category.slice(1)}`)}</td>
              <td className={tdCls}>{e.actualHours}</td>
              <td className={tdCls}>{e.payableHours ?? '—'}</td>
              <td className={tdCls}>{e.billableHours ?? '—'}</td>
              <td className={cn(tdCls, 'text-slate-500')}>{e.description}</td>
              <td className={tdCls}>
                {!e.timesheetId && (
                  <span className="flex gap-1">
                    <button className={btnSecondaryCls} onClick={() => startEdit(e)}>{t('common.edit')}</button>
                    <button className={btnDangerCls}
                      onClick={() => void deleteTimeEntry(e.id).then(onChanged).catch(onError)}>
                      {t('common.delete')}
                    </button>
                  </span>
                )}
              </td>
            </tr>
          ))}
          {entries.length === 0 && (
            <tr><td className={tdCls} colSpan={8}>{t('myWork.noRows')}</td></tr>
          )}
        </tbody>
      </table>
      {!locked && (
        <div className="mt-2 flex flex-wrap items-end gap-2">
          <Field label={t('myWork.date')}>
            <DayPicker days={days} value={form.date} onChange={(v) => setForm({ ...form, date: v })} />
          </Field>
          <Field label={t('myWork.project')}>
            <ProjectPicker options={projectOptions} value={form.projectId}
              onChange={(v) => setForm({ ...form, projectId: v })} label="—" />
          </Field>
          <Field label={t('myWork.category')}>
            <select className={selectCls} value={form.category}
              onChange={(e) => setForm({ ...form, category: e.target.value })}>
              {(['work', 'waiting', 'standby', 'break'] as const).map((c) => (
                <option key={c} value={c}>{t(`myWork.category${c[0].toUpperCase()}${c.slice(1)}`)}</option>
              ))}
            </select>
          </Field>
          <Field label={t('myWork.hours')}>
            <input className={cn(inputCls, 'w-20')} type="number" min="0.25" max="24" step="0.25"
              value={form.hours} onChange={(e) => setForm({ ...form, hours: e.target.value })} />
          </Field>
          <Field label={t('myWork.description')}>
            <input className={inputCls} value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })} />
          </Field>
          <button className={btnSecondaryCls} disabled={!form.projectId || !form.hours}
            onClick={() => void save()}>
            {editId ? t('common.save') : t('myWork.addEntry')}
          </button>
        </div>
      )}
    </section>
  );
}

// ── Travel ──────────────────────────────────────────────────────────────────

function TravelSection({ days, range, projectOptions, rows, locked, onChanged, onError }:
  SectionProps & { rows: TravelRecord[] }) {
  const { t } = useTranslation();
  const empty = {
    date: range.from, projectId: '', fromPlace: '', toPlace: '',
    mode: 'car', distanceKm: '', durationHours: '', notes: '',
  };
  const [form, setForm] = useState(empty);
  const [editId, setEditId] = useState<string | null>(null);

  const save = async () => {
    try {
      const body = {
        projectId: form.projectId || null, date: form.date,
        fromPlace: form.fromPlace || null, toPlace: form.toPlace || null,
        mode: form.mode as TravelRecord['mode'],
        distanceKm: form.distanceKm ? Number(form.distanceKm) : null,
        durationHours: form.durationHours ? Number(form.durationHours) : null,
        notes: form.notes || null,
      };
      if (editId) await updateTravelRecord(editId, body);
      else await createTravelRecord(body);
      setForm(empty); setEditId(null); onChanged();
    } catch (e) { onError(e); }
  };

  return (
    <section className="mb-4">
      <h3 className="mb-1 text-xs font-semibold text-slate-500">{t('myWork.travel')}</h3>
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('myWork.date')}</th>
            <th className={thCls}>{t('myWork.project')}</th>
            <th className={thCls}>{t('myWork.mode')}</th>
            <th className={thCls}>{t('myWork.from')}–{t('myWork.to')}</th>
            <th className={thCls}>{t('myWork.distanceKm')}</th>
            <th className={thCls}>{t('myWork.travelHours')}</th>
            <th className={thCls}></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((x) => (
            <tr key={x.id} className="border-b border-slate-100">
              <td className={tdCls}>{x.date}</td>
              <td className={tdCls}>{x.projectName ?? '—'}</td>
              <td className={tdCls}>{t(`myWork.travelMode.${x.mode}`)}</td>
              <td className={tdCls}>{[x.fromPlace, x.toPlace].filter(Boolean).join(' → ')}</td>
              <td className={tdCls}>{x.distanceKm ?? '—'}</td>
              <td className={tdCls}>{x.durationHours ?? '—'}</td>
              <td className={tdCls}>
                {!x.timesheetId && (
                  <span className="flex gap-1">
                    <button className={btnSecondaryCls} onClick={() => {
                      setEditId(x.id);
                      setForm({
                        date: x.date, projectId: x.projectId ?? '',
                        fromPlace: x.fromPlace ?? '', toPlace: x.toPlace ?? '',
                        mode: x.mode, distanceKm: x.distanceKm?.toString() ?? '',
                        durationHours: x.durationHours?.toString() ?? '', notes: x.notes ?? '',
                      });
                    }}>{t('common.edit')}</button>
                    <button className={btnDangerCls}
                      onClick={() => void deleteTravelRecord(x.id).then(onChanged).catch(onError)}>
                      {t('common.delete')}
                    </button>
                  </span>
                )}
              </td>
            </tr>
          ))}
          {rows.length === 0 && (
            <tr><td className={tdCls} colSpan={7}>{t('myWork.noRows')}</td></tr>
          )}
        </tbody>
      </table>
      {!locked && (
        <div className="mt-2 flex flex-wrap items-end gap-2">
          <Field label={t('myWork.date')}>
            <DayPicker days={days} value={form.date} onChange={(v) => setForm({ ...form, date: v })} />
          </Field>
          <Field label={t('myWork.project')}>
            <ProjectPicker options={projectOptions} value={form.projectId}
              onChange={(v) => setForm({ ...form, projectId: v })} label="—" />
          </Field>
          <Field label={t('myWork.mode')}>
            <select className={selectCls} value={form.mode}
              onChange={(e) => setForm({ ...form, mode: e.target.value })}>
              {(['car', 'train', 'flight', 'boat', 'other'] as const).map((m) => (
                <option key={m} value={m}>{t(`myWork.travelMode.${m}`)}</option>
              ))}
            </select>
          </Field>
          <Field label={`${t('myWork.from')}–${t('myWork.to')}`}>
            <span className="flex gap-1">
              <input className={cn(inputCls, 'w-24')} placeholder={t('myWork.from')} value={form.fromPlace}
                onChange={(e) => setForm({ ...form, fromPlace: e.target.value })} />
              <input className={cn(inputCls, 'w-24')} placeholder={t('myWork.to')} value={form.toPlace}
                onChange={(e) => setForm({ ...form, toPlace: e.target.value })} />
            </span>
          </Field>
          <Field label={t('myWork.distanceKm')}>
            <input className={cn(inputCls, 'w-16')} type="number" min="0" step="1" value={form.distanceKm}
              onChange={(e) => setForm({ ...form, distanceKm: e.target.value })} />
          </Field>
          <Field label={t('myWork.travelHours')}>
            <input className={cn(inputCls, 'w-16')} type="number" min="0" step="0.25" value={form.durationHours}
              onChange={(e) => setForm({ ...form, durationHours: e.target.value })} />
          </Field>
          <button className={btnSecondaryCls} onClick={() => void save()}>
            {editId ? t('common.save') : t('myWork.addTravel')}
          </button>
        </div>
      )}
    </section>
  );
}

// ── Expenses ────────────────────────────────────────────────────────────────

function ExpenseSection({ days, range, projectOptions, rows, locked, onChanged, onError }:
  SectionProps & { rows: Expense[] }) {
  const { t } = useTranslation();
  const empty = {
    date: range.from, projectId: '', category: 'other', amount: '', currency: 'SEK',
    description: '', receiptRef: '', reimbursable: true, rechargeable: false, inRate: false,
  };
  const [form, setForm] = useState(empty);
  const [editId, setEditId] = useState<string | null>(null);
  const [receiptsFor, setReceiptsFor] = useState<string | null>(null);

  const save = async () => {
    try {
      const body = {
        projectId: form.projectId || null, date: form.date,
        category: form.category as Expense['category'],
        amount: Number(form.amount), currency: form.currency,
        description: form.description || null, receiptRef: form.receiptRef || null,
        reimbursableToResource: form.reimbursable,
        rechargeableToCustomer: form.rechargeable,
        includedInRate: form.inRate,
      };
      if (editId) await updateExpense(editId, body);
      else await createExpense(body);
      setForm(empty); setEditId(null); onChanged();
    } catch (e) { onError(e); }
  };

  const flags = (x: Expense) => [
    x.reimbursableToResource ? t('myWork.reimbursable') : null,
    x.rechargeableToCustomer ? t('myWork.rechargeable') : null,
    x.includedInRate ? t('myWork.includedInRate') : null,
  ].filter(Boolean).join(' · ');

  return (
    <section className="mb-4">
      <h3 className="mb-1 text-xs font-semibold text-slate-500">{t('myWork.expenses')}</h3>
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('myWork.date')}</th>
            <th className={thCls}>{t('myWork.project')}</th>
            <th className={thCls}>{t('myWork.category')}</th>
            <th className={thCls}>{t('myWork.amount')}</th>
            <th className={thCls}>{t('common.actions')}</th>
            <th className={thCls}></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((x) => (
            <Fragment key={x.id}>
              <tr className="border-b border-slate-100">
                <td className={tdCls}>{x.date}</td>
                <td className={tdCls}>{x.projectName ?? '—'}</td>
                <td className={tdCls}>{t(`myWork.expenseCategory.${x.category}`)}</td>
                <td className={tdCls}>{x.amount} {x.currency}</td>
                <td className={cn(tdCls, 'text-xs text-slate-500')}>{flags(x)}</td>
                <td className={tdCls}>
                  <span className="flex gap-1">
                    <button className={btnSecondaryCls}
                      onClick={() => setReceiptsFor(receiptsFor === x.id ? null : x.id)}>
                      {t('documents.receipts')}
                    </button>
                    {!x.timesheetId && (
                      <>
                        <button className={btnSecondaryCls} onClick={() => {
                          setEditId(x.id);
                          setForm({
                            date: x.date, projectId: x.projectId ?? '', category: x.category,
                            amount: String(x.amount), currency: x.currency,
                            description: x.description ?? '', receiptRef: x.receiptRef ?? '',
                            reimbursable: x.reimbursableToResource,
                            rechargeable: x.rechargeableToCustomer, inRate: x.includedInRate,
                          });
                        }}>{t('common.edit')}</button>
                        <button className={btnDangerCls}
                          onClick={() => void deleteExpense(x.id).then(onChanged).catch(onError)}>
                          {t('common.delete')}
                        </button>
                      </>
                    )}
                  </span>
                </td>
              </tr>
              {receiptsFor === x.id && (
                <tr className="border-b border-slate-100 bg-slate-50">
                  <td colSpan={6} className="px-3 py-2">
                    <Attachments ownerType="expense" ownerId={x.id} canWrite={!x.timesheetId} />
                  </td>
                </tr>
              )}
            </Fragment>
          ))}
          {rows.length === 0 && (
            <tr><td className={tdCls} colSpan={6}>{t('myWork.noRows')}</td></tr>
          )}
        </tbody>
      </table>
      {!locked && (
        <div className="mt-2 flex flex-wrap items-end gap-2">
          <Field label={t('myWork.date')}>
            <DayPicker days={days} value={form.date} onChange={(v) => setForm({ ...form, date: v })} />
          </Field>
          <Field label={t('myWork.project')}>
            <ProjectPicker options={projectOptions} value={form.projectId}
              onChange={(v) => setForm({ ...form, projectId: v })} label="—" />
          </Field>
          <Field label={t('myWork.category')}>
            <select className={selectCls} value={form.category}
              onChange={(e) => setForm({ ...form, category: e.target.value })}>
              {(['travel', 'meal', 'accommodation', 'material', 'equipment', 'other'] as const).map((c) => (
                <option key={c} value={c}>{t(`myWork.expenseCategory.${c}`)}</option>
              ))}
            </select>
          </Field>
          <Field label={t('myWork.amount')}>
            <span className="flex gap-1">
              <input className={cn(inputCls, 'w-20')} type="number" min="0.01" step="0.01"
                value={form.amount} onChange={(e) => setForm({ ...form, amount: e.target.value })} />
              <input className={cn(inputCls, 'w-16')} value={form.currency} maxLength={3}
                onChange={(e) => setForm({ ...form, currency: e.target.value.toUpperCase() })} />
            </span>
          </Field>
          <Field label={t('myWork.description')}>
            <input className={inputCls} value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })} />
          </Field>
          <Field label={t('myWork.receiptRef')}>
            <input className={cn(inputCls, 'w-24')} value={form.receiptRef}
              onChange={(e) => setForm({ ...form, receiptRef: e.target.value })} />
          </Field>
          <label className="flex items-center gap-1 text-xs text-slate-600">
            <input type="checkbox" checked={form.reimbursable}
              onChange={(e) => setForm({ ...form, reimbursable: e.target.checked })} />
            {t('myWork.reimbursable')}
          </label>
          <label className="flex items-center gap-1 text-xs text-slate-600">
            <input type="checkbox" checked={form.rechargeable}
              onChange={(e) => setForm({ ...form, rechargeable: e.target.checked })} />
            {t('myWork.rechargeable')}
          </label>
          <label className="flex items-center gap-1 text-xs text-slate-600">
            <input type="checkbox" checked={form.inRate}
              onChange={(e) => setForm({ ...form, inRate: e.target.checked })} />
            {t('myWork.includedInRate')}
          </label>
          <button className={btnSecondaryCls} disabled={!form.amount} onClick={() => void save()}>
            {editId ? t('common.save') : t('myWork.addExpense')}
          </button>
        </div>
      )}
    </section>
  );
}

// ── Per diem ────────────────────────────────────────────────────────────────

function AllowanceSection({ days, range, projectOptions, rows, locked, onChanged, onError }:
  SectionProps & { rows: AllowanceClaim[] }) {
  const { t } = useTranslation();
  const empty = { date: range.from, projectId: '', amount: '', currency: 'SEK', notes: '' };
  const [form, setForm] = useState(empty);

  const save = async () => {
    try {
      await createAllowance({
        projectId: form.projectId || null, date: form.date,
        amount: Number(form.amount), currency: form.currency, notes: form.notes || null,
      });
      setForm(empty); onChanged();
    } catch (e) { onError(e); }
  };

  return (
    <section className="mb-2">
      <h3 className="mb-1 text-xs font-semibold text-slate-500">{t('myWork.allowances')}</h3>
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('myWork.date')}</th>
            <th className={thCls}>{t('myWork.project')}</th>
            <th className={thCls}>{t('myWork.amount')}</th>
            <th className={thCls}>{t('common.notes')}</th>
            <th className={thCls}></th>
          </tr>
        </thead>
        <tbody>
          {rows.map((x) => (
            <tr key={x.id} className="border-b border-slate-100">
              <td className={tdCls}>{x.date}</td>
              <td className={tdCls}>{x.projectName ?? '—'}</td>
              <td className={tdCls}>{x.amount} {x.currency}</td>
              <td className={cn(tdCls, 'text-slate-500')}>{x.notes}</td>
              <td className={tdCls}>
                {!x.timesheetId && (
                  <button className={btnDangerCls}
                    onClick={() => void deleteAllowance(x.id).then(onChanged).catch(onError)}>
                    {t('common.delete')}
                  </button>
                )}
              </td>
            </tr>
          ))}
          {rows.length === 0 && (
            <tr><td className={tdCls} colSpan={5}>{t('myWork.noRows')}</td></tr>
          )}
        </tbody>
      </table>
      {!locked && (
        <div className="mt-2 flex flex-wrap items-end gap-2">
          <Field label={t('myWork.date')}>
            <DayPicker days={days} value={form.date} onChange={(v) => setForm({ ...form, date: v })} />
          </Field>
          <Field label={t('myWork.project')}>
            <ProjectPicker options={projectOptions} value={form.projectId}
              onChange={(v) => setForm({ ...form, projectId: v })} label="—" />
          </Field>
          <Field label={t('myWork.amount')}>
            <span className="flex gap-1">
              <input className={cn(inputCls, 'w-20')} type="number" min="0.01" step="0.01"
                value={form.amount} onChange={(e) => setForm({ ...form, amount: e.target.value })} />
              <input className={cn(inputCls, 'w-16')} value={form.currency} maxLength={3}
                onChange={(e) => setForm({ ...form, currency: e.target.value.toUpperCase() })} />
            </span>
          </Field>
          <Field label={t('common.notes')}>
            <input className={inputCls} value={form.notes}
              onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          </Field>
          <button className={btnSecondaryCls} disabled={!form.amount} onClick={() => void save()}>
            {t('myWork.addAllowance')}
          </button>
        </div>
      )}
    </section>
  );
}
