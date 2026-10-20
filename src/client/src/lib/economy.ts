// Billing-document workflow helpers — pure, mirrored from the server's
// status flows (InvoicesController / StatementsController).
import { parseDecimal } from './validation';
import type {
  InvoiceSpecification, InvoiceStatus, MarginRow, StatementLineKind, StatementStatus,
} from './types';

export type InvoiceStep = 'review' | 'finalize' | 'issue' | 'pay';
export type StatementStep = 'finalize' | 'pay';

/** draft → customerReview → final → issued → paid. */
export function nextInvoiceStep(s: InvoiceStatus): InvoiceStep | null {
  switch (s) {
    case 'draft': return 'review';
    case 'customerReview': return 'finalize';
    case 'final': return 'issue';
    case 'issued': return 'pay';
    default: return null;
  }
}

/** draft → final → paid. */
export function nextStatementStep(s: StatementStatus): StatementStep | null {
  return s === 'draft' ? 'finalize' : s === 'final' ? 'pay' : null;
}

/** A spec under customer review can be pulled back to draft for rework. */
export const canBackToDraft = (s: InvoiceStatus) => s === 'customerReview';

export type GenerateMode = 'new' | 'regenerate' | 'supplement';

/**
 * What "generate" means for a project-month given its existing specs: no
 * spec yet → a new one; the latest revision still a draft → rebuild it;
 * otherwise the month is frozen and only a supplement (bills what was
 * approved since) is possible.
 */
export function generateMode(
  specs: InvoiceSpecification[], projectId: string, year: number, month: number,
): GenerateMode {
  const mine = specs.filter((s) => s.projectId === projectId && s.year === year && s.month === month);
  if (mine.length === 0) return 'new';
  const latest = mine.reduce((a, b) => (b.revision > a.revision ? b : a));
  return latest.status === 'draft' ? 'regenerate' : 'supplement';
}

export interface CurrencyTotals { currency: string; billed: number; cost: number; margin: number }

/** Margin totals per currency — amounts in different currencies are never
 * added together (the server returns one row per project and currency). */
export function marginTotalsByCurrency(rows: MarginRow[]): CurrencyTotals[] {
  const byCcy = new Map<string, CurrencyTotals>();
  for (const r of rows) {
    const t = byCcy.get(r.currency) ?? { currency: r.currency, billed: 0, cost: 0, margin: 0 };
    t.billed += r.billed; t.cost += r.cost; t.margin += r.margin;
    byCcy.set(r.currency, t);
  }
  const round = (n: number) => Math.round(n * 100) / 100;
  return [...byCcy.values()]
    .map((t) => ({ ...t, billed: round(t.billed), cost: round(t.cost), margin: round(t.margin) }))
    .sort((a, b) => a.currency.localeCompare(b.currency));
}

/** A supplier invoice in discrepancy can only be approved with a note. */
export const approvalNeedsNote = (status: string) => status === 'discrepancy';

/** Steps that can't be undone from the UI — they ask for confirmation. */
export const IRREVERSIBLE_STEPS: ReadonlySet<string> = new Set(['finalize', 'issue', 'pay']);

export const SUPPLIER_LINE_KINDS: StatementLineKind[] =
  ['hours', 'expense', 'allowance', 'mileage', 'travelTime', 'manual'];

export interface ParsedSupplierLine { kind: StatementLineKind; amount: number; description: string }

export type SupplierLinesResult =
  | { ok: true; lines: ParsedSupplierLine[] }
  | { ok: false; errors: { line: number; reason: 'kind' | 'amount' }[] };

/**
 * Parses the supplier-invoice line box — one "kind amount [description]" per
 * line, e.g. "hours 4800,50 March work". Swedish decimal commas accepted;
 * unknown kinds and bad amounts are reported by (1-based) line number instead
 * of failing opaquely on the server.
 */
export function parseSupplierLines(text: string): SupplierLinesResult {
  const lines: ParsedSupplierLine[] = [];
  const errors: { line: number; reason: 'kind' | 'amount' }[] = [];
  text.split('\n').forEach((raw, i) => {
    const l = raw.trim();
    if (!l) return;
    const [kindRaw, amountRaw = '', ...rest] = l.split(/\s+/);
    const kind = SUPPLIER_LINE_KINDS.find((k) => k.toLowerCase() === kindRaw.toLowerCase());
    if (!kind) { errors.push({ line: i + 1, reason: 'kind' }); return; }
    const amount = parseDecimal(amountRaw);
    if (!amount.ok || amount.value === null) { errors.push({ line: i + 1, reason: 'amount' }); return; }
    lines.push({ kind, amount: amount.value, description: rest.join(' ') || kind });
  });
  return errors.length ? { ok: false, errors } : { ok: true, lines };
}
