import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createAllowance, createExpense, createTimeEntry, createTravelRecord,
  deleteAllowance, deleteExpense, deleteTimeEntry, deleteTravelRecord,
  describeError, getAllowances, getExpenses, getMyProjects, getProjectWorkPackages,
  getTimeEntries, getTimesheet, getTimesheets, getTravelRecords,
  submitTimesheet, updateAllowance, updateExpense, updateTimeEntry, updateTravelRecord,
} from '@/lib/api';
import { addDays as shiftIso, matchBooking } from '@/lib/bookings';
import { confirmAction } from '@/lib/confirm';
import { defaultCurrencyFor, SYSTEM_DEFAULT_CURRENCY } from '@/lib/currency';
import { localTodayIso as today } from '@/lib/dates';
import { expenseRuleErrors, saveExpenseConfirmingDuplicates } from '@/lib/expenses';
import { isoWeekDates, isoWeekLabel, isoWeekMonday, isoWeekOf } from '@/lib/timekeeping';
import { sheetStatusCls, weekSheetState } from '@/lib/timesheets';
import { useDevUserReload } from '@/lib/useMe';
import { useFlash } from '@/lib/useFlash';
import { invalidNumericFields } from '@/lib/validation';
import { numOrNull } from '@/lib/versioned';
import type {
  AllowanceClaim, Booking, EntryBilling, Expense, ProjectSummary, TimeEntry,
  Timesheet, TimesheetReview, TravelRecord, WorkPackage,
} from '@/lib/types';
import {
  btnCls, btnDangerCls, btnSecondaryCls, ErrorBox, Field, inputCls, NumInput, OkBox, Panel,
  tdCls, thCls,
} from '@/components/forms';
import { Attachments } from '@/components/reporting/Attachments';
import { ReviewHistory } from '@/components/reporting/ReviewHistory';
import { cn } from '@/lib/utils';

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
  const [myProjects, setMyProjects] = useState<ProjectSummary[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [banner, setBanner] = useFlash();

  const monday = isoWeekMonday(anchor);
  const sunday = shiftIso(monday, 6);
  const { year, week } = isoWeekOf(monday);
  const days = isoWeekDates(year, week);
  const range = { from: monday, to: sunday };

  const fail = useCallback((e: unknown) => setError(describeError(e, t)), [t]);

  const reload = useCallback(() => {
    const r = { from: isoWeekMonday(anchor), to: shiftIso(isoWeekMonday(anchor), 6) };
    Promise.all([
      getTimeEntries(r).then(setEntries),
      getTravelRecords(r).then(setTravel),
      getExpenses(r).then(setExpenses),
      getAllowances(r).then(setAllowances),
      getTimesheets().then(setSheets),
      getMyProjects().then(setMyProjects),
    ]).catch(fail);
  }, [anchor, fail]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);
  useDevUserReload(reload);

  // Projects the resource may report on: access list ∪ active bookings
  // (/api/projects/mine), plus anything already used on a row this week.
  const projectOptions = useMemo(() => {
    const map = new Map<string, string>();
    for (const p of myProjects) map.set(p.id, p.name);
    for (const b of bookings) map.set(b.projectId, b.projectName);
    for (const e of entries) map.set(e.projectId, e.projectName);
    return [...map.entries()].sort((a, b) => a[1].localeCompare(b[1]));
  }, [myProjects, bookings, entries]);

  const state = weekSheetState(sheets, year, week, [...entries, ...travel, ...expenses, ...allowances]);
  const { sheet: weekSheet, locked, draftCount } = state;

  // The week's review trail (returned/reopened comments) from the sheet detail.
  const [reviews, setReviews] = useState<TimesheetReview[]>([]);
  const weekSheetId = weekSheet?.id;
  const weekSheetStatus = weekSheet?.status;
  useEffect(() => {
    let live = true;
    if (!weekSheetId) { void Promise.resolve().then(() => { if (live) setReviews([]); }); return; }
    getTimesheet(weekSheetId)
      .then((d) => { if (live) setReviews(d.reviews ?? []); })
      .catch(() => { if (live) setReviews([]); });
    return () => { live = false; };
  }, [weekSheetId, weekSheetStatus]);

  const submit = async () => {
    setError(null);
    try {
      await submitTimesheet(year, week);
      setBanner(t('myWork.submittedBanner'));
      reload();
    } catch (e) { fail(e); }
  };

  const sectionProps = {
    days, range, projectOptions, projects: myProjects, locked, onChanged: reload, onError: fail,
  };

  return (
    <Panel
      title={`${t('myWork.reportWeek')} — ${isoWeekLabel(year, week)}`}
      help={t('myWork.reportHelp')}
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
      <ErrorBox message={error} />
      <OkBox message={banner} />
      {weekSheet && (
        <div className={cn('mb-2 rounded px-3 py-1.5 text-sm', sheetStatusCls(weekSheet.status))}>
          <p>{t(`myWork.timesheetStatus.${weekSheet.status}`)}</p>
          {state.needsResubmit && (
            <p className="mt-0.5 text-xs">
              {state.returnNote
                ? t('myWork.reviewerNote', { note: state.returnNote })
                : t('myWork.returnedNoNote')}
              {' '}{t('myWork.fixAndResubmit')}
            </p>
          )}
          <ReviewHistory reviews={reviews} />
        </div>
      )}

      <TimeSection key={`t-${range.from}`} {...sectionProps} entries={entries} bookings={bookings} />
      <TravelSection key={`r-${range.from}`} {...sectionProps} rows={travel} />
      <ExpenseSection key={`e-${range.from}`} {...sectionProps} rows={expenses} />
      <AllowanceSection key={`a-${range.from}`} {...sectionProps} rows={allowances} />

      <div className="mt-4 flex items-center gap-3 border-t border-slate-100 pt-3">
        <button className={btnCls} disabled={locked || draftCount === 0} onClick={() => void submit()}>
          {state.needsResubmit ? t('myWork.resubmitWeek') : t('myWork.submitWeek')}
        </button>
        <span className="text-xs text-slate-500">{t('myWork.draftRowsNote')}</span>
      </div>

      {sheets.length > 0 && (
        <div className="mt-4">
          <h3 className="mb-1 text-xs font-semibold text-slate-500">{t('myWork.sheets')}</h3>
          <div className="flex flex-wrap gap-2">
            {sheets.slice(0, 12).map((s) => (
              <span key={s.id} title={s.reviewerNote ?? undefined}
                className={cn('rounded px-2 py-1 text-xs', sheetStatusCls(s.status))}>
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
  /** Reporting projects incl. their contract currency (expense defaults). */
  projects: ProjectSummary[];
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

/** Delete after a translated confirmation. */
function useConfirmDelete(onChanged: () => void, onError: (e: unknown) => void) {
  const { t } = useTranslation();
  return (del: () => Promise<void>) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    void del().then(onChanged).catch(onError);
  };
}

// ── Time entries ────────────────────────────────────────────────────────────

function TimeSection({ days, range, projectOptions, entries, bookings, locked, onChanged, onError }:
  SectionProps & { entries: TimeEntry[]; bookings: Booking[] }) {
  const { t } = useTranslation();
  const empty = {
    date: range.from, projectId: '', workPackageId: '', category: 'work', hours: '',
    billing: 'contract' as EntryBilling, description: '',
  };
  const [form, setForm] = useState(empty);
  const [editId, setEditId] = useState<string | null>(null);
  const confirmDelete = useConfirmDelete(onChanged, onError);
  // Work packages per project — the row-level package picker only appears
  // when the picked project actually has packages to target.
  const [pkgMap, setPkgMap] = useState<Record<string, WorkPackage[]>>({});
  const packages = form.projectId ? (pkgMap[form.projectId] ?? []) : [];

  useEffect(() => {
    if (!form.projectId) return;
    let live = true;
    getProjectWorkPackages(form.projectId)
      .then((p) => { if (live) setPkgMap((m) => ({ ...m, [form.projectId]: p })); })
      .catch(() => {});
    return () => { live = false; };
  }, [form.projectId]);

  // A committed booking on that day + project links the row (booked rows
  // inherit the booking's package, so an explicit package means unbooked).
  const booking = form.projectId && !form.workPackageId
    ? matchBooking(bookings, form.date, form.projectId)
    : null;
  const hours = numOrNull(form.hours);

  const save = async () => {
    const bad = invalidNumericFields({ [t('myWork.hours')]: form.hours });
    if (bad.length || hours === null || hours <= 0 || hours > 24) {
      onError(new Error(t('common.invalidNumbers', { fields: t('myWork.hours') })));
      return;
    }
    try {
      const body = {
        projectId: form.projectId, workPackageId: form.workPackageId || null,
        bookingId: booking?.id ?? null,
        date: form.date,
        category: form.category as TimeEntry['category'],
        actualHours: hours, billing: form.billing,
        description: form.description || null,
      };
      if (editId) await updateTimeEntry(editId, body);
      else await createTimeEntry(body);
      setForm(empty); setEditId(null); onChanged();
    } catch (e) { onError(e); }
  };
  const startEdit = (e: TimeEntry) => {
    setEditId(e.id);
    setForm({
      date: e.date, projectId: e.projectId, workPackageId: e.workPackageId ?? '',
      category: e.category, hours: String(e.actualHours), billing: e.billing,
      description: e.description ?? '',
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
            <th className={thCls}>{t('myWork.billing')}</th>
            <th className={thCls}>{t('myWork.payableHours')}</th>
            <th className={thCls}>{t('myWork.description')}</th>
            <th className={thCls}></th>
          </tr>
        </thead>
        <tbody>
          {entries.map((e) => (
            <tr key={e.id} className="border-b border-slate-100">
              <td className={tdCls}>{e.date}</td>
              <td className={tdCls}>
                {e.projectName}
                {e.workPackageName && (
                  <span className="block text-xs text-slate-500">{e.workPackageName}</span>
                )}
                {e.bookingId && (
                  <span className="block text-xs text-blue-600">{t('myWork.linkedToBooking')}</span>
                )}
              </td>
              <td className={tdCls}>{t(`myWork.category${e.category[0].toUpperCase()}${e.category.slice(1)}`)}</td>
              <td className={tdCls}>{t('common.hoursValue', { value: e.actualHours })}</td>
              <td className={cn(tdCls, 'text-xs',
                e.billing === 'hours' ? 'text-amber-700' : 'text-slate-500')}>
                {t(`myWork.billingMode.${e.billing}`)}
              </td>
              <td className={tdCls}>{e.payableHours ?? '—'}</td>
              <td className={cn(tdCls, 'text-slate-500')}>{e.description}</td>
              <td className={tdCls}>
                {!e.timesheetId && (
                  <span className="flex gap-1">
                    <button className={btnSecondaryCls} onClick={() => startEdit(e)}>{t('common.edit')}</button>
                    <button className={btnDangerCls}
                      onClick={() => confirmDelete(() => deleteTimeEntry(e.id))}>
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
              onChange={(v) => setForm({ ...form, projectId: v, workPackageId: '' })} label="—" />
          </Field>
          {packages.length > 0 && (
            <Field label={t('booking.workPackage')} help={t('myWork.workPackageHelp')}>
              <select className={selectCls} value={form.workPackageId}
                onChange={(e) => setForm({ ...form, workPackageId: e.target.value })}>
                <option value="">—</option>
                {packages.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </Field>
          )}
          <Field label={t('myWork.category')}>
            <select className={selectCls} value={form.category}
              onChange={(e) => setForm({ ...form, category: e.target.value })}>
              {(['work', 'waiting', 'standby', 'break'] as const).map((c) => (
                <option key={c} value={c}>{t(`myWork.category${c[0].toUpperCase()}${c.slice(1)}`)}</option>
              ))}
            </select>
          </Field>
          <Field label={t('myWork.hours')}>
            <NumInput className={cn(inputCls, 'w-20')} value={form.hours}
              onChange={(v) => setForm({ ...form, hours: v })} />
          </Field>
          <Field label={t('myWork.billing')} help={t('myWork.billingHelp')}>
            <select className={selectCls} value={form.billing}
              onChange={(e) => setForm({ ...form, billing: e.target.value as EntryBilling })}>
              {(['contract', 'hours'] as const).map((b) => (
                <option key={b} value={b}>{t(`myWork.billingMode.${b}`)}</option>
              ))}
            </select>
          </Field>
          <Field label={t('myWork.description')}>
            <input className={inputCls} value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })} />
          </Field>
          <button className={btnSecondaryCls} disabled={!form.projectId || hours === null}
            onClick={() => void save()}>
            {editId ? t('common.save') : t('myWork.addEntry')}
          </button>
          {editId && (
            <button className={btnSecondaryCls} onClick={() => { setForm(empty); setEditId(null); }}>
              {t('common.cancel')}
            </button>
          )}
          {booking && (
            <span className="text-xs text-blue-600">
              {t('myWork.willLinkBooking', { status: t(`booking.status.${booking.status}`) })}
            </span>
          )}
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
  const confirmDelete = useConfirmDelete(onChanged, onError);

  const save = async () => {
    const bad = invalidNumericFields({
      [t('myWork.distanceKm')]: form.distanceKm,
      [t('myWork.travelHours')]: form.durationHours,
    });
    if (bad.length) {
      onError(new Error(t('common.invalidNumbers', { fields: bad.join(', ') })));
      return;
    }
    try {
      const body = {
        projectId: form.projectId || null, date: form.date,
        fromPlace: form.fromPlace || null, toPlace: form.toPlace || null,
        mode: form.mode as TravelRecord['mode'],
        distanceKm: numOrNull(form.distanceKm),
        durationHours: numOrNull(form.durationHours),
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
                      onClick={() => confirmDelete(() => deleteTravelRecord(x.id))}>
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
            <NumInput className={cn(inputCls, 'w-16')} value={form.distanceKm}
              onChange={(v) => setForm({ ...form, distanceKm: v })} />
          </Field>
          <Field label={t('myWork.travelHours')}>
            <NumInput className={cn(inputCls, 'w-16')} value={form.durationHours}
              onChange={(v) => setForm({ ...form, durationHours: v })} />
          </Field>
          <button className={btnSecondaryCls} onClick={() => void save()}>
            {editId ? t('common.save') : t('myWork.addTravel')}
          </button>
          {editId && (
            <button className={btnSecondaryCls} onClick={() => { setForm(empty); setEditId(null); }}>
              {t('common.cancel')}
            </button>
          )}
        </div>
      )}
    </section>
  );
}

// ── Expenses ────────────────────────────────────────────────────────────────

function ExpenseSection({ days, range, projectOptions, projects, rows, locked, onChanged, onError }:
  SectionProps & { rows: Expense[] }) {
  const { t } = useTranslation();
  const empty = {
    date: range.from, projectId: '', category: 'other', amount: '', currency: SYSTEM_DEFAULT_CURRENCY,
    description: '', receiptRef: '', reimbursable: true, rechargeable: false, inRate: false,
  };
  const [form, setForm] = useState(empty);
  const [editId, setEditId] = useState<string | null>(null);
  const [receiptsFor, setReceiptsFor] = useState<string | null>(null);
  const confirmDelete = useConfirmDelete(onChanged, onError);

  const amount = numOrNull(form.amount);
  const ruleErrors = expenseRuleErrors({
    projectId: form.projectId || null,
    reimbursableToResource: form.reimbursable,
    rechargeableToCustomer: form.rechargeable,
    includedInRate: form.inRate,
  });

  const save = async () => {
    if (amount === null || amount <= 0) {
      onError(new Error(t('common.invalidNumbers', { fields: t('myWork.amount') })));
      return;
    }
    try {
      const body = {
        projectId: form.projectId || null, date: form.date,
        category: form.category as Expense['category'],
        amount, currency: form.currency,
        description: form.description || null, receiptRef: form.receiptRef || null,
        reimbursableToResource: form.reimbursable,
        rechargeableToCustomer: form.rechargeable,
        includedInRate: form.inRate,
      };
      const saved = await saveExpenseConfirmingDuplicates(
        body,
        (b) => (editId ? updateExpense(editId, b) : createExpense(b)),
        () => confirmAction(t('myWork.confirmDuplicateExpense')),
      );
      if (saved === null) return; // user declined the duplicate warning
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
            <th className={thCls}>{t('myWork.expenseFlags')}</th>
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
                          onClick={() => confirmDelete(() => deleteExpense(x.id))}>
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
              onChange={(v) => setForm({
                ...form, projectId: v, currency: defaultCurrencyFor(v, projects, form.currency),
              })} label="—" />
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
              <NumInput className={cn(inputCls, 'w-20')} value={form.amount}
                onChange={(v) => setForm({ ...form, amount: v })} />
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
          <button className={btnSecondaryCls}
            disabled={amount === null || form.currency.length !== 3 || ruleErrors.length > 0}
            onClick={() => void save()}>
            {editId ? t('common.save') : t('myWork.addExpense')}
          </button>
          {editId && (
            <button className={btnSecondaryCls} onClick={() => { setForm(empty); setEditId(null); }}>
              {t('common.cancel')}
            </button>
          )}
          {ruleErrors.map((k) => (
            <span key={k} className="text-xs text-red-600">{t(k)}</span>
          ))}
        </div>
      )}
    </section>
  );
}

// ── Per diem ────────────────────────────────────────────────────────────────

function AllowanceSection({ days, range, projectOptions, projects, rows, locked, onChanged, onError }:
  SectionProps & { rows: AllowanceClaim[] }) {
  const { t } = useTranslation();
  const empty = { date: range.from, projectId: '', amount: '', currency: SYSTEM_DEFAULT_CURRENCY, notes: '' };
  const [form, setForm] = useState(empty);
  const [editId, setEditId] = useState<string | null>(null);
  const confirmDelete = useConfirmDelete(onChanged, onError);
  const amount = numOrNull(form.amount);

  const save = async () => {
    if (amount === null || amount <= 0) {
      onError(new Error(t('common.invalidNumbers', { fields: t('myWork.amount') })));
      return;
    }
    try {
      const body = {
        projectId: form.projectId || null, date: form.date,
        amount, currency: form.currency, notes: form.notes || null,
      };
      if (editId) await updateAllowance(editId, body);
      else await createAllowance(body);
      setForm(empty); setEditId(null); onChanged();
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
                  <span className="flex gap-1">
                    <button className={btnSecondaryCls} onClick={() => {
                      setEditId(x.id);
                      setForm({
                        date: x.date, projectId: x.projectId ?? '', amount: String(x.amount),
                        currency: x.currency, notes: x.notes ?? '',
                      });
                    }}>{t('common.edit')}</button>
                    <button className={btnDangerCls}
                      onClick={() => confirmDelete(() => deleteAllowance(x.id))}>
                      {t('common.delete')}
                    </button>
                  </span>
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
              onChange={(v) => setForm({
                ...form, projectId: v, currency: defaultCurrencyFor(v, projects, form.currency),
              })} label="—" />
          </Field>
          <Field label={t('myWork.amount')}>
            <span className="flex gap-1">
              <NumInput className={cn(inputCls, 'w-20')} value={form.amount}
                onChange={(v) => setForm({ ...form, amount: v })} />
              <input className={cn(inputCls, 'w-16')} value={form.currency} maxLength={3}
                onChange={(e) => setForm({ ...form, currency: e.target.value.toUpperCase() })} />
            </span>
          </Field>
          <Field label={t('common.notes')}>
            <input className={inputCls} value={form.notes}
              onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          </Field>
          <button className={btnSecondaryCls} disabled={amount === null || form.currency.length !== 3}
            onClick={() => void save()}>
            {editId ? t('common.save') : t('myWork.addAllowance')}
          </button>
          {editId && (
            <button className={btnSecondaryCls} onClick={() => { setForm(empty); setEditId(null); }}>
              {t('common.cancel')}
            </button>
          )}
        </div>
      )}
    </section>
  );
}
