import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  closeMonth,
  createPadRequest,
  getPadDashboard,
  getPadRequests,
  getProjects,
  padRequestAction,
  setBaseline,
} from '@/lib/api';
import { numOrNull } from '@/lib/versioned';
import type {
  AdditionalPadRequest, CloseMonthResult, PadDashboard, ProjectSummary,
} from '@/lib/types';
import { cn } from '@/lib/utils';
import { canSeeCommercial, isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, Panel, tdCls, thCls,
} from '@/components/forms';

const pad2 = (n: number) => String(n).padStart(2, '0');
const fmt = (n: number) => n.toFixed(2).replace(/\.?0+$/, '');

/** Selector for project + month; the data body remounts on change. */
export function MonthEnd() {
  const { t } = useTranslation();
  const me = useMe();
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [projectId, setProjectId] = useState('');
  const [ym, setYm] = useState(() => {
    const d = new Date();
    return { year: d.getFullYear(), month: d.getMonth() + 1 };
  });

  useEffect(() => {
    void getProjects().then(setProjects).catch(() => setProjects([]));
  }, []);

  const shiftMonth = (d: number) =>
    setYm(({ year, month }) => {
      const next = new Date(year, month - 1 + d, 1);
      return { year: next.getFullYear(), month: next.getMonth() + 1 };
    });

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
            <span className="min-w-24 text-center text-sm font-medium">{ym.year}-{pad2(ym.month)}</span>
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

  const reload = useCallback(() => {
    getPadDashboard(projectId, year, month).then(setDash).catch(() => setDash(null));
    getPadRequests(projectId, year, month).then(setRequests).catch(() => setRequests([]));
  }, [projectId, year, month]);
  useEffect(() => { void reload(); }, [reload]);

  const saveBaseline = async () => {
    const amount = numOrNull(baselineAmt);
    if (amount === null) return;
    setError(null);
    try {
      await setBaseline(projectId, year, month, amount, baselineNote || undefined);
      setBaselineAmt('');
      setBaselineNote('');
      reload();
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
  };

  const doClose = async () => {
    setError(null);
    try {
      setCloseResult(await closeMonth(projectId, year, month));
      reload();
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
  };

  const saveRequest = async () => {
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
    } catch (e) { setError(e instanceof Error ? e.message : String(e)); }
  };

  const actOnRequest = async (id: string, action: 'approve' | 'reject' | 'withdraw') => {
    setError(null);
    try { await padRequestAction(id, action); reload(); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); }
  };

  const canManage = me !== null && ['Administrator', 'ProjectManager'].includes(me.role);

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

  return (
    <>
      <Panel title={`${dash?.projectName ?? ''} ${year}-${pad2(month)}`}>
        {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
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
          </>
        )}
      </Panel>

      {dash && isAdmin(me) && !dash.closed && (
        <Panel title={t('monthEnd.setBaseline')}>
          <div className="flex flex-wrap items-end gap-2">
            <Field label={t('monthEnd.baselineAmount')}>
              <input className={inputCls} inputMode="decimal" value={baselineAmt}
                onChange={(e) => setBaselineAmt(e.target.value)} />
            </Field>
            <Field label={t('common.notes')}>
              <input className={inputCls} value={baselineNote}
                onChange={(e) => setBaselineNote(e.target.value)} />
            </Field>
            <button className={btnCls} disabled={!baselineAmt.trim()} onClick={() => void saveBaseline()}>
              {t('common.save')}
            </button>
            <button className={btnDangerCls} onClick={() => void doClose()}>
              {t('monthEnd.closeMonth')}
            </button>
          </div>
          {closeResult && (
            <p className="mt-2 rounded bg-green-50 px-3 py-1.5 text-sm text-green-800">
              {t('monthEnd.closeResult', {
                carried: fmt(closeResult.carryOverOut),
                expired: fmt(closeResult.expired),
                next: `${closeResult.nextYear}-${pad2(closeResult.nextMonth)}`,
              })}
            </p>
          )}
        </Panel>
      )}

      {dash && (
        <Panel
          title={t('monthEnd.padRequests')}
          actions={canManage && !dash.closed && !reqOpen && (
            <button className={btnSecondaryCls} onClick={() => setReqOpen(true)}>
              {t('monthEnd.newPadRequest')}
            </button>
          )}
        >
          {reqOpen && (
            <div className="mb-3 flex flex-wrap items-end gap-2 rounded border border-slate-200 bg-slate-50 p-2">
              <Field label={t('monthEnd.quantity')}>
                <input className={inputCls} inputMode="decimal" value={reqForm.quantity}
                  onChange={(e) => setReqForm({ ...reqForm, quantity: e.target.value })} />
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
                          <button className={btnSecondaryCls} onClick={() => void actOnRequest(r.id, 'withdraw')}>
                            {t('monthEnd.withdraw')}
                          </button>
                        )}
                      </span>
                    )}
                  </td>
                </tr>
              ))}
              {requests.length === 0 && (
                <tr><td className={tdCls} colSpan={5}>{t('common.none')}</td></tr>
              )}
            </tbody>
          </table>
        </Panel>
      )}

      {dash && (
        <Panel title={t('monthEnd.transactions')}>
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
                  <td className={tdCls}>{tx.note ?? '—'}</td>
                  <td className={tdCls}>{tx.createdUtc.slice(0, 16).replace('T', ' ')}</td>
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
