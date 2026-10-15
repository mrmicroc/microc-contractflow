import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  deleteInvoice, deleteStatement, errorMessage, generateInvoice, generateStatement,
  getInvoice, getInvoices, getProjects, getResources, getStatement, getStatements,
  invoiceAction, statementAction,
} from '@/lib/api';
import { useMe } from '@/lib/useMe';
import type {
  BillingLine, InvoiceDetail, InvoiceSpecification, InvoiceStatus,
  ProjectSummary, ResourceSummary, StatementDetail, StatementStatus, SubcontractorStatement,
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
    <Panel title={t('economy.invoices')}>
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
    <Panel title={t('economy.statements')}>
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
