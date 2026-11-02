import { Fragment, useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  confirmMobilisation, deleteMobilisation, describeError, getBooking, getMobilisations,
  getPadRequests, getProjects, getProjectWorkPackages, getResources, getTimesheet,
  getTimesheets, mergeMobilisation, padRequestAction, setMobilisationWorkPackage,
  splitMobilisation, suggestMobilisations, timesheetAction,
} from '@/lib/api';
import { confirmAction, promptNote, promptOptional } from '@/lib/confirm';
import { localMonth, monthKey, monthRange } from '@/lib/dates';
import { isoWeekLabel } from '@/lib/timekeeping';
import { canReopen, reviewNoteValid, sheetStatusCls } from '@/lib/timesheets';
import { canManage, isAdmin, useDevUserReload, useMe } from '@/lib/useMe';
import { useFlash } from '@/lib/useFlash';
import type {
  AdditionalPadRequest, MobilisationPeriod, MobilisationSuggestion,
  ResourceSummary, Timesheet, TimesheetDetail, WorkPackage,
} from '@/lib/types';
import {
  btnCls, btnDangerCls, btnSecondaryCls, ErrorBox, Field, inputCls, OkBox, Panel, tdCls, thCls,
} from '@/components/forms';
import { Attachments } from '@/components/reporting/Attachments';
import { ReviewHistory } from '@/components/reporting/ReviewHistory';
import { cn } from '@/lib/utils';
import { useFeatures } from '@/lib/features';

/** PM/admin review queue — submitted timesheets, Additional PAD requests
 * and mobilisation confirmations (design §2.2.6). */
export function Approvals() {
  const { t } = useTranslation();
  const me = useMe();
  const pad = useFeatures().pad;

  if (me && !canManage(me))
    return <div className="mx-auto max-w-4xl px-4 py-6 text-sm text-slate-500">{t('common.noAccess')}</div>;
  if (!me) return <div className="mx-auto max-w-4xl px-4 py-6 text-sm text-slate-400">{t('common.loading')}</div>;

  return (
    <div className="mx-auto max-w-5xl space-y-4 px-4 py-6">
      <TimesheetQueue />
      {isAdmin(me) && <ApprovedWeeks />}
      {pad && <PadRequestQueue canDecide={isAdmin(me)} />}
      <MobilisationPanel />
    </div>
  );
}

function TimesheetQueue() {
  const { t } = useTranslation();
  const [sheets, setSheets] = useState<Timesheet[] | null>(null);
  const [open, setOpen] = useState<TimesheetDetail | null>(null);
  const [note, setNote] = useState('');
  const [msg, setMsg] = useFlash();
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(() => {
    getTimesheets({ status: 'submitted' }).then(setSheets)
      .catch((e) => { setSheets([]); setError(describeError(e, t)); });
  }, [t]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);
  useDevUserReload(reload);

  const act = async (id: string, action: 'approve' | 'return' | 'reject') => {
    setError(null);
    // Returning or rejecting must tell the resource what to fix.
    if (action !== 'approve' && !reviewNoteValid(note)) {
      setError(t('approvals.noteRequired'));
      return;
    }
    if (action === 'reject' && !confirmAction(t('approvals.confirmReject'))) return;
    try {
      await timesheetAction(id, action, note.trim() || undefined);
      setMsg(t(`approvals.${action === 'approve' ? 'approved' : action === 'return' ? 'returned' : 'rejected'}`));
      setOpen(null); setNote(''); reload();
    } catch (e) { setError(describeError(e, t)); }
  };

  const toggle = async (s: Timesheet) => {
    if (open?.sheet.id === s.id) { setOpen(null); return; }
    setNote('');
    try { setOpen(await getTimesheet(s.id)); }
    catch (e) { setError(describeError(e, t)); }
  };

  return (
    <Panel title={t('approvals.queue')} help={t('approvals.queueHelp')}>
      <ErrorBox message={error} />
      <OkBox message={msg} />
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('approvals.week')}</th>
            <th className={thCls}>{t('approvals.resource')}</th>
            <th className={thCls}>{t('approvals.hours')}</th>
            <th className={thCls}>{t('approvals.items')}</th>
            <th className={thCls}>{t('approvals.submittedAt')}</th>
            <th className={thCls}></th>
          </tr>
        </thead>
        <tbody>
          {(sheets ?? []).map((s) => (
            <SheetRow key={s.id} sheet={s} open={open} note={note} setNote={setNote}
              onToggle={toggle} onAct={act} />
          ))}
          {sheets === null && (
            <tr><td className={tdCls} colSpan={6}>{t('common.loading')}</td></tr>
          )}
          {sheets?.length === 0 && (
            <tr><td className={tdCls} colSpan={6}>{t('approvals.noPending')}</td></tr>
          )}
        </tbody>
      </table>
    </Panel>
  );
}

function SheetRow({ sheet, open, note, setNote, onToggle, onAct }: {
  sheet: Timesheet;
  open: TimesheetDetail | null;
  note: string;
  setNote: (v: string) => void;
  onToggle: (s: Timesheet) => void;
  onAct: (id: string, action: 'approve' | 'return' | 'reject') => void;
}) {
  const { t } = useTranslation();
  const expanded = open?.sheet.id === sheet.id;
  const counts = [
    sheet.entryCount && `${sheet.entryCount} ${t('approvals.entries').toLowerCase()}`,
    sheet.travelCount && `${sheet.travelCount} ${t('approvals.travel').toLowerCase()}`,
    sheet.expenseCount && `${sheet.expenseCount} ${t('approvals.expenses').toLowerCase()}`,
    sheet.allowanceCount && `${sheet.allowanceCount} ${t('approvals.allowances').toLowerCase()}`,
  ].filter(Boolean).join(', ');
  const noteOk = reviewNoteValid(note);

  return (
    <>
      <tr className="border-b border-slate-100">
        <td className={cn(tdCls, 'font-medium')}>{isoWeekLabel(sheet.year, sheet.isoWeek)}</td>
        <td className={tdCls}>{sheet.resourceName}</td>
        <td className={tdCls}>{t('common.hoursValue', { value: sheet.totalActualHours })}</td>
        <td className={tdCls}>{counts}</td>
        <td className={tdCls}>{sheet.submittedUtc ? new Date(sheet.submittedUtc).toLocaleDateString() : '—'}</td>
        <td className={tdCls}>
          <button className={btnSecondaryCls} onClick={() => onToggle(sheet)}>
            {t('approvals.expand')}
          </button>
        </td>
      </tr>
      {expanded && (
        <tr className="border-b border-slate-200 bg-slate-50">
          <td colSpan={6} className="px-3 py-3">
            <DetailTables detail={open!} />
            <ReviewHistory reviews={open!.reviews} />
            <div className="mt-3 flex flex-wrap items-center gap-2">
              <input className={cn(inputCls, 'w-80')} placeholder={t('approvals.reviewNote')}
                value={note} onChange={(e) => setNote(e.target.value)} />
              <button className={btnCls} onClick={() => onAct(sheet.id, 'approve')}>
                {t('approvals.approve')}
              </button>
              <button className={btnSecondaryCls} disabled={!noteOk}
                title={noteOk ? undefined : t('approvals.noteRequired')}
                onClick={() => onAct(sheet.id, 'return')}>
                {t('approvals.return')}
              </button>
              <button className={btnDangerCls} disabled={!noteOk}
                title={noteOk ? undefined : t('approvals.noteRequired')}
                onClick={() => onAct(sheet.id, 'reject')}>
                {t('approvals.reject')}
              </button>
              <span className="text-xs text-slate-500">{t('approvals.noteHint')}</span>
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

function DetailTables({ detail }: { detail: TimesheetDetail }) {
  const { t } = useTranslation();
  const [receiptsFor, setReceiptsFor] = useState<string | null>(null);
  const cat = (c: string) => t(`myWork.category${c[0].toUpperCase()}${c.slice(1)}`);
  return (
    <div className="grid gap-4 md:grid-cols-2">
      <div>
        <h4 className="mb-1 text-xs font-semibold text-slate-500">{t('approvals.entries')}</h4>
        <table className="w-full border-collapse">
          <tbody>
            {detail.entries.map((e) => (
              <tr key={e.id} className="border-b border-slate-200">
                <td className={tdCls}>{e.date}</td>
                <td className={tdCls}>
                  {e.projectName}
                  {e.workPackageName && <span className="block text-xs text-slate-500">{e.workPackageName}</span>}
                  {e.description && <span className="block text-xs text-slate-500">{e.description}</span>}
                </td>
                <td className={cn(tdCls, e.category === 'break' && 'text-slate-400')}>{cat(e.category)}</td>
                <td className={tdCls}>
                  {t('common.hoursValue', { value: e.actualHours })}
                  {/* Frozen at approval: payable / billable incl. their minimum adjustments. */}
                  {e.payableHours != null && (
                    <span className="block text-xs text-slate-500">
                      {t('approvals.payable', { value: e.payableHours })}
                      {(e.minimumAdjustmentHours ?? 0) > 0
                        && ` (${t('approvals.minAdj', { value: e.minimumAdjustmentHours })})`}
                    </span>
                  )}
                  {e.billableHours != null && (
                    <span className="block text-xs text-slate-500">
                      {t('approvals.billable', { value: e.billableHours })}
                      {(e.billableMinimumAdjustmentHours ?? 0) > 0
                        && ` (${t('approvals.minAdj', { value: e.billableMinimumAdjustmentHours })})`}
                    </span>
                  )}
                </td>
                <td className={cn(tdCls, 'text-xs', e.billing === 'hours' ? 'text-amber-700' : 'text-slate-500')}>
                  {t(`myWork.billingMode.${e.billing}`)}
                </td>
              </tr>
            ))}
            {detail.entries.length === 0 && <tr><td className={tdCls}>—</td></tr>}
          </tbody>
        </table>
      </div>
      <div>
        <h4 className="mb-1 text-xs font-semibold text-slate-500">{t('approvals.travel')}</h4>
        <table className="w-full border-collapse">
          <tbody>
            {detail.travel.map((x) => (
              <tr key={x.id} className="border-b border-slate-200">
                <td className={tdCls}>{x.date}</td>
                <td className={tdCls}>{t(`myWork.travelMode.${x.mode}`)}</td>
                <td className={tdCls}>{[x.fromPlace, x.toPlace].filter(Boolean).join(' → ')}</td>
                <td className={tdCls}>{x.distanceKm != null ? t('common.kmValue', { value: x.distanceKm }) : ''}</td>
              </tr>
            ))}
            {detail.travel.length === 0 && <tr><td className={tdCls}>—</td></tr>}
          </tbody>
        </table>
        <h4 className="mb-1 mt-3 text-xs font-semibold text-slate-500">{t('approvals.expenses')}</h4>
        <table className="w-full border-collapse">
          <tbody>
            {detail.expenses.map((x) => (
              <Fragment key={x.id}>
                <tr className="border-b border-slate-200">
                  <td className={tdCls}>{x.date}</td>
                  <td className={tdCls}>
                    {t(`myWork.expenseCategory.${x.category}`)}
                    {x.description && <span className="block text-xs text-slate-500">{x.description}</span>}
                  </td>
                  <td className={tdCls}>{x.amount} {x.currency}</td>
                  <td className={tdCls}>
                    <button className={btnSecondaryCls}
                      onClick={() => setReceiptsFor(receiptsFor === x.id ? null : x.id)}>
                      {t('documents.receipts')}
                    </button>
                  </td>
                </tr>
                {receiptsFor === x.id && (
                  <tr className="border-b border-slate-200">
                    <td colSpan={4} className="px-3 py-2">
                      <Attachments ownerType="expense" ownerId={x.id} canWrite={false} />
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
            {detail.expenses.length === 0 && <tr><td className={tdCls}>—</td></tr>}
          </tbody>
        </table>
        <h4 className="mb-1 mt-3 text-xs font-semibold text-slate-500">{t('approvals.allowances')}</h4>
        <table className="w-full border-collapse">
          <tbody>
            {detail.allowances.map((x) => (
              <tr key={x.id} className="border-b border-slate-200">
                <td className={tdCls}>{x.date}</td>
                <td className={tdCls}>{x.amount} {x.currency}</td>
              </tr>
            ))}
            {detail.allowances.length === 0 && <tr><td className={tdCls}>—</td></tr>}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/** Recently approved/invoiced weeks — an administrator can reopen an
 * approved week (with a reason) while no closed month or billed document
 * covers it; the server re-checks both. */
function ApprovedWeeks() {
  const { t } = useTranslation();
  const me = useMe();
  const [sheets, setSheets] = useState<Timesheet[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useFlash();

  const reload = useCallback(() => {
    Promise.all([getTimesheets({ status: 'approved' }), getTimesheets({ status: 'invoiced' })])
      .then(([a, i]) => setSheets([...a, ...i]
        .sort((x, y) => y.year * 100 + y.isoWeek - (x.year * 100 + x.isoWeek)).slice(0, 25)))
      .catch((e) => { setSheets([]); setError(describeError(e, t)); });
  }, [t]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);
  useDevUserReload(reload);

  const reopen = async (s: Timesheet) => {
    const note = promptNote(t('approvals.reopenPrompt'));
    if (!note) return;
    setError(null);
    try {
      await timesheetAction(s.id, 'reopen', note);
      setMsg(t('approvals.reopened'));
      reload();
    } catch (e) { setError(describeError(e, t)); }
  };

  return (
    <Panel title={t('approvals.approvedWeeks')} help={t('approvals.approvedWeeksHelp')}>
      <ErrorBox message={error} />
      <OkBox message={msg} />
      <table className="w-full border-collapse">
        <tbody>
          {(sheets ?? []).map((s) => (
            <tr key={s.id} className="border-b border-slate-100">
              <td className={cn(tdCls, 'font-medium')}>{isoWeekLabel(s.year, s.isoWeek)}</td>
              <td className={tdCls}>{s.resourceName}</td>
              <td className={tdCls}>{t('common.hoursValue', { value: s.totalActualHours })}</td>
              <td className={tdCls}>
                <span className={cn('rounded px-2 py-0.5 text-xs', sheetStatusCls(s.status))}>
                  {t(`myWork.timesheetStatus.${s.status}`)}
                </span>
              </td>
              <td className={tdCls}>
                {canReopen(s.status, me) && (
                  <button className={btnSecondaryCls} onClick={() => void reopen(s)}>
                    {t('approvals.reopen')}
                  </button>
                )}
              </td>
            </tr>
          ))}
          {sheets === null && <tr><td className={tdCls} colSpan={5}>{t('common.loading')}</td></tr>}
          {sheets?.length === 0 && <tr><td className={tdCls} colSpan={5}>{t('approvals.noApproved')}</td></tr>}
        </tbody>
      </table>
    </Panel>
  );
}

/** Additional PAD requests awaiting a decision across all active projects —
 * Admin approves/rejects, PM sees the queue (design §2.2.6). */
function PadRequestQueue({ canDecide }: { canDecide: boolean }) {
  const { t } = useTranslation();
  const [rows, setRows] = useState<(AdditionalPadRequest & { projectName: string })[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useFlash();

  const reload = useCallback(() => {
    getProjects()
      .then((ps) => Promise.all(ps.filter((p) => p.isActive).map((p) =>
        getPadRequests(p.id).then((rs) => rs.map((r) => ({ ...r, projectName: p.name }))))))
      .then((all) => setRows(all.flat().filter((r) => r.status === 'submitted')
        .sort((a, b) => a.year * 12 + a.month - (b.year * 12 + b.month))))
      .catch((e) => { setRows([]); setError(describeError(e, t)); });
  }, [t]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  const decide = async (r: AdditionalPadRequest, action: 'approve' | 'reject') => {
    if (action === 'reject' && !confirmAction(t('approvals.confirmRejectPad'))) return;
    // Optional evidence: which customer representative approved (§14).
    const rep = action === 'approve' ? promptOptional(t('approvals.approvedByRepPrompt')) : '';
    if (rep === undefined) return;
    setError(null);
    try {
      await padRequestAction(r.id, action, rep);
      setMsg(t(action === 'approve' ? 'approvals.padApproved' : 'approvals.padRejected'));
      reload();
    } catch (e) { setError(describeError(e, t)); }
  };

  return (
    <Panel title={t('approvals.padRequests')} help={t('approvals.padRequestsHelp')}>
      <ErrorBox message={error} />
      <OkBox message={msg} />
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('economy.project')}</th>
            <th className={thCls}>{t('economy.month')}</th>
            <th className={thCls}>{t('monthEnd.quantity')}</th>
            <th className={thCls}>{t('monthEnd.reason')}</th>
            <th className={thCls}>{t('monthEnd.reference')}</th>
            <th className={thCls}>{t('monthEnd.linkedBooking')}</th>
            <th className={thCls}></th>
          </tr>
        </thead>
        <tbody>
          {(rows ?? []).map((r) => (
            <tr key={r.id} className="border-b border-slate-100">
              <td className={tdCls}>{r.projectName}</td>
              <td className={tdCls}>{monthKey(r.year, r.month)}</td>
              <td className={tdCls}>{r.quantity}</td>
              <td className={cn(tdCls, 'text-slate-500')}>{r.reason ?? '—'}</td>
              <td className={cn(tdCls, 'text-slate-500')}>{r.reference ?? '—'}</td>
              <td className={cn(tdCls, 'text-slate-500')}>
                {r.bookingId ? <LinkedBooking id={r.bookingId} /> : '—'}
              </td>
              <td className={tdCls}>
                {canDecide && (
                  <span className="flex gap-1">
                    <button className={btnSecondaryCls} onClick={() => void decide(r, 'approve')}>
                      {t('monthEnd.approve')}
                    </button>
                    <button className={btnDangerCls} onClick={() => void decide(r, 'reject')}>
                      {t('monthEnd.reject')}
                    </button>
                  </span>
                )}
              </td>
            </tr>
          ))}
          {rows === null && <tr><td className={tdCls} colSpan={7}>{t('common.loading')}</td></tr>}
          {rows?.length === 0 && <tr><td className={tdCls} colSpan={7}>{t('approvals.noPadRequests')}</td></tr>}
        </tbody>
      </table>
    </Panel>
  );
}

/** The prepared booking a PAD request waits on — approval confirms it. */
function LinkedBooking({ id }: { id: string }) {
  const { t } = useTranslation();
  const [label, setLabel] = useState<string | null>(null);
  useEffect(() => {
    let live = true;
    getBooking(id)
      .then((b) => { if (live) setLabel(`${b.date} · ${b.resourceName}`); })
      .catch(() => { if (live) setLabel(t('monthEnd.linkedBooking')); });
    return () => { live = false; };
  }, [id, t]);
  return <span title={t('approvals.linkedBookingHint')}>{label ?? '…'}</span>;
}

/** Suggested vs confirmed mobilisation periods for one resource in a month —
 * confirm (optionally to a work package), split, merge, re-target, delete. */
function MobilisationPanel() {
  const { t } = useTranslation();
  const [resources, setResources] = useState<ResourceSummary[]>([]);
  const [resourceId, setResourceId] = useState('');
  const [month, setMonth] = useState(() => { const m = localMonth(); return monthKey(m.year, m.month); });
  const [suggested, setSuggested] = useState<MobilisationSuggestion[]>([]);
  const [confirmed, setConfirmed] = useState<MobilisationPeriod[]>([]);
  const [pkgMap, setPkgMap] = useState<Record<string, WorkPackage[]>>({});
  const [suggestPkg, setSuggestPkg] = useState<Record<string, string>>({});
  const [splitAt, setSplitAt] = useState<Record<string, string>>({});
  const [mergeWith, setMergeWith] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { getResources().then(setResources).catch((e) => setError(describeError(e, t))); }, [t]);

  const loadPackages = useCallback((projectIds: string[]) => {
    for (const pid of new Set(projectIds)) {
      getProjectWorkPackages(pid)
        .then((p) => setPkgMap((m) => ({ ...m, [pid]: p })))
        .catch(() => {});
    }
  }, []);

  const reload = useCallback(() => {
    if (!resourceId || !/^\d{4}-\d{2}$/.test(month)) { setSuggested([]); setConfirmed([]); return; }
    const { from, to } = monthRange(Number(month.slice(0, 4)), Number(month.slice(5)));
    suggestMobilisations(resourceId, from, to)
      .then((s) => { setSuggested(s); loadPackages(s.map((x) => x.projectId)); })
      .catch((e) => { setSuggested([]); setError(describeError(e, t)); });
    getMobilisations({ resourceId })
      .then((all) => {
        const inMonth = all.filter((p) => p.startDate <= to && p.endDate >= from);
        setConfirmed(inMonth);
        loadPackages(inMonth.map((x) => x.projectId));
      })
      .catch((e) => setError(describeError(e, t)));
  }, [resourceId, month, t, loadPackages]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  const run = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); reload(); }
    catch (e) { setError(describeError(e, t)); }
  };
  const key = (s: MobilisationSuggestion) => `${s.projectId}-${s.startDate}`;

  return (
    <Panel title={t('approvals.mobilisations')} help={t('approvals.mobHelp')}>
      <ErrorBox message={error} />
      <div className="mb-3 flex flex-wrap items-end gap-2">
        <Field label={t('approvals.resource')}>
          <select className={inputCls} value={resourceId}
            onChange={(e) => setResourceId(e.target.value)}>
            <option value="">—</option>
            {resources.map((r) => <option key={r.id} value={r.id}>{r.displayName}</option>)}
          </select>
        </Field>
        <Field label={t('approvals.range')}>
          <input className={inputCls} type="month" value={month}
            onChange={(e) => setMonth(e.target.value)} />
        </Field>
      </div>

      {resourceId && (
        <>
          <h3 className="mb-1 text-xs font-semibold text-slate-500">{t('approvals.suggestionTitle')}</h3>
          <table className="mb-3 w-full border-collapse">
            <tbody>
              {suggested.map((s) => {
                const pkgs = pkgMap[s.projectId] ?? [];
                return (
                  <tr key={key(s)} className="border-b border-slate-100">
                    <td className={tdCls}>{s.projectName}</td>
                    <td className={tdCls}>{s.startDate} → {s.endDate}</td>
                    <td className={tdCls}>
                      {pkgs.length > 0 && (
                        <select className={inputCls} value={suggestPkg[key(s)] ?? ''}
                          aria-label={t('booking.workPackage')}
                          onChange={(e) => setSuggestPkg({ ...suggestPkg, [key(s)]: e.target.value })}>
                          <option value="">{t('approvals.noWorkPackage')}</option>
                          {pkgs.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                        </select>
                      )}
                    </td>
                    <td className={tdCls}>
                      <button className={btnSecondaryCls} onClick={() => void run(() => confirmMobilisation({
                        projectId: s.projectId, resourceId, startDate: s.startDate, endDate: s.endDate,
                        workPackageId: suggestPkg[key(s)] || null,
                      }))}>
                        {t('approvals.confirm')}
                      </button>
                    </td>
                  </tr>
                );
              })}
              {suggested.length === 0 && (
                <tr><td className={tdCls}>—</td></tr>
              )}
            </tbody>
          </table>
          <table className="w-full border-collapse">
            <tbody>
              {confirmed.map((p) => {
                const pkgs = pkgMap[p.projectId] ?? [];
                const siblings = confirmed.filter((o) => o.id !== p.id && o.projectId === p.projectId);
                return (
                  <tr key={p.id} className="border-b border-slate-100 align-top">
                    <td className={tdCls}>{p.projectName}</td>
                    <td className={tdCls}>
                      {p.startDate} → {p.endDate}
                      {p.visitNumber != null && (
                        <span className={cn('ml-1.5 rounded px-1.5 py-0.5 text-[10px]',
                          p.additionalTravel ? 'bg-amber-100 text-amber-800' : 'bg-sky-100 text-sky-800')}
                          title={p.additionalTravel ? t('approvals.visitBeyondHelp') : undefined}>
                          {t('approvals.visitNumber', { n: p.visitNumber, of: p.includedVisits ?? '∞' })}
                          {p.additionalTravel && ` · ${t('visits.beyond')}`}
                        </span>
                      )}
                    </td>
                    <td className={tdCls}>
                      {pkgs.length > 0 ? (
                        <select className={inputCls} value={p.workPackageId ?? ''}
                          aria-label={t('booking.workPackage')}
                          onChange={(e) => void run(() => setMobilisationWorkPackage(p.id, e.target.value || null))}>
                          <option value="">{t('approvals.noWorkPackage')}</option>
                          {pkgs.map((w) => <option key={w.id} value={w.id}>{w.name}</option>)}
                        </select>
                      ) : (p.workPackageName ?? '—')}
                    </td>
                    <td className={tdCls}>
                      <span className="flex flex-wrap items-center gap-1">
                        {p.startDate < p.endDate && (
                          <>
                            <input type="date" className={cn(inputCls, 'w-36')}
                              aria-label={t('approvals.splitAt')}
                              min={p.startDate} max={p.endDate} value={splitAt[p.id] ?? ''}
                              onChange={(e) => setSplitAt({ ...splitAt, [p.id]: e.target.value })} />
                            <button className={btnSecondaryCls}
                              disabled={!splitAt[p.id] || splitAt[p.id] <= p.startDate || splitAt[p.id] > p.endDate}
                              onClick={() => void run(() => splitMobilisation(p.id, splitAt[p.id]))}>
                              {t('approvals.split')}
                            </button>
                          </>
                        )}
                        {siblings.length > 0 && (
                          <>
                            <select className={cn(inputCls, 'w-40')} value={mergeWith[p.id] ?? ''}
                              aria-label={t('approvals.mergeWith')}
                              onChange={(e) => setMergeWith({ ...mergeWith, [p.id]: e.target.value })}>
                              <option value="">{t('approvals.mergeWith')}</option>
                              {siblings.map((o) => (
                                <option key={o.id} value={o.id}>{o.startDate} → {o.endDate}</option>
                              ))}
                            </select>
                            <button className={btnSecondaryCls} disabled={!mergeWith[p.id]}
                              onClick={() => {
                                if (!confirmAction(t('approvals.confirmMerge'))) return;
                                void run(() => mergeMobilisation(p.id, mergeWith[p.id]));
                              }}>
                              {t('approvals.merge')}
                            </button>
                          </>
                        )}
                        <button className={btnDangerCls}
                          onClick={() => {
                            if (!confirmAction(t('common.confirmDelete'))) return;
                            void run(() => deleteMobilisation(p.id));
                          }}>
                          {t('approvals.deletePeriod')}
                        </button>
                      </span>
                    </td>
                  </tr>
                );
              })}
              {confirmed.length === 0 && (
                <tr><td className={tdCls}>{t('approvals.noMobilisations')}</td></tr>
              )}
            </tbody>
          </table>
        </>
      )}
    </Panel>
  );
}
