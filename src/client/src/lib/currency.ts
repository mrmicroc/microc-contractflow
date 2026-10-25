import { parseDecimal } from './validation';
import type { BillingLine, ExpenseCategory, ExpenseMarkupRow, ProjectSummary } from './types';

/** A typed number (comma or point decimals), or null when blank/invalid. */
const decimalOrNull = (raw: string): number | null => {
  const p = parseDecimal(raw);
  return p.ok ? p.value : null;
};

/** Fallback when neither the project's contract nor anything else names one. */
export const SYSTEM_DEFAULT_CURRENCY = 'SEK';

/** ISO 4217 shape: three letters. */
export const isCurrencyCode = (s: string) => /^[A-Za-z]{3}$/.test(s.trim());

/** The currency an expense / per-diem form preselects: the chosen project's
 * contract currency, else the fallback (agreement or system default). */
export function defaultCurrencyFor(
  projectId: string, projects: Pick<ProjectSummary, 'id' | 'currency'>[], fallback = SYSTEM_DEFAULT_CURRENCY,
): string {
  return projects.find((p) => p.id === projectId)?.currency ?? fallback;
}

/** "1 200 EUR × 11.5" for a converted line; null when the line was already
 * in the document currency. */
export function originalLabel(line: Pick<BillingLine, 'originalAmount' | 'originalCurrency' | 'exchangeRate'>): string | null {
  if (line.originalAmount == null || !line.originalCurrency) return null;
  const rate = line.exchangeRate != null ? ` × ${Number(line.exchangeRate.toFixed(6))}` : '';
  return `${line.originalAmount} ${line.originalCurrency}${rate}`;
}

/** True when a generation warning concerns a missing month rate. */
export const isMissingRateWarning = (w: string) => /^No [A-Z]{3}→[A-Z]{3} rate for \d{4}-\d{2}/.test(w);

export interface RateForm {
  year: string;
  month: string;
  fromCurrency: string;
  toCurrency: string;
  rate: string;
  note: string;
}

export interface RateBody {
  year: number;
  month: number;
  fromCurrency: string;
  toCurrency: string;
  rate: number;
  note: string | null;
}

/** Validates the rate form → request body, or the i18n key of the first problem. */
export function rateFormToBody(f: RateForm): { body?: RateBody; error?: string } {
  const year = Number(f.year);
  const month = Number(f.month);
  if (!Number.isInteger(year) || year < 2000 || year > 2100) return { error: 'fx.errYear' };
  if (!Number.isInteger(month) || month < 1 || month > 12) return { error: 'fx.errMonth' };
  if (!isCurrencyCode(f.fromCurrency) || !isCurrencyCode(f.toCurrency)) return { error: 'fx.errCurrency' };
  const from = f.fromCurrency.trim().toUpperCase();
  const to = f.toCurrency.trim().toUpperCase();
  if (from === to) return { error: 'fx.errSame' };
  const rate = decimalOrNull(f.rate);
  if (rate === null || rate <= 0) return { error: 'fx.errRate' };
  return { body: { year, month, fromCurrency: from, toCurrency: to, rate, note: f.note.trim() || null } };
}

/** Per-category mark-up rows as edited (percent as typed). */
export interface MarkupFormRow {
  category: ExpenseCategory;
  percent: string;
}

/** Form rows → request rows; error = i18n key when a percent is invalid or a
 * category repeats. Blank percents are dropped (the default applies). */
export function markupRowsToBody(rows: MarkupFormRow[]): { rows?: ExpenseMarkupRow[]; error?: string } {
  const out: ExpenseMarkupRow[] = [];
  for (const r of rows) {
    if (r.percent.trim() === '') continue;
    const percent = decimalOrNull(r.percent);
    if (percent === null || percent < 0 || percent > 1000) return { error: 'terms.errMarkupPercent' };
    if (out.some((o) => o.category === r.category)) return { error: 'terms.errMarkupDuplicate' };
    out.push({ category: r.category, percent });
  }
  return { rows: out };
}

/** Every expense category, in the order the forms list them. */
export const EXPENSE_CATEGORIES: ExpenseCategory[] =
  ['material', 'travel', 'accommodation', 'meal', 'equipment', 'other'];

/** First category without an override yet — what a new mark-up row starts as. */
export const nextFreeCategory = (rows: { category: ExpenseCategory }[]): ExpenseCategory =>
  EXPENSE_CATEGORIES.find((c) => !rows.some((r) => r.category === c)) ?? 'other';

/** Stored overrides → editable rows. */
export const markupRowsFrom = (rows: ExpenseMarkupRow[] | null | undefined): MarkupFormRow[] =>
  (rows ?? []).map((r) => ({ category: r.category, percent: String(r.percent) }));
