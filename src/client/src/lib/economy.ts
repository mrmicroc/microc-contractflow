// Billing-document workflow helpers — pure, mirrored from the server's
// status flows (InvoicesController / StatementsController).
import { parseDecimal } from './validation';
import type { InvoiceStatus, StatementLineKind, StatementStatus } from './types';

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
