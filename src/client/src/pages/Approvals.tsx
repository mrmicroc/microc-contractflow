import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  confirmMobilisation, deleteMobilisation, errorMessage, getMobilisations,
  getResources, getTimesheet, getTimesheets, suggestMobilisations, timesheetAction,
} from '@/lib/api';
import { isoWeekLabel } from '@/lib/timekeeping';
import { useMe } from '@/lib/useMe';
import type {
  MobilisationPeriod, MobilisationSuggestion, ResourceSummary,
  Timesheet, TimesheetDetail,
} from '@/lib/types';
import { btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, Panel, tdCls, thCls } from '@/components/forms';
import { cn } from '@/lib/utils';

const currentMonth = () => new Date().toISOString().slice(0, 7);

/** PM/admin review queue — submitted timesheets + mobilisation confirmations
 * (design §2.2.6). */
export function Approvals() {
  const { t } = useTranslation();
  const me = useMe();
  const canReview = me?.role === 'Administrator' || me?.role === 'ProjectManager';

  if (me && !canReview)
    return <div className="mx-auto max-w-4xl px-4 py-6 text-sm text-slate-500">{t('common.noAccess')}</div>;

  return (
    <div className="mx-auto max-w-5xl space-y-4 px-4 py-6">
      <TimesheetQueue />
      {canReview && <MobilisationPanel />}
    </div>
  );
}

function TimesheetQueue() {
  const { t } = useTranslation();
  const [sheets, setSheets] = useState<Timesheet[]>([]);
  const [open, setOpen] = useState<TimesheetDetail | null>(null);
  const [note, setNote] = useState('');
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(() => {
    getTimesheets({ status: 'submitted' }).then(setSheets).catch(() => {});
  }, []);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  const act = async (id: string, action: 'approve' | 'return' | 'reject') => {
    setError(null);
    try {
      await timesheetAction(id, action, note || undefined);
      setMsg(t(`approvals.${action === 'approve' ? 'approved' : action === 'return' ? 'returned' : 'rejected'}`));
      setOpen(null); setNote(''); reload();
    } catch (e) { setError(errorMessage(e)); }
  };

  const toggle = async (s: Timesheet) => {
    if (open?.sheet.id === s.id) { setOpen(null); return; }
    setNote('');
    setOpen(await getTimesheet(s.id).catch(() => null));
  };

  return (
    <Panel title={t('approvals.queue')} help={t('approvals.queueHelp')}>
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
      {msg && <p className="mb-2 rounded bg-emerald-50 px-3 py-1.5 text-sm text-emerald-700">{msg}</p>}
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
          {sheets.map((s) => (
            <SheetRow key={s.id} sheet={s} open={open} note={note} setNote={setNote}
              onToggle={toggle} onAct={act} />
          ))}
          {sheets.length === 0 && (
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

  return (
    <>
      <tr className="border-b border-slate-100">
        <td className={cn(tdCls, 'font-medium')}>{isoWeekLabel(sheet.year, sheet.isoWeek)}</td>
        <td className={tdCls}>{sheet.resourceName}</td>
        <td className={tdCls}>{sheet.totalActualHours} h</td>
        <td className={tdCls}>{counts}</td>
        <td className={tdCls}>{sheet.submittedUtc?.slice(0, 10)}</td>
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
            <div className="mt-3 flex flex-wrap items-center gap-2">
              <input className={cn(inputCls, 'w-72')} placeholder={t('approvals.reviewNote')}
                value={note} onChange={(e) => setNote(e.target.value)} />
              <button className={btnCls} onClick={() => onAct(sheet.id, 'approve')}>
                {t('approvals.approve')}
              </button>
              <button className={btnSecondaryCls} onClick={() => onAct(sheet.id, 'return')}>
                {t('approvals.return')}
              </button>
              <button className={btnDangerCls} onClick={() => onAct(sheet.id, 'reject')}>
                {t('approvals.reject')}
              </button>
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

function DetailTables({ detail }: { detail: TimesheetDetail }) {
  const { t } = useTranslation();
  return (
    <div className="grid gap-4 md:grid-cols-2">
      <div>
        <h4 className="mb-1 text-xs font-semibold text-slate-500">{t('approvals.entries')}</h4>
        <table className="w-full border-collapse">
          <tbody>
            {detail.entries.map((e) => (
              <tr key={e.id} className="border-b border-slate-200">
                <td className={tdCls}>{e.date}</td>
                <td className={tdCls}>{e.projectName}</td>
                <td className={tdCls}>{e.actualHours} h</td>
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
                <td className={tdCls}>{[x.fromPlace, x.toPlace].filter(Boolean).join(' → ')}</td>
                <td className={tdCls}>{x.distanceKm != null ? `${x.distanceKm} km` : ''}</td>
              </tr>
            ))}
            {detail.travel.length === 0 && <tr><td className={tdCls}>—</td></tr>}
          </tbody>
        </table>
        <h4 className="mb-1 mt-3 text-xs font-semibold text-slate-500">{t('approvals.expenses')}</h4>
        <table className="w-full border-collapse">
          <tbody>
            {detail.expenses.map((x) => (
              <tr key={x.id} className="border-b border-slate-200">
                <td className={tdCls}>{x.date}</td>
                <td className={tdCls}>{x.description}</td>
                <td className={tdCls}>{x.amount} {x.currency}</td>
              </tr>
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

/** Suggested vs confirmed mobilisation periods for one resource in a month. */
function MobilisationPanel() {
  const { t } = useTranslation();
  const [resources, setResources] = useState<ResourceSummary[]>([]);
  const [resourceId, setResourceId] = useState('');
  const [month, setMonth] = useState(currentMonth());
  const [suggested, setSuggested] = useState<MobilisationSuggestion[]>([]);
  const [confirmed, setConfirmed] = useState<MobilisationPeriod[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { getResources().then(setResources).catch(() => {}); }, []);

  const reload = useCallback(() => {
    if (!resourceId) { setSuggested([]); setConfirmed([]); return; }
    const from = `${month}-01`;
    const to = new Date(Date.UTC(Number(month.slice(0, 4)), Number(month.slice(5)), 0))
      .toISOString().slice(0, 10);
    suggestMobilisations(resourceId, from, to).then(setSuggested).catch(() => setSuggested([]));
    getMobilisations({ resourceId }).then((all) =>
      setConfirmed(all.filter((p) => p.startDate <= to && p.endDate >= from))).catch(() => {});
  }, [resourceId, month]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  const confirm = async (s: MobilisationSuggestion) => {
    setError(null);
    try {
      await confirmMobilisation({
        projectId: s.projectId, resourceId, startDate: s.startDate, endDate: s.endDate,
      });
      reload();
    } catch (e) { setError(errorMessage(e)); }
  };

  return (
    <Panel title={t('approvals.mobilisations')} help={t('approvals.mobHelp')}>
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
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
              {suggested.map((s) => (
                <tr key={`${s.projectId}-${s.startDate}`} className="border-b border-slate-100">
                  <td className={tdCls}>{s.projectName}</td>
                  <td className={tdCls}>{s.startDate} → {s.endDate}</td>
                  <td className={tdCls}>
                    <button className={btnSecondaryCls} onClick={() => void confirm(s)}>
                      {t('approvals.confirm')}
                    </button>
                  </td>
                </tr>
              ))}
              {suggested.length === 0 && (
                <tr><td className={tdCls}>—</td></tr>
              )}
            </tbody>
          </table>
          <table className="w-full border-collapse">
            <tbody>
              {confirmed.map((p) => (
                <tr key={p.id} className="border-b border-slate-100">
                  <td className={tdCls}>{p.projectName}</td>
                  <td className={tdCls}>{p.startDate} → {p.endDate}</td>
                  <td className={tdCls}>
                    <button className={btnDangerCls}
                      onClick={() => void deleteMobilisation(p.id).then(reload).catch((e) => setError(errorMessage(e)))}>
                      {t('approvals.deletePeriod')}
                    </button>
                  </td>
                </tr>
              ))}
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
