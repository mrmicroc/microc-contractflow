import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import {
  closeMonth,
  createPadRequest,
  deletePadRequest,
  describeError,
  errorCode,
  getBookings,
  getClosePreview,
  getPadDashboard,
  getPadRequests,
  getProjects,
  padRequestAction,
  setBaseline,
} from '@/lib/api';
import { numOrNull } from '@/lib/versioned';
import { invalidNumericFields } from '@/lib/validation';
import type {
  AdditionalPadRequest, Booking, CloseMonthResult, PadDashboard, ProjectSummary,
} from '@/lib/types';
import { viewRange } from '@/lib/bookings';
import { blockedPreview, canSubmitForce, closeView, type ClosePreview } from '@/lib/monthClose';
import { cn } from '@/lib/utils';
import { confirmAction } from '@/lib/confirm';
import { localMonth, monthKey, shiftMonth as shiftYm } from '@/lib/dates';
import { canManage as canManageRole, canSeeCommercial, isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, ErrorBox, Field, inputCls, NumInput, Panel, tdCls, thCls,
} from '@/components/forms';

const fmt = (n: number) => n.toFixed(2).replace(/\.?0+$/, '');

/** Where a checklist item gets resolved. */
const CHECK_LINKS: Record<string, string> = {
  unresolvedBookings: '/plan',
  planningLeftovers: '/plan',
  unsubmittedTime: '/approvals',
  unapprovedTimesheets: '/approvals',
  unapprovedExpenses: '/approvals',
  travelNeedingReview: '/approvals',
};

/** Selector for project + month; the data body remounts on change. */
export function MonthEnd() {
  const { t } = useTranslation();
  const me = useMe();
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [projectId, setProjectId] = useState('');
  const [ym, setYm] = useState(() => localMonth());

  useEffect(() => {
    void getProjects().then(setProjects).catch(() => setProjects([]));
  }, []);

  const shiftMonth = (d: number) => setYm(({ year, month }) => shiftYm(year, month, d));

  if (me && !canSeeCommercial(me)) {
    return <div className="mx-auto max-w-6xl px-4 py-8"><p className="text-sm text-slate-500">{t('common.noAccess')}</p></div>;
  }

  return (
    <div className="mx-auto max-w-6xl space-y-4 px-4 py-6">
      <Panel
        title={t('monthEnd.title')}
        actions={
          <div className="flex items-center gap-2">
            <select className={inputCls} value={projectId}
              onChange={(e) => setProjectId(e.target.value)}>
              <option value="">{t('monthEnd.selectProject')}</option>
              {projects.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
            <button className={btnSecondaryCls} onClick={() => shiftMonth(-1)}>←</button>
            <span className="min-w-24 text-center text-sm font-medium">{monthKey(ym.year, ym.month)}</span>
            <button className={btnSecondaryCls} onClick={() => shiftMonth(1)}>→</button>
          </div>
        }
      >
        {!projectId && <p className="text-sm text-slate-500">{t('monthEnd.selectProject')}</p>}
      </Panel>

      {projectId && (
        <MonthEndBody
          key={`${projectId}-${ym.year}-${ym.month}`}
          projectId={projectId}
          year={ym.year}
          month={ym.month}
        />
      )}
    </div>
  );
}

function MonthEndBody({ projectId, year, month }: { projectId: string; year: number; month: number }) {
  const { t } = useTranslation();
  const me = useMe();
  const [dash, setDash] = useState<PadDashboard | null>(null);
  const [requests, setRequests] = useState<AdditionalPadRequest[]>([]);
  const [baselineAmt, setBaselineAmt] = useState('');
  const [baselineNote, setBaselineNote] = useState('');
  const [closeResult, setCloseResult] = useState<CloseMonthResult | null>(null);
  const [reqForm, setReqForm] = useState({ quantity: '', reason: '', reference: '' });
  const [reqOpen, setReqOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [preview, setPreview] = useState<ClosePreview | null>(null);
  const [forceReason, setForceReason] = useState('');
  const [bookings, setBookings] = useState<Booking[]>([]);

  const reload = useCallback(() => {
    getPadDashboard(projectId, year, month).then(setDash)
      .catch((e) => { setDash(null); setError(describeError(e, t)); });
    getPadRequests(projectId, year, month).then(setRequests).catch(() => setRequests([]));
    getClosePreview(projectId, year, month).then(setPreview).catch(() => setPreview(null));
    const range = viewRange('month', { year, month }, `${monthKey(year, month)}-01`);
    getBookings({ projectId, from: range.from, to: range.to }).then(setBookings)
      .catch(() => setBookings([]));
  }, [projectId, year, month, t]);
  const view = preview ? closeView(preview, isAdmin(me)) : null;
  const bookingLabel = (id: string | null) => {
    if (!id) return null;
    const b = bookings.find((x) => x.id === id);
    return b ? `${b.date} · ${b.resourceName}` : t('monthEnd.linkedBooking');
  };
  useEffect(() => { void reload(); }, [reload]);

  const saveBaseline = async () => {
    const bad = invalidNumericFields({ [t('monthEnd.baselineAmount')]: baselineAmt });
    if (bad.length) { setError(t('common.invalidNumbers', { fields: bad.join(', ') })); return; }
    const amount = numOrNull(baselineAmt);
    if (amount === null) return;
    setError(null);
    try {
      await setBaseline(projectId, year, month, amount, baselineNote || undefined);
      setBaselineAmt('');
      setBaselineNote('');
      reload();
    } catch (e) { setError(describeError(e, t)); }
  };

  const doClose = async (force = false) => {
    // Closing releases open commitments, writes carry-over/expiry and locks
    // the month — never one click away from an accident.
    if (!confirmAction(t(force ? 'monthEnd.preview.confirmForce' : 'monthEnd.confirmClose',
      { month: monthKey(year, month) }))) return;
    setError(null);
    try {
      setCloseResult(await closeMonth(projectId, year, month,
        force ? { reason: forceReason.trim() } : undefined));
      setForceReason('');
      reload();
    } catch (e) {
      // 409 closeBlocked carries a fresh preview — show it instead of a bare error.
      const fresh = blockedPreview(e);
      if (fresh) setPreview(fresh);
      setError(errorCode(e) === 'closeBlocked' ? t('monthEnd.preview.blockedError') : describeError(e, t));
    }
  };

  const saveRequest = async () => {
    const bad = invalidNumericFields({ [t('monthEnd.quantity')]: reqForm.quantity });
    if (bad.length) { setError(t('common.invalidNumbers', { fields: bad.join(', ') })); return; }
    const quantity = numOrNull(reqForm.quantity);
    if (quantity === null) return;
    setError(null);
    try {
      await createPadRequest({
        projectId, year, month, quantity,
        reason: reqForm.reason || null, reference: reqForm.reference || null,
      });
      setReqForm({ quantity: '', reason: '', reference: '' });
      setReqOpen(false);
      reload();
    } catch (e) { setError(describeError(e, t)); }
  };

  const actOnRequest = async (id: string, action: 'approve' | 'reject' | 'withdraw') => {
    if (action !== 'approve' && !confirmAction(t(`monthEnd.confirm.${action}`))) return;
    setError(null);
    try { await padRequestAction(id, action); reload(); }
    catch (e) { setError(describeError(e, t)); }
  };

  const removeRequest = async (id: string) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    setError(null);
    try { await deletePadRequest(id); reload(); }
    catch (e) { setError(describeError(e, t)); }
  };

  const canManage = canManageRole(me);

  const cards: [string, number, string?][] = dash ? [
    ['baseline', dash.baseline, 'text-slate-800'],
    ['carryOverIn', dash.carryOverIn, 'text-teal-700'],
    ['additionalApproved', dash.additionalApproved, 'text-teal-700'],
    ['totalAvailable', dash.totalAvailable, 'text-slate-900 font-semibold'],
    ['committed', dash.committedOpen, 'text-blue-700'],
    ['utilised', dash.utilised, 'text-green-700'],
    ['free', dash.free, dash.free < 0 ? 'text-red-700' : 'text-blue-800 font-semibold'],
    ['expired', dash.expired, 'text-slate-500'],
    ['carryOverOut', dash.carryOverOut, 'text-slate-500'],
  ] : [];
  // Secondary ledger categories — shown when non-zero so every number in the
  // ledger is reconcilable from the dashboard (§4.2).
  const minorCards: [string, number][] = dash ? ([
    ['released', dash.released],
    ['borrowedFromThisMonth', dash.borrowedFromThisMonth],
    ['adjustments', dash.adjustments],
  ] as [string, number][]).filter(([, v]) => v !== 0) : [];

  return (
    <>
      <Panel title={`${dash?.projectName ?? ''} ${monthKey(year, month)}`} help={t('monthEnd.dashHelp')}>
        <ErrorBox message={error} />
        {dash && (
          <>
            {dash.closed && (
              <span className="mb-2 inline-block rounded bg-slate-800 px-2 py-0.5 text-xs text-white">
                {t('monthEnd.closed')}
              </span>
            )}
            <div className="grid grid-cols-3 gap-2 sm:grid-cols-9">
              {cards.map(([key, value, cls]) => (
                <div key={key} className="rounded border border-slate-200 bg-slate-50 p-2 text-center">
                  <div className={cn('text-lg', cls)}>{fmt(value)}</div>
                  <div className="text-[10px] uppercase tracking-wide text-slate-500">
                    {t(`monthEnd.${key}`)}
                  </div>
                </div>
              ))}
            </div>
            {minorCards.length > 0 && (
              <p className="mt-2 flex flex-wrap gap-3 text-xs text-slate-500">
                {minorCards.map(([key, value]) => (
                  <span key={key}>{t(`monthEnd.${key}`)}: <span className="font-medium text-slate-700">{fmt(value)}</span></span>
                ))}
              </p>
            )}
          </>
        )}
        {closeResult && (
          <p className="mt-2 rounded bg-green-50 px-3 py-1.5 text-sm text-green-800">
            {t('monthEnd.closeResult', {
              carried: fmt(closeResult.carryOverOut),
              expired: fmt(closeResult.expired),
              next: monthKey(closeResult.nextYear, closeResult.nextMonth),
            })}
          </p>
        )}
      </Panel>

      {dash && isAdmin(me) && !dash.closed && (
        <Panel title={t('monthEnd.setBaseline')} help={t('monthEnd.baselineHelp')}>
          <div className="flex flex-wrap items-end gap-2">
            <Field label={t('monthEnd.baselineAmount')}>
              <NumInput className={inputCls} value={baselineAmt}
                onChange={(v) => setBaselineAmt(v)} />
            </Field>
            <Field label={t('common.notes')}>
              <input className={inputCls} value={baselineNote}
                onChange={(e) => setBaselineNote(e.target.value)} />
            </Field>
            <button className={btnCls} disabled={!baselineAmt.trim()} onClick={() => void saveBaseline()}>
              {t('common.save')}
            </button>
          </div>
        </Panel>
      )}

      {dash && canSeeCommercial(me) && !dash.closed && (
        <Panel title={t('monthEnd.closeMonth')} help={t('monthEnd.closeHelp')}>
          {!preview && <p className="text-sm text-slate-500">{t('common.loading')}</p>}
          {view && (
            <div className="space-y-3">
              {view.orderingErrors.length > 0 && (
                <div className="rounded border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800">
                  <p className="font-medium">{t('monthEnd.preview.orderingTitle')}</p>
                  <ul className="list-disc pl-5">
                    {view.orderingErrors.map((c) => (
                      <li key={c}>{t(`monthEnd.preview.ordering.${c}`, { defaultValue: c })}</li>
                    ))}
                  </ul>
                </div>
              )}
              <div>
                <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-500">
                  {t('monthEnd.preview.checklistTitle')}
                </p>
                {view.blocking.length + view.warnings.length === 0 ? (
                  <p className="text-sm text-green-700">{t('monthEnd.preview.allClear')}</p>
                ) : (
                  <ul className="space-y-1 text-sm">
                    {[...view.blocking, ...view.warnings].map((c) => (
                      <li key={c.code} className="flex items-center gap-2">
                        <span className={cn('rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase',
                          c.blocking ? 'bg-red-100 text-red-700' : 'bg-amber-100 text-amber-800')}>
                          {t(c.blocking ? 'monthEnd.preview.blocking' : 'monthEnd.preview.warning')}
                        </span>
                        <span>{t(`monthEnd.preview.check.${c.code}`, { count: c.count, defaultValue: c.code })}</span>
                        {CHECK_LINKS[c.code] && (
                          <Link className="text-xs text-blue-700 underline" to={CHECK_LINKS[c.code]}>
                            {t('monthEnd.preview.open')}
                          </Link>
                        )}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
              <div>
                <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-500">
                  {t('monthEnd.preview.stepsTitle')}
                </p>
                <table className="w-full border-collapse text-sm">
                  <tbody>
                    {view.steps.map((s, i) => (
                      <tr key={`${s.code}-${i}`} className="border-b border-slate-100">
                        <td className={tdCls}>{t(`monthEnd.preview.step.${s.code}`, { defaultValue: s.code })}</td>
                        <td className={cn(tdCls, 'text-right font-medium')}>{fmt(s.amount)}</td>
                        <td className={cn(tdCls, 'text-xs text-slate-500')}>{s.explanation}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {view.mode === 'close' && (
                <div className="flex flex-wrap items-center gap-3">
                  <button className={btnDangerCls} onClick={() => void doClose()}>
                    {t('monthEnd.closeMonth')}
                  </button>
                  <span className="text-xs text-slate-500">{t('monthEnd.closeWarning')}</span>
                </div>
              )}
              {view.mode === 'force' && (
                <div className="flex flex-wrap items-end gap-2 rounded border border-amber-200 bg-amber-50 p-2">
                  <Field label={t('monthEnd.preview.forceReason')}>
                    <input className={cn(inputCls, 'min-w-72')} value={forceReason}
                      onChange={(e) => setForceReason(e.target.value)} />
                  </Field>
                  <button className={btnDangerCls} disabled={!canSubmitForce(forceReason)}
                    onClick={() => void doClose(true)}>
                    {t('monthEnd.preview.forceClose')}
                  </button>
                  <span className="text-xs text-amber-800">{t('monthEnd.preview.forceHint')}</span>
                </div>
              )}
              {view.mode === 'none' && !isAdmin(me) && (
                <p className="text-xs text-slate-500">{t('monthEnd.preview.adminOnly')}</p>
              )}
            </div>
          )}
        </Panel>
      )}

      {dash && (
        <Panel
          title={t('monthEnd.padRequests')}
          help={t('monthEnd.requestsHelp')}
          actions={canManage && !dash.closed && !reqOpen && (
            <button className={btnSecondaryCls} onClick={() => setReqOpen(true)}>
              {t('monthEnd.newPadRequest')}
            </button>
          )}
        >
          {reqOpen && (
            <div className="mb-3 flex flex-wrap items-end gap-2 rounded border border-slate-200 bg-slate-50 p-2">
              <Field label={t('monthEnd.quantity')}>
                <NumInput className={inputCls} value={reqForm.quantity}
                  onChange={(v) => setReqForm({ ...reqForm, quantity: v })} />
              </Field>
              <Field label={t('monthEnd.reason')}>
                <input className={inputCls} value={reqForm.reason}
                  onChange={(e) => setReqForm({ ...reqForm, reason: e.target.value })} />
              </Field>
              <Field label={t('monthEnd.reference')}>
                <input className={inputCls} value={reqForm.reference}
                  onChange={(e) => setReqForm({ ...reqForm, reference: e.target.value })} />
              </Field>
              <button className={btnCls} disabled={!reqForm.quantity.trim()} onClick={() => void saveRequest()}>
                {t('common.save')}
              </button>
              <button className={btnSecondaryCls} onClick={() => setReqOpen(false)}>
                {t('common.cancel')}
              </button>
            </div>
          )}
          <table className="w-full border-collapse">
            <thead>
              <tr className="border-b border-slate-200">
                <th className={thCls}>{t('monthEnd.quantity')}</th>
                <th className={thCls}>{t('monthEnd.reason')}</th>
                <th className={thCls}>{t('monthEnd.reference')}</th>
                <th className={thCls}>{t('monthEnd.linkedBooking')}</th>
                <th className={thCls}>{t('booking.statusLabel')}</th>
                <th className={thCls}>{t('common.actions')}</th>
              </tr>
            </thead>
            <tbody>
              {requests.map((r) => (
                <tr key={r.id} className="border-b border-slate-100">
                  <td className={tdCls}>{fmt(r.quantity)}</td>
                  <td className={tdCls}>{r.reason ?? '—'}</td>
                  <td className={tdCls}>{r.reference ?? '—'}</td>
                  <td className={tdCls}>{bookingLabel(r.bookingId) ?? '—'}</td>
                  <td className={tdCls}>{t(`monthEnd.requestStatus.${r.status}`)}</td>
                  <td className={tdCls}>
                    {r.status === 'submitted' && (
                      <span className="flex gap-1">
                        {isAdmin(me) && (
                          <>
                            <button className={btnCls} onClick={() => void actOnRequest(r.id, 'approve')}>
                              {t('monthEnd.approve')}
                            </button>
                            <button className={btnDangerCls} onClick={() => void actOnRequest(r.id, 'reject')}>
                              {t('monthEnd.reject')}
                            </button>
                          </>
                        )}
                        {canManage && (
                          <>
                            <button className={btnSecondaryCls} onClick={() => void actOnRequest(r.id, 'withdraw')}>
                              {t('monthEnd.withdraw')}
                            </button>
                            <button className={btnDangerCls} onClick={() => void removeRequest(r.id)}>
                              {t('common.delete')}
                            </button>
                          </>
                        )}
                      </span>
                    )}
                  </td>
                </tr>
              ))}
              {requests.length === 0 && (
                <tr><td className={tdCls} colSpan={6}>{t('common.none')}</td></tr>
              )}
            </tbody>
          </table>
        </Panel>
      )}

      {dash && (
        <Panel title={t('monthEnd.transactions')} help={t('monthEnd.txHelp')}>
          <table className="w-full border-collapse">
            <thead>
              <tr className="border-b border-slate-200">
                <th className={thCls}>{t('monthEnd.txType')}</th>
                <th className={thCls}>{t('monthEnd.amount')}</th>
                <th className={thCls}>{t('common.notes')}</th>
                <th className={thCls}>{t('common.created')}</th>
              </tr>
            </thead>
            <tbody>
              {dash.transactions.map((tx) => (
                <tr key={tx.id} className="border-b border-slate-100">
                  <td className={tdCls}>{t(`monthEnd.tx.${tx.type}`)}</td>
                  <td className={cn(tdCls, 'text-right', tx.amount < 0 && 'text-red-600')}>
                    {fmt(tx.amount)}
                  </td>
                  <td className={tdCls}>
                    {tx.note ?? '—'}
                    {tx.bookingId && (
                      <span className="ml-1 text-xs text-slate-400">· {t('monthEnd.fromBooking')}</span>
                    )}
                  </td>
                  <td className={tdCls}>{new Date(tx.createdUtc).toLocaleString()}</td>
                </tr>
              ))}
              {dash.transactions.length === 0 && (
                <tr><td className={tdCls} colSpan={4}>{t('common.none')}</td></tr>
              )}
            </tbody>
          </table>
        </Panel>
      )}
    </>
  );
}
