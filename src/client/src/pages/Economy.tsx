import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  deleteInvoice, deleteStatement, deleteSupplierInvoice, errorMessage,
  generateInvoice, generateStatement,
  getInvoice, getInvoices, getMargins, getProjects, getResources, getStatement,
  getStatements, getSupplierInvoice, getSupplierInvoices,
  invoiceAction, matchSupplierInvoice, registerSupplierInvoice,
  statementAction, supplierInvoiceAction,
} from '@/lib/api';
import { useMe } from '@/lib/useMe';
import type {
  BillingLine, InvoiceDetail, InvoiceSpecification, InvoiceStatus, MarginRow,
  ProjectSummary, ResourceSummary, StatementDetail, StatementStatus,
  SubcontractorStatement, SupplierInvoice, SupplierInvoiceDetail, SupplierInvoiceStatus,
} from '@/lib/types';
import { btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, Panel, tdCls, thCls } from '@/components/forms';
import { cn } from '@/lib/utils';

const currentMonth = () => new Date().toISOString().slice(0, 7);
const money = (n: number, ccy: string) =>
  `${n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${ccy}`;

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
 * generated from the same approved timesheets but never sharing a rate. */
export function Economy() {
  const me = useMe();
  const isResource = me?.role === 'Resource';
  const canGenerate = me?.role === 'Administrator' || me?.role === 'Finance';

  return (
    <div className="mx-auto max-w-5xl space-y-4 px-4 py-6">
      {!isResource && <InvoicePanel canGenerate={canGenerate} />}
      <StatementPanel canGenerate={canGenerate} isResource={isResource ?? false} />
      <SupplierInvoicePanel canGenerate={canGenerate} isResource={isResource ?? false} />
      {!isResource && <MarginsPanel />}
    </div>
  );
}

// ── Customer invoice specifications ─────────────────────────────────────────

function InvoicePanel({ canGenerate }: { canGenerate: boolean }) {
  const { t } = useTranslation();
  const [projects, setProjects] = useState<ProjectSummary[]>([]);
  const [projectId, setProjectId] = useState('');
  const [month, setMonth] = useState(currentMonth());
  const [rows, setRows] = useState<InvoiceSpecification[]>([]);
  const [open, setOpen] = useState<InvoiceDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { getProjects().then(setProjects).catch(() => {}); }, []);
  const reload = useCallback(() => { getInvoices().then(setRows).catch(() => {}); }, []);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  const generate = async () => {
    setError(null);
    try {
      await generateInvoice(projectId, Number(month.slice(0, 4)), Number(month.slice(5)));
      reload();
    } catch (e) { setError(errorMessage(e)); }
  };
  const act = async (id: string, action: 'review' | 'finalize' | 'issue' | 'pay') => {
    setError(null);
    try { await invoiceAction(id, action); reload(); }
    catch (e) { setError(errorMessage(e)); }
  };
  const toggle = async (s: InvoiceSpecification) => {
    if (open?.invoice.id === s.id) { setOpen(null); return; }
    setOpen(await getInvoice(s.id).catch(() => null));
  };

  const next: Record<InvoiceStatus, 'review' | 'finalize' | 'issue' | 'pay' | null> = {
    draft: 'review', customerReview: 'finalize', final: 'issue', issued: 'pay', paid: null,
  };

  return (
    <Panel title={t('economy.invoices')} help={t('economy.invoicesHelp')}>
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
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
          <button className={btnCls} disabled={!projectId} onClick={() => void generate()}>
            {t('economy.generate')}
          </button>
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
          {rows.map((s) => {
            const expanded = open?.invoice.id === s.id;
            const step = next[s.status];
            return (
              <InvoiceRow key={s.id} s={s} expanded={expanded} open={open}
                step={step} canGenerate={canGenerate}
                onToggle={toggle} onAct={act}
                onDelete={(id) => void deleteInvoice(id).then(reload).catch((e) => setError(errorMessage(e)))} />
            );
          })}
          {rows.length === 0 && <tr><td className={tdCls} colSpan={6}>{t('economy.none')}</td></tr>}
        </tbody>
      </table>
    </Panel>
  );
}

function InvoiceRow({ s, expanded, open, step, canGenerate, onToggle, onAct, onDelete }: {
  s: InvoiceSpecification; expanded: boolean; open: InvoiceDetail | null;
  step: 'review' | 'finalize' | 'issue' | 'pay' | null;
  canGenerate: boolean;
  onToggle: (s: InvoiceSpecification) => void;
  onAct: (id: string, a: 'review' | 'finalize' | 'issue' | 'pay') => void;
  onDelete: (id: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <>
      <tr className="border-b border-slate-100">
        <td className={tdCls}>{s.projectName}</td>
        <td className={tdCls}>{s.year}-{String(s.month).padStart(2, '0')}</td>
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
          <td colSpan={6} className="px-3 py-2"><LineTable lines={open.lines} currency={s.currency} /></td>
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
  const [rows, setRows] = useState<SubcontractorStatement[]>([]);
  const [open, setOpen] = useState<StatementDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { if (!isResource) getResources().then(setResources).catch(() => {}); }, [isResource]);
  const reload = useCallback(() => { getStatements().then(setRows).catch(() => {}); }, []);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  const generate = async () => {
    setError(null);
    try {
      await generateStatement(resourceId, Number(month.slice(0, 4)), Number(month.slice(5)));
      reload();
    } catch (e) { setError(errorMessage(e)); }
  };
  const act = async (id: string, action: 'finalize' | 'pay') => {
    setError(null);
    try { await statementAction(id, action); reload(); }
    catch (e) { setError(errorMessage(e)); }
  };
  const toggle = async (s: SubcontractorStatement) => {
    if (open?.statement.id === s.id) { setOpen(null); return; }
    setOpen(await getStatement(s.id).catch(() => null));
  };

  return (
    <Panel title={t('economy.statements')} help={t('economy.statementsHelp')}>
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
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
          <button className={btnCls} disabled={!resourceId} onClick={() => void generate()}>
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
          {rows.map((s) => {
            const expanded = open?.statement.id === s.id;
            const step: 'finalize' | 'pay' | null =
              s.status === 'draft' ? 'finalize' : s.status === 'final' ? 'pay' : null;
            return (
              <StatementRow key={s.id} s={s} expanded={expanded} open={open}
                step={step} canGenerate={canGenerate} isResource={isResource}
                onToggle={toggle} onAct={act}
                onDelete={(id) => void deleteStatement(id).then(reload).catch((e) => setError(errorMessage(e)))} />
            );
          })}
          {rows.length === 0 && (
            <tr><td className={tdCls} colSpan={isResource ? 5 : 6}>{t('economy.noneStatements')}</td></tr>
          )}
        </tbody>
      </table>
    </Panel>
  );
}

function StatementRow({ s, expanded, open, step, canGenerate, isResource, onToggle, onAct, onDelete }: {
  s: SubcontractorStatement; expanded: boolean; open: StatementDetail | null;
  step: 'finalize' | 'pay' | null; canGenerate: boolean; isResource: boolean;
  onToggle: (s: SubcontractorStatement) => void;
  onAct: (id: string, a: 'finalize' | 'pay') => void;
  onDelete: (id: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <>
      <tr className="border-b border-slate-100">
        {!isResource && <td className={tdCls}>{s.resourceName}</td>}
        <td className={tdCls}>{s.year}-{String(s.month).padStart(2, '0')}</td>
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
  const [rows, setRows] = useState<SupplierInvoice[]>([]);
  const [open, setOpen] = useState<SupplierInvoiceDetail | null>(null);
  const [error, setError] = useState<string | null>(null);
  const empty = { resourceId: '', number: '', invoiceDate: '', totalAmount: '', linesText: '' };
  const [form, setForm] = useState(empty);
  const [formOpen, setFormOpen] = useState(false);
  const [matchFor, setMatchFor] = useState<string | null>(null);

  useEffect(() => {
    if (!isResource) getResources().then(setResources).catch(() => {});
  }, [isResource]);
  const reload = useCallback(() => {
    getSupplierInvoices().then(setRows).catch(() => {});
    getStatements({ status: 'final' }).then(setStatements).catch(() => {});
  }, []);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  /** Parses "kind amount per line" — e.g. "hours 4800" — into register lines. */
  const parseLines = (text: string) => text.split('\n')
    .map((l) => l.trim()).filter(Boolean)
    .map((l) => {
      const [kind, amount, ...rest] = l.split(/\s+/);
      return { kind, amount: Number(amount), description: rest.join(' ') || kind };
    });

  const register = async () => {
    setError(null);
    try {
      await registerSupplierInvoice({
        resourceId: form.resourceId, number: form.number, invoiceDate: form.invoiceDate,
        currency: 'SEK', totalAmount: Number(form.totalAmount),
        lines: form.linesText.trim() ? parseLines(form.linesText) : undefined,
      });
      setForm(empty); setFormOpen(false); reload();
    } catch (e) { setError(errorMessage(e)); }
  };
  const toggle = async (x: SupplierInvoice) => {
    if (open?.invoice.id === x.id) { setOpen(null); return; }
    setOpen(await getSupplierInvoice(x.id).catch(() => null));
  };
  const act = async (id: string, action: 'approve' | 'pay') => {
    setError(null);
    try { await supplierInvoiceAction(id, action); reload(); }
    catch (e) { setError(errorMessage(e)); }
  };

  return (
    <Panel title={t('economy.supplierInvoices')}
      help={t('economy.supplierHelp')}
      actions={canGenerate && (
        <button className={btnSecondaryCls} onClick={() => setFormOpen((v) => !v)}>
          {t('economy.registerInvoice')}
        </button>
      )}>
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}

      {formOpen && (
        <div className="mb-3 rounded border border-slate-200 bg-slate-50 p-3">
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
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
            <Field label={t('economy.total')}>
              <input className={inputCls} type="number" min="0" step="0.01" value={form.totalAmount}
                onChange={(e) => setForm({ ...form, totalAmount: e.target.value })} />
            </Field>
          </div>
          <Field label={`${t('economy.lines')} (${t('economy.linesHint')})`}>
            <textarea className={cn(inputCls, 'mt-1 h-20 font-mono text-xs')}
              placeholder={'hours 4800\nexpense 1200 Cable\nmileage 2500\ntravelTime 600\nallowance 350'}
              value={form.linesText} onChange={(e) => setForm({ ...form, linesText: e.target.value })} />
          </Field>
          <div className="mt-2 flex gap-2">
            <button className={btnCls}
              disabled={!form.resourceId || !form.number || !form.invoiceDate || !form.totalAmount}
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
          {rows.map((x) => (
            <SupplierRow key={x.id} x={x} isResource={isResource} canGenerate={canGenerate}
              open={open} matchFor={matchFor} statements={statements}
              onToggle={toggle} onAct={act}
              onMatch={async (statementId) => {
                setError(null);
                try { setOpen(await matchSupplierInvoice(x.id, statementId)); setMatchFor(null); reload(); }
                catch (e) { setError(errorMessage(e)); }
              }}
              onMatchToggle={() => setMatchFor(matchFor === x.id ? null : x.id)}
              onDelete={(id) => void deleteSupplierInvoice(id).then(reload).catch((e) => setError(errorMessage(e)))} />
          ))}
          {rows.length === 0 && (
            <tr><td className={tdCls} colSpan={isResource ? 5 : 6}>{t('economy.noneSupplier')}</td></tr>
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
  const expanded = open?.invoice.id === x.id;
  const matchable = x.status === 'registered' || x.status === 'discrepancy';
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
              <select className={inputCls} defaultValue=""
                onChange={(e) => e.target.value && onMatch(e.target.value)}>
                <option value="">—</option>
                {statements.filter((s) => s.resourceId === x.resourceId).map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.year}-{String(s.month).padStart(2, '0')} · {money(s.totalAmount, s.currency)}
                  </option>
                ))}
              </select>
            </div>
          </td>
        </tr>
      )}
      {expanded && open && (
        <tr className="border-b border-slate-200 bg-slate-50">
          <td colSpan={isResource ? 5 : 6} className="px-3 py-2">
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

  const reload = useCallback(() => {
    const [y, m] = month.split('-').map(Number);
    getMargins(y, m).then(setRows).catch((e) => setError(errorMessage(e)));
  }, [month]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  return (
    <Panel title={t('economy.margins')}
      help={t('economy.marginsHelp')}
      actions={<input className={inputCls} type="month" value={month}
        onChange={(e) => setMonth(e.target.value)} />}>
      {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('economy.project')}</th>
            <th className={thCls}>{t('economy.customer')}</th>
            <th className={cn(thCls, 'text-right')}>{t('economy.billed')}</th>
            <th className={cn(thCls, 'text-right')}>{t('economy.cost')}</th>
            <th className={cn(thCls, 'text-right')}>{t('economy.margin')}</th>
            <th className={cn(thCls, 'text-right')}>%</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.projectId} className="border-b border-slate-100">
              <td className={tdCls}>{r.projectName}</td>
              <td className={tdCls}>{r.customerName}</td>
              <td className={cn(tdCls, 'text-right')}>{r.billed}</td>
              <td className={cn(tdCls, 'text-right')}>{r.cost}</td>
              <td className={cn(tdCls, 'text-right font-medium',
                r.margin < 0 ? 'text-red-600' : 'text-emerald-700')}>{r.margin}</td>
              <td className={cn(tdCls, 'text-right')}>{r.marginPercent != null ? `${r.marginPercent}%` : '—'}</td>
            </tr>
          ))}
          {rows.length === 0 && <tr><td className={tdCls} colSpan={6}>{t('economy.noneMargins')}</td></tr>}
        </tbody>
      </table>
    </Panel>
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
            </td>
            <td className={cn(tdCls, 'text-xs text-slate-500')}>{t(`economy.kind.${l.kind}`)}</td>
            <td className={cn(tdCls, 'text-right')}>{l.quantity}</td>
            <td className={cn(tdCls, 'text-right')}>{l.unitPrice}</td>
            <td className={cn(tdCls, 'text-right')}>{money(l.amount, currency)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
