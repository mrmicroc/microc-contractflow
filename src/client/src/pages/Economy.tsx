import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  deleteInvoice, deleteStatement, deleteSupplierInvoice, describeError,
  generateInvoice, generateStatement,
  getCustomers, getInvoice, getInvoices, getMargins, getProjects, getResources, getStatement,
  getStatements, getSupplierInvoice, getSupplierInvoices, getUnbilled,
  invoiceAction, matchSupplierInvoice, registerSupplierInvoice,
  statementAction, supplierInvoiceAction,
} from '@/lib/api';
import { confirmAction, promptNote } from '@/lib/confirm';
import { localMonth, monthKey } from '@/lib/dates';
import {
  approvalNeedsNote, canBackToDraft, generateMode, IRREVERSIBLE_STEPS, marginTotalsByCurrency,
  nextInvoiceStep, nextStatementStep, parseSupplierLines,
  type InvoiceStep, type StatementStep,
} from '@/lib/economy';
import { endClientLabel } from '@/lib/organisations';
import { isMissingRateWarning, originalLabel } from '@/lib/currency';
import { ExchangeRatesPanel } from '@/components/economy/ExchangeRatesPanel';
import { canHandleMoney, useDevUserReload, useMe } from '@/lib/useMe';
import { numOrNull } from '@/lib/versioned';
import type {
  BillingLine, CostBreakdown, CurrencySummary, CustomerSummary, InvoiceDetail, InvoiceSpecification, InvoiceStatus, MarginRow,
  ProjectSummary, ResourceSummary, StatementDetail, StatementStatus,
  SubcontractorStatement, SupplierInvoice, SupplierInvoiceDetail, SupplierInvoiceStatus,
  UnbilledRow,
} from '@/lib/types';
import {
  btnCls, btnDangerCls, btnSecondaryCls, ErrorBox, Field, inputCls, NumInput, Panel, tdCls, thCls,
} from '@/components/forms';
import { cn } from '@/lib/utils';

const currentMonth = () => { const m = localMonth(); return monthKey(m.year, m.month); };
const money = (n: number, ccy: string) =>
  `${n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${ccy}`;
const ymOf = (month: string) => ({ year: Number(month.slice(0, 4)), month: Number(month.slice(5)) });
const validMonth = (month: string) => /^\d{4}-\d{2}$/.test(month);

const invoiceStatusCls: Record<InvoiceStatus, string> = {
  draft: 'bg-slate-100 text-slate-600',
  customerReview: 'bg-amber-50 text-amber-700',
  final: 'bg-blue-50 text-blue-700',
  issued: 'bg-indigo-50 text-indigo-700',
  paid: 'bg-emerald-50 text-emerald-700',
};
const statementStatusCls: Record<StatementStatus, string> = {
  draft: 'bg-slate-100 text-slate-600',
  final: 'bg-blue-50 text-blue-700',
  paid: 'bg-emerald-50 text-emerald-700',
};

/** Billing surface (design §2.2.7): customer invoice specifications on the
 * contract-rate side, subcontractor statements on the agreement-rate side —
 * generated from the same approved timesheets but never sharing a rate.
 * Subcontractor pay (statements, supplier invoices, margins) is Admin/Finance
 * only; a resource sees just their own statements and invoices. */
export function Economy() {
  const { t } = useTranslation();
  const me = useMe();
  if (!me) return <div className="mx-auto max-w-5xl px-4 py-6 text-sm text-slate-400">{t('common.loading')}</div>;
  const isResource = me.role === 'Resource';
  const handlesMoney = canHandleMoney(me);
  if (!isResource && !handlesMoney && me.role !== 'ProjectManager')
    return <div className="mx-auto max-w-5xl px-4 py-6 text-sm text-slate-500">{t('common.noAccess')}</div>;

  return (
    <div className="mx-auto max-w-5xl space-y-4 px-4 py-6">
      {!isResource && <InvoicePanel canGenerate={handlesMoney} />}
      {(handlesMoney || isResource) && <StatementPanel canGenerate={handlesMoney} isResource={isResource} />}
      {(handlesMoney || isResource) && <SupplierInvoicePanel canGenerate={handlesMoney} isResource={isResource} />}
      {handlesMoney && <MarginsPanel />}
      {handlesMoney && <ExchangeRatesPanel />}
    </div>
  );
}

/** Confirms irreversible workflow steps before running them. */
function confirmStep(step: string, t: (k: string) => string): boolean {
  return !IRREVERSIBLE_STEPS.has(step) || confirmAction(t(`economy.confirmStep.${step}`));
}

// ── Customer invoice specifications ─────────────────────────────────────────

function InvoicePanel({ canGenerate }: { canGenerate: boolean }) {
  const { t } = useTranslation();
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [projectId, setProjectId] = useState('');
  const [month, setMonth] = useState(currentMonth());
  const [rows, setRows] = useState<InvoiceSpecification[] | null>(null);
  const [open, setOpen] = useState<InvoiceDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { getProjects().then(setProjects).catch(() => {}); }, []);
  const reload = useCallback(() => {
    getInvoices().then(setRows).catch((e) => { setRows([]); setError(describeError(e, t)); });
  }, [t]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);
  useDevUserReload(reload);

  // Re-fetch the open detail after an action so lines/status stay current.
  const refreshOpen = async (id: string) => {
    if (open?.invoice.id === id) setOpen(await getInvoice(id).catch(() => null));
  };

  // Unbilled approved rows for the chosen month — e.g. weeks approved after
  // the month's spec left draft; they need a supplement.
  const [unbilled, setUnbilled] = useState<UnbilledRow[]>([]);
  const reloadUnbilled = useCallback(() => {
    if (!canGenerate || !validMonth(month)) { setUnbilled([]); return; }
    const { year, month: m } = ymOf(month);
    getUnbilled(year, m).then(setUnbilled).catch(() => setUnbilled([]));
  }, [canGenerate, month]);
  useEffect(() => { void Promise.resolve().then(reloadUnbilled); }, [reloadUnbilled]);

  const { year: selYear, month: selMonth } = ymOf(month);
  const mode = projectId && validMonth(month)
    ? generateMode(rows ?? [], projectId, selYear, selMonth) : 'new';

  const generate = async () => {
    setError(null);
    try {
      await generateInvoice(projectId, selYear, selMonth, mode === 'supplement');
      reload(); reloadUnbilled();
    } catch (e) { setError(describeError(e, t)); }
  };
  const act = async (id: string, action: InvoiceStep | 'back-to-draft') => {
    if (!confirmStep(action, t)) return;
    setError(null);
    try { await invoiceAction(id, action); reload(); reloadUnbilled(); await refreshOpen(id); }
    catch (e) { setError(describeError(e, t)); }
  };
  const remove = async (id: string) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    setError(null);
    try { await deleteInvoice(id); if (open?.invoice.id === id) setOpen(null); reload(); }
    catch (e) { setError(describeError(e, t)); }
  };
  const toggle = async (s: InvoiceSpecification) => {
    if (open?.invoice.id === s.id) { setOpen(null); return; }
    try { setOpen(await getInvoice(s.id)); }
    catch (e) { setError(describeError(e, t)); }
  };

  return (
    <Panel title={t('economy.invoices')} help={t('economy.invoicesHelp')}>
      <ErrorBox message={error} />
      {canGenerate && (
        <div className="mb-3 flex flex-wrap items-end gap-2">
          <Field label={t('economy.project')}>
            <select className={inputCls} value={projectId} onChange={(e) => setProjectId(e.target.value)}>
              <option value="">—</option>
              {projects.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          </Field>
          <Field label={t('economy.month')}>
            <input className={inputCls} type="month" value={month} onChange={(e) => setMonth(e.target.value)} />
          </Field>
          <button className={btnCls} disabled={!projectId || !validMonth(month)} onClick={() => void generate()}>
            {t(`economy.generateMode.${mode}`)}
          </button>
          {mode === 'supplement' && (
            <span className="text-xs text-slate-500">{t('economy.supplementHint')}</span>
          )}
        </div>
      )}
      {unbilled.length > 0 && (
        <div className="mb-3 rounded border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
          <p className="font-medium">{t('economy.unbilledTitle', { month })}</p>
          <ul className="mt-1 space-y-0.5 text-xs">
            {unbilled.map((u) => (
              <li key={u.projectId}>
                {u.projectName}: {t('economy.unbilledRow', {
                  entries: u.entryCount, hours: u.hours, expenses: u.expenseCount,
                })}
                {u.hasFrozenSpec && <> — {t('economy.unbilledNeedsSupplement')}</>}
              </li>
            ))}
          </ul>
        </div>
      )}
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('economy.project')}</th>
            <th className={thCls}>{t('economy.month')}</th>
            <th className={thCls}>{t('common.status')}</th>
            <th className={thCls}>{t('economy.lines')}</th>
            <th className={cn(thCls, 'text-right')}>{t('economy.total')}</th>
            <th className={thCls}></th>
          </tr>
        </thead>
        <tbody>
          {(rows ?? []).map((s) => (
            <InvoiceRow key={s.id} s={s} expanded={open?.invoice.id === s.id} open={open}
              step={nextInvoiceStep(s.status)} canGenerate={canGenerate}
              onToggle={toggle} onAct={act} onDelete={(id) => void remove(id)} />
          ))}
          {rows === null && <tr><td className={tdCls} colSpan={6}>{t('common.loading')}</td></tr>}
          {rows?.length === 0 && <tr><td className={tdCls} colSpan={6}>{t('economy.none')}</td></tr>}
        </tbody>
      </table>
    </Panel>
  );
}

function InvoiceRow({ s, expanded, open, step, canGenerate, onToggle, onAct, onDelete }: {
  s: InvoiceSpecification; expanded: boolean; open: InvoiceDetail | null;
  step: InvoiceStep | null;
  canGenerate: boolean;
  onToggle: (s: InvoiceSpecification) => void;
  onAct: (id: string, a: InvoiceStep | 'back-to-draft') => void;
  onDelete: (id: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <>
      <tr className="border-b border-slate-100">
        <td className={tdCls}>
          {s.projectName}
          {s.warnings.length > 0 && (
            <span className="ml-1 text-amber-600" title={s.warnings.join('\n')}>⚠ {s.warnings.length}</span>
          )}
          <span className="block text-xs text-slate-500">
            {t('economy.addressedTo', { name: s.customerName })}
            {endClientLabel(s.endClientName, s.endClientReference) && (
              <> · {t('economy.forEndClient', { label: endClientLabel(s.endClientName, s.endClientReference) })}</>
            )}
          </span>
        </td>
        <td className={tdCls}>
          {monthKey(s.year, s.month)}
          {s.revision > 0 && (
            <span className="ml-1 rounded bg-slate-100 px-1 text-xs text-slate-500">
              {t('economy.revision', { n: s.revision })}
            </span>
          )}
        </td>
        <td className={tdCls}>
          <span className={cn('rounded px-2 py-0.5 text-xs', invoiceStatusCls[s.status])}>
            {t(`economy.invoiceStatus.${s.status}`)}
          </span>
        </td>
        <td className={tdCls}>{s.lineCount}</td>
        <td className={cn(tdCls, 'text-right font-medium')}>{money(s.totalAmount, s.currency)}</td>
        <td className={tdCls}>
          <span className="flex justify-end gap-1">
            <button className={btnSecondaryCls} onClick={() => onToggle(s)}>{t('approvals.expand')}</button>
            {canGenerate && canBackToDraft(s.status) && (
              <button className={btnSecondaryCls} onClick={() => onAct(s.id, 'back-to-draft')}>
                {t('economy.step.backToDraft')}
              </button>
            )}
            {canGenerate && step && (
              <button className={btnSecondaryCls} onClick={() => onAct(s.id, step)}>
                {t(`economy.step.${step}`)}
              </button>
            )}
            {canGenerate && s.status === 'draft' && (
              <button className={btnDangerCls} onClick={() => onDelete(s.id)}>{t('common.delete')}</button>
            )}
          </span>
        </td>
      </tr>
      {expanded && open && (
        <tr className="border-b border-slate-200 bg-slate-50">
          <td colSpan={6} className="px-3 py-2">
            <Warnings items={open.invoice.warnings} />
            <CurrencySummaryTable rows={open.invoice.currencySummary} currency={s.currency} />
            <LineTable lines={open.lines} currency={s.currency} />
          </td>
        </tr>
      )}
    </>
  );
}

// ── Subcontractor statements ────────────────────────────────────────────────

function StatementPanel({ canGenerate, isResource }: { canGenerate: boolean; isResource: boolean }) {
  const { t } = useTranslation();
  const [resources, setResources] = useState<ResourceSummary[]>([]);
  const [resourceId, setResourceId] = useState('');
  const [month, setMonth] = useState(currentMonth());
  const [rows, setRows] = useState<SubcontractorStatement[] | null>(null);
  const [open, setOpen] = useState<StatementDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { if (!isResource) getResources().then(setResources).catch(() => {}); }, [isResource]);
  const reload = useCallback(() => {
    getStatements().then(setRows).catch((e) => { setRows([]); setError(describeError(e, t)); });
  }, [t]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);
  useDevUserReload(reload);

  const refreshOpen = async (id: string) => {
    if (open?.statement.id === id) setOpen(await getStatement(id).catch(() => null));
  };
  const generate = async () => {
    setError(null);
    const { year, month: m } = ymOf(month);
    try { await generateStatement(resourceId, year, m); reload(); }
    catch (e) { setError(describeError(e, t)); }
  };
  const act = async (id: string, action: StatementStep) => {
    if (!confirmStep(action, t)) return;
    setError(null);
    try { await statementAction(id, action); reload(); await refreshOpen(id); }
    catch (e) { setError(describeError(e, t)); }
  };
  const remove = async (id: string) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    setError(null);
    try { await deleteStatement(id); if (open?.statement.id === id) setOpen(null); reload(); }
    catch (e) { setError(describeError(e, t)); }
  };
  const toggle = async (s: SubcontractorStatement) => {
    if (open?.statement.id === s.id) { setOpen(null); return; }
    try { setOpen(await getStatement(s.id)); }
    catch (e) { setError(describeError(e, t)); }
  };
  const cols = isResource ? 5 : 6;

  return (
    <Panel title={t('economy.statements')} help={t('economy.statementsHelp')}>
      <ErrorBox message={error} />
      {canGenerate && (
        <div className="mb-3 flex flex-wrap items-end gap-2">
          <Field label={t('economy.resource')}>
            <select className={inputCls} value={resourceId} onChange={(e) => setResourceId(e.target.value)}>
              <option value="">—</option>
              {resources.map((r) => <option key={r.id} value={r.id}>{r.displayName}</option>)}
            </select>
          </Field>
          <Field label={t('economy.month')}>
            <input className={inputCls} type="month" value={month} onChange={(e) => setMonth(e.target.value)} />
          </Field>
          <button className={btnCls} disabled={!resourceId || !validMonth(month)} onClick={() => void generate()}>
            {t('economy.generate')}
          </button>
        </div>
      )}
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            {!isResource && <th className={thCls}>{t('economy.resource')}</th>}
            <th className={thCls}>{t('economy.month')}</th>
            <th className={thCls}>{t('common.status')}</th>
            <th className={thCls}>{t('economy.lines')}</th>
            <th className={cn(thCls, 'text-right')}>{t('economy.total')}</th>
            <th className={thCls}></th>
          </tr>
        </thead>
        <tbody>
          {(rows ?? []).map((s) => (
            <StatementRow key={s.id} s={s} expanded={open?.statement.id === s.id} open={open}
              step={nextStatementStep(s.status)} canGenerate={canGenerate} isResource={isResource}
              onToggle={toggle} onAct={act} onDelete={(id) => void remove(id)} />
          ))}
          {rows === null && <tr><td className={tdCls} colSpan={cols}>{t('common.loading')}</td></tr>}
          {rows?.length === 0 && (
            <tr><td className={tdCls} colSpan={cols}>{t('economy.noneStatements')}</td></tr>
          )}
        </tbody>
      </table>
    </Panel>
  );
}

function StatementRow({ s, expanded, open, step, canGenerate, isResource, onToggle, onAct, onDelete }: {
  s: SubcontractorStatement; expanded: boolean; open: StatementDetail | null;
  step: StatementStep | null; canGenerate: boolean; isResource: boolean;
  onToggle: (s: SubcontractorStatement) => void;
  onAct: (id: string, a: StatementStep) => void;
  onDelete: (id: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <>
      <tr className="border-b border-slate-100">
        {!isResource && <td className={tdCls}>{s.resourceName}</td>}
        <td className={tdCls}>
          {monthKey(s.year, s.month)}
          {s.warnings.length > 0 && (
            <span className="ml-1 text-amber-600" title={s.warnings.join('\n')}>⚠ {s.warnings.length}</span>
          )}
        </td>
        <td className={tdCls}>
          <span className={cn('rounded px-2 py-0.5 text-xs', statementStatusCls[s.status])}>
            {t(`economy.statementStatus.${s.status}`)}
          </span>
        </td>
        <td className={tdCls}>{s.lineCount}</td>
        <td className={cn(tdCls, 'text-right font-medium')}>{money(s.totalAmount, s.currency)}</td>
        <td className={tdCls}>
          <span className="flex justify-end gap-1">
            <button className={btnSecondaryCls} onClick={() => onToggle(s)}>{t('approvals.expand')}</button>
            {canGenerate && step && (
              <button className={btnSecondaryCls} onClick={() => onAct(s.id, step)}>
                {t(`economy.step.${step}`)}
              </button>
            )}
            {canGenerate && s.status === 'draft' && (
              <button className={btnDangerCls} onClick={() => onDelete(s.id)}>{t('common.delete')}</button>
            )}
          </span>
        </td>
      </tr>
      {expanded && open && (
        <tr className="border-b border-slate-200 bg-slate-50">
          <td colSpan={isResource ? 5 : 6} className="px-3 py-2">
            <Warnings items={open.statement.warnings} />
            <CurrencySummaryTable rows={open.statement.currencySummary} currency={s.currency} />
            <LineTable lines={open.lines} currency={s.currency} />
          </td>
        </tr>
      )}
    </>
  );
}

// ── Supplier invoices (reconciliation) ──────────────────────────────────────

const supplierStatusCls: Record<SupplierInvoiceStatus, string> = {
  registered: 'bg-slate-100 text-slate-600',
  matched: 'bg-emerald-50 text-emerald-700',
  discrepancy: 'bg-amber-50 text-amber-700',
  approved: 'bg-blue-50 text-blue-700',
  paid: 'bg-emerald-50 text-emerald-700',
};

function SupplierInvoicePanel({ canGenerate, isResource }: { canGenerate: boolean; isResource: boolean }) {
  const { t } = useTranslation();
  const [resources, setResources] = useState<ResourceSummary[]>([]);
  const [statements, setStatements] = useState<SubcontractorStatement[]>([]);
  const [rows, setRows] = useState<SupplierInvoice[] | null>(null);
  const [open, setOpen] = useState<SupplierInvoiceDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const empty = { resourceId: '', number: '', invoiceDate: '', currency: 'SEK', totalAmount: '', linesText: '' };
  const [form, setForm] = useState(empty);
  const [formOpen, setFormOpen] = useState(false);
  const [matchFor, setMatchFor] = useState<string | null>(null);

  useEffect(() => {
    if (!isResource) getResources().then(setResources).catch(() => {});
  }, [isResource]);
  const reload = useCallback(() => {
    getSupplierInvoices().then(setRows).catch((e) => { setRows([]); setError(describeError(e, t)); });
    if (canGenerate) getStatements({ status: 'final' }).then(setStatements).catch(() => {});
  }, [t, canGenerate]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);
  useDevUserReload(reload);

  const total = numOrNull(form.totalAmount);
  const parsed = parseSupplierLines(form.linesText);

  const register = async () => {
    setError(null);
    if (!parsed.ok) {
      setError(parsed.errors.map((er) => t(`economy.lineError.${er.reason}`, { line: er.line })).join('\n'));
      return;
    }
    if (total === null) { setError(t('common.invalidNumbers', { fields: t('economy.total') })); return; }
    try {
      await registerSupplierInvoice({
        resourceId: form.resourceId, number: form.number.trim(), invoiceDate: form.invoiceDate,
        currency: form.currency, totalAmount: total,
        lines: parsed.lines.length ? parsed.lines : undefined,
      });
      setForm(empty); setFormOpen(false); reload();
    } catch (e) { setError(describeError(e, t)); }
  };
  const toggle = async (x: SupplierInvoice) => {
    if (open?.invoice.id === x.id) { setOpen(null); return; }
    try { setOpen(await getSupplierInvoice(x.id)); }
    catch (e) { setError(describeError(e, t)); }
  };
  const act = async (id: string, action: 'approve' | 'pay') => {
    const row = rows?.find((r) => r.id === id);
    // A discrepancy is approved only with a note explaining why.
    let note: string | undefined;
    if (action === 'approve' && row && approvalNeedsNote(row.status)) {
      const n = promptNote(t('economy.discrepancyNotePrompt'));
      if (!n) return;
      note = n;
    } else if (!confirmAction(t(`economy.confirmStep.${action === 'pay' ? 'pay' : 'approveSupplier'}`))) {
      return;
    }
    setError(null);
    try {
      await supplierInvoiceAction(id, action, note);
      reload();
      if (open?.invoice.id === id) setOpen(await getSupplierInvoice(id).catch(() => null));
    } catch (e) { setError(describeError(e, t)); }
  };
  const remove = async (id: string) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    setError(null);
    try { await deleteSupplierInvoice(id); if (open?.invoice.id === id) setOpen(null); reload(); }
    catch (e) { setError(describeError(e, t)); }
  };
  const cols = isResource ? 5 : 6;

  return (
    <Panel title={t('economy.supplierInvoices')}
      help={t('economy.supplierHelp')}
      actions={canGenerate && (
        <button className={btnSecondaryCls} onClick={() => setFormOpen((v) => !v)}>
          {t('economy.registerInvoice')}
        </button>
      )}>
      <ErrorBox message={error} />

      {formOpen && (
        <div className="mb-3 rounded border border-slate-200 bg-slate-50 p-3">
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-5">
            <Field label={t('economy.resource')}>
              <select className={inputCls} value={form.resourceId}
                onChange={(e) => setForm({ ...form, resourceId: e.target.value })}>
                <option value="">—</option>
                {resources.map((r) => <option key={r.id} value={r.id}>{r.displayName}</option>)}
              </select>
            </Field>
            <Field label={t('economy.invoiceNumber')}>
              <input className={inputCls} value={form.number}
                onChange={(e) => setForm({ ...form, number: e.target.value })} />
            </Field>
            <Field label={t('economy.invoiceDate')}>
              <input className={inputCls} type="date" value={form.invoiceDate}
                onChange={(e) => setForm({ ...form, invoiceDate: e.target.value })} />
            </Field>
            <Field label={t('common.currency')}>
              <input className={inputCls} value={form.currency} maxLength={3}
                onChange={(e) => setForm({ ...form, currency: e.target.value.toUpperCase() })} />
            </Field>
            <Field label={t('economy.total')}>
              <NumInput className={inputCls} value={form.totalAmount}
                onChange={(v) => setForm({ ...form, totalAmount: v })} />
            </Field>
          </div>
          <Field label={`${t('economy.lines')} (${t('economy.linesHint')})`}>
            <textarea className={cn(inputCls, 'mt-1 h-20 font-mono text-xs', !parsed.ok && 'border-red-400')}
              placeholder={'hours 4800\nexpense 1200 Cable\nmileage 2500\ntravelTime 600\nallowance 350'}
              value={form.linesText} onChange={(e) => setForm({ ...form, linesText: e.target.value })} />
          </Field>
          <div className="mt-2 flex gap-2">
            <button className={btnCls}
              disabled={!form.resourceId || !form.number.trim() || !form.invoiceDate
                || total === null || form.currency.length !== 3}
              onClick={() => void register()}>
              {t('common.save')}
            </button>
            <button className={btnSecondaryCls} onClick={() => setFormOpen(false)}>{t('common.cancel')}</button>
          </div>
        </div>
      )}

      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            {!isResource && <th className={thCls}>{t('economy.resource')}</th>}
            <th className={thCls}>{t('economy.invoiceNumber')}</th>
            <th className={thCls}>{t('economy.invoiceDate')}</th>
            <th className={thCls}>{t('common.status')}</th>
            <th className={cn(thCls, 'text-right')}>{t('economy.total')}</th>
            <th className={thCls}></th>
          </tr>
        </thead>
        <tbody>
          {(rows ?? []).map((x) => (
            <SupplierRow key={x.id} x={x} isResource={isResource} canGenerate={canGenerate}
              open={open} matchFor={matchFor} statements={statements}
              onToggle={toggle} onAct={act}
              onMatch={async (statementId) => {
                setError(null);
                try { setOpen(await matchSupplierInvoice(x.id, statementId)); setMatchFor(null); reload(); }
                catch (e) { setError(describeError(e, t)); }
              }}
              onMatchToggle={() => setMatchFor(matchFor === x.id ? null : x.id)}
              onDelete={(id) => void remove(id)} />
          ))}
          {rows === null && <tr><td className={tdCls} colSpan={cols}>{t('common.loading')}</td></tr>}
          {rows?.length === 0 && (
            <tr><td className={tdCls} colSpan={cols}>{t('economy.noneSupplier')}</td></tr>
          )}
        </tbody>
      </table>
    </Panel>
  );
}

function SupplierRow({ x, isResource, canGenerate, open, matchFor, statements,
  onToggle, onAct, onMatch, onMatchToggle, onDelete }: {
  x: SupplierInvoice; isResource: boolean; canGenerate: boolean;
  open: SupplierInvoiceDetail | null; matchFor: string | null;
  statements: SubcontractorStatement[];
  onToggle: (x: SupplierInvoice) => void;
  onAct: (id: string, a: 'approve' | 'pay') => void;
  onMatch: (statementId: string) => void;
  onMatchToggle: () => void;
  onDelete: (id: string) => void;
}) {
  const { t } = useTranslation();
  const [pick, setPick] = useState('');
  const expanded = open?.invoice.id === x.id;
  const matchable = x.status === 'registered' || x.status === 'discrepancy';
  // Only same-resource, same-currency statements are valid match targets.
  const candidates = statements.filter((s) => s.resourceId === x.resourceId && s.currency === x.currency);
  return (
    <>
      <tr className="border-b border-slate-100">
        {!isResource && <td className={tdCls}>{x.resourceName}</td>}
        <td className={tdCls}>{x.number}</td>
        <td className={tdCls}>{x.invoiceDate}</td>
        <td className={tdCls}>
          <span className={cn('rounded px-2 py-0.5 text-xs', supplierStatusCls[x.status])}>
            {t(`economy.supplierStatus.${x.status}`)}
          </span>
        </td>
        <td className={cn(tdCls, 'text-right font-medium')}>{money(x.totalAmount, x.currency)}</td>
        <td className={tdCls}>
          <span className="flex justify-end gap-1">
            <button className={btnSecondaryCls} onClick={() => onToggle(x)}>{t('approvals.expand')}</button>
            {canGenerate && matchable && (
              <button className={btnSecondaryCls} onClick={onMatchToggle}>{t('economy.match')}</button>
            )}
            {canGenerate && (x.status === 'matched' || x.status === 'discrepancy') && (
              <button className={btnSecondaryCls} onClick={() => onAct(x.id, 'approve')}>
                {t('approvals.approve')}
              </button>
            )}
            {canGenerate && x.status === 'approved' && (
              <button className={btnSecondaryCls} onClick={() => onAct(x.id, 'pay')}>
                {t('economy.step.pay')}
              </button>
            )}
            {canGenerate && matchable && (
              <button className={btnDangerCls} onClick={() => onDelete(x.id)}>{t('common.delete')}</button>
            )}
          </span>
        </td>
      </tr>
      {matchFor === x.id && (
        <tr className="border-b border-slate-100 bg-slate-50">
          <td colSpan={isResource ? 5 : 6} className="px-3 py-2">
            <div className="flex items-center gap-2 text-sm">
              <span>{t('economy.matchAgainst')}</span>
              <select className={cn(inputCls, 'w-64')} value={pick} onChange={(e) => setPick(e.target.value)}>
                <option value="">—</option>
                {candidates.map((s) => (
                  <option key={s.id} value={s.id}>
                    {monthKey(s.year, s.month)} · {money(s.totalAmount, s.currency)}
                  </option>
                ))}
              </select>
              <button className={btnCls} disabled={!pick} onClick={() => onMatch(pick)}>
                {t('economy.match')}
              </button>
              {candidates.length === 0 && (
                <span className="text-xs text-slate-500">{t('economy.noMatchCandidates')}</span>
              )}
            </div>
          </td>
        </tr>
      )}
      {expanded && open && (
        <tr className="border-b border-slate-200 bg-slate-50">
          <td colSpan={isResource ? 5 : 6} className="px-3 py-2">
            {open.invoice.approvalNote && (
              <p className="mb-2 text-xs text-slate-600">
                {t('economy.approvalNote', { note: open.invoice.approvalNote })}
              </p>
            )}
            <div className="grid gap-3 sm:grid-cols-2">
              <div>
                <h4 className="mb-1 text-xs font-semibold text-slate-500">{t('economy.lines')}</h4>
                <table className="w-full border-collapse">
                  <tbody>
                    {open.lines.map((l) => (
                      <tr key={l.id} className="border-b border-slate-100">
                        <td className={tdCls}>{l.description}</td>
                        <td className={cn(tdCls, 'text-xs text-slate-500')}>{t(`economy.kind.${l.kind}`)}</td>
                        <td className={cn(tdCls, 'text-right')}>{money(l.amount, x.currency)}</td>
                      </tr>
                    ))}
                    {open.lines.length === 0 && <tr><td className={tdCls}>—</td></tr>}
                  </tbody>
                </table>
              </div>
              <div>
                <h4 className="mb-1 text-xs font-semibold text-slate-500">{t('economy.diffs')}</h4>
                <table className="w-full border-collapse">
                  <tbody>
                    {(open.diffs ?? []).map((d) => (
                      <tr key={d.kind} className="border-b border-slate-100">
                        <td className={tdCls}>{t(`economy.kind.${d.kind}`)}</td>
                        <td className={cn(tdCls, 'text-right')}>{money(d.invoiced, x.currency)}</td>
                        <td className={cn(tdCls, 'text-right')}>{money(d.statement, x.currency)}</td>
                        <td className={cn(tdCls, 'text-right font-medium',
                          d.diff === 0 ? 'text-emerald-700' : 'text-amber-700')}>
                          {d.diff > 0 ? '+' : ''}{money(d.diff, x.currency)}
                        </td>
                      </tr>
                    ))}
                    {!(open.diffs?.length) && <tr><td className={tdCls}>—</td></tr>}
                  </tbody>
                </table>
              </div>
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

// ── Margins ─────────────────────────────────────────────────────────────────

function MarginsPanel() {
  const { t } = useTranslation();
  const [month, setMonth] = useState(currentMonth());
  const [rows, setRows] = useState<MarginRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  // Filter by effective end client — any organisation of the register.
  const [organisations, setOrganisations] = useState<CustomerSummary[]>([]);
  const [endClientId, setEndClientId] = useState('');
  useEffect(() => { getCustomers().then(setOrganisations).catch(() => setOrganisations([])); }, []);

  const reload = useCallback(() => {
    if (!validMonth(month)) return;
    const { year, month: m } = ymOf(month);
    setError(null);
    getMargins(year, m, endClientId || null).then(setRows)
      .catch((e) => { setRows([]); setError(describeError(e, t)); });
  }, [month, endClientId, t]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  return (
    <Panel title={t('economy.margins')}
      help={t('economy.marginsHelp')}
      actions={
        <span className="flex gap-2">
          <select className={cn(inputCls, 'w-56')} value={endClientId} title={t('economy.hEndClientFilter')}
            onChange={(e) => setEndClientId(e.target.value)}>
            <option value="">{t('economy.allEndClients')}</option>
            {organisations.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
          </select>
          <input className={inputCls} type="month" value={month}
            onChange={(e) => setMonth(e.target.value)} />
        </span>
      }>
      <ErrorBox message={error} />
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('economy.project')}</th>
            <th className={thCls}>{t('economy.customer')}</th>
            <th className={thCls}>{t('economy.endClient')}</th>
            <th className={cn(thCls, 'text-right')}>{t('economy.billed')}</th>
            <th className={cn(thCls, 'text-right')}>{t('economy.cost')}</th>
            <th className={cn(thCls, 'text-right')}>{t('economy.margin')}</th>
            <th className={cn(thCls, 'text-right')}>%</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={`${r.projectId}-${r.currency}`} className="border-b border-slate-100">
              <td className={tdCls}>{r.projectName}</td>
              <td className={tdCls}>{r.customerName}</td>
              <td className={tdCls}>{r.endClientName}</td>
              <td className={cn(tdCls, 'text-right')}>{money(r.billed, r.currency)}</td>
              <td className={cn(tdCls, 'text-right')} title={costTitle(r.costBreakdown, t)}>
                {money(r.cost, r.currency)}
              </td>
              <td className={cn(tdCls, 'text-right font-medium',
                r.margin < 0 ? 'text-red-600' : 'text-emerald-700')}>{money(r.margin, r.currency)}</td>
              <td className={cn(tdCls, 'text-right')}>{r.marginPercent != null ? `${r.marginPercent}%` : '—'}</td>
            </tr>
          ))}
          {rows.length === 0 && <tr><td className={tdCls} colSpan={7}>{t('economy.noneMargins')}</td></tr>}
        </tbody>
        {rows.length > 0 && (
          <tfoot>
            {marginTotalsByCurrency(rows).map((tot) => (
              <tr key={tot.currency} className="border-t border-slate-200 font-medium">
                <td className={tdCls} colSpan={3}>{t('economy.totalIn', { currency: tot.currency })}</td>
                <td className={cn(tdCls, 'text-right')}>{money(tot.billed, tot.currency)}</td>
                <td className={cn(tdCls, 'text-right')}>{money(tot.cost, tot.currency)}</td>
                <td className={cn(tdCls, 'text-right',
                  tot.margin < 0 ? 'text-red-600' : 'text-emerald-700')}>{money(tot.margin, tot.currency)}</td>
                <td className={tdCls}></td>
              </tr>
            ))}
          </tfoot>
        )}
      </table>
    </Panel>
  );
}

/** Cost breakdown as a tooltip — hours, expenses, per diem, mileage, travel time. */
function costTitle(c: CostBreakdown | null, t: (k: string) => string): string | undefined {
  if (!c) return undefined;
  const parts: [string, number][] = [
    ['kind.hours', c.hours], ['kind.expense', c.expenses], ['kind.allowance', c.allowances],
    ['kind.mileage', c.mileage], ['kind.travelTime', c.travelTime],
  ];
  return parts.filter(([, v]) => v !== 0)
    .map(([k, v]) => `${t(`economy.${k}`)}: ${money(v, c.currency)}`).join('\n') || undefined;
}

/** Generation warnings (mixed currencies, unreviewed rate cells, PAD without
 * a rate…) — the amounts they concern were left out of the total. */
function Warnings({ items }: { items: string[] }) {
  const { t } = useTranslation();
  if (items.length === 0) return null;
  return (
    <div className="mb-2 rounded border border-amber-200 bg-amber-50 px-3 py-1.5 text-xs text-amber-800">
      <p className="font-medium">{t('economy.warnings')}</p>
      <ul className="list-disc pl-4">
        {items.map((w, i) => (
          <li key={i}>
            {w}
            {isMissingRateWarning(w) && (
              <a href="#exchange-rates" className="ml-1 font-medium text-blue-700 hover:underline">
                {t('fx.missingLink')}
              </a>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}

/** Month-end sums per source currency and their conversion (before mark-up). */
function CurrencySummaryTable({ rows, currency }: { rows?: CurrencySummary[] | null; currency: string }) {
  const { t } = useTranslation();
  if (!rows || rows.length === 0) return null;
  return (
    <div className="mb-2 rounded border border-slate-200 bg-white px-3 py-1.5 text-xs">
      <p className="font-medium text-slate-600">{t('fx.summary')}</p>
      <table className="mt-1">
        <thead>
          <tr className="text-slate-500">
            <th className="pr-4 text-left font-normal">{t('fx.original')}</th>
            <th className="pr-4 text-right font-normal">{t('fx.rate')}</th>
            <th className="text-right font-normal">{t('fx.converted')}</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.currency}>
              <td className="pr-4">{money(r.originalTotal, r.currency)}</td>
              <td className="pr-4 text-right">{r.rate}</td>
              <td className="text-right">{money(r.convertedTotal, currency)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function LineTable({ lines, currency }: { lines: BillingLine[]; currency: string }) {
  const { t } = useTranslation();
  return (
    <table className="w-full border-collapse">
      <thead>
        <tr className="border-b border-slate-200">
          <th className={thCls}>{t('myWork.date')}</th>
          <th className={thCls}>{t('myWork.description')}</th>
          <th className={thCls}>{t('economy.typeLabel')}</th>
          <th className={cn(thCls, 'text-right')}>{t('economy.qty')}</th>
          <th className={cn(thCls, 'text-right')}>{t('economy.unitPrice')}</th>
          <th className={cn(thCls, 'text-right')}>{t('economy.amount')}</th>
        </tr>
      </thead>
      <tbody>
        {lines.map((l) => (
          <tr key={l.id} className="border-b border-slate-100 align-top">
            <td className={tdCls}>{l.date ?? '—'}</td>
            <td className={tdCls}>
              {l.description}
              {l.explanation && (
                <div className="text-xs text-slate-400">{l.explanation}</div>
              )}
              {originalLabel(l) && (
                <div className="text-xs text-slate-500">{t('fx.original')}: {originalLabel(l)}</div>
              )}
            </td>
            <td className={cn(tdCls, 'text-xs text-slate-500')}>{t(`economy.kind.${l.kind}`)}</td>
            <td className={cn(tdCls, 'text-right')}>{l.quantity}</td>
            <td className={cn(tdCls, 'text-right')}>{l.unitPrice}</td>
            <td className={cn(tdCls, 'text-right')}>{money(l.amount, currency)}</td>
          </tr>
        ))}
        {lines.length === 0 && <tr><td className={tdCls} colSpan={6}>—</td></tr>}
      </tbody>
    </table>
  );
}
