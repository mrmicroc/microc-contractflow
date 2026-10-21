// Baseline schedule grid (contract §2: baseline per project and month, e.g.
// years × Jan–Dec) ⇄ the PUT /api/projects/{id}/pad/baseline-schedule body.
// Pure so totals, the contracted-total check and "only changed months are
// sent" are unit-tested.
import { isInvalidDecimal } from './validation';
import { numOrNull } from './versioned';
import type { BaselineMonth, BaselineSchedule } from './types';

export const MONTHS = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12] as const;

/** Cell values keyed "YYYY-M" — strings while editing. */
export interface ScheduleGrid {
  years: number[];
  cells: Record<string, string>;
}

export const cellKey = (year: number, month: number) => `${year}-${month}`;

/** Grid from the stored schedule; with no months yet it opens on `fallbackYear`. */
export function gridFromSchedule(s: BaselineSchedule | null, fallbackYear: number): ScheduleGrid {
  const cells: Record<string, string> = {};
  for (const m of s?.months ?? []) cells[cellKey(m.year, m.month)] = String(m.amount);
  const years = [...new Set((s?.months ?? []).map((m) => m.year))].sort((a, b) => a - b);
  return { years: years.length ? years : [fallbackYear], cells };
}

/** Adds the year after the last one (or before the first with `before`). */
export function addYear(g: ScheduleGrid, before = false): ScheduleGrid {
  const year = before ? Math.min(...g.years) - 1 : Math.max(...g.years) + 1;
  return { ...g, years: [...g.years, year].sort((a, b) => a - b) };
}

const value = (g: ScheduleGrid, y: number, m: number) => numOrNull(g.cells[cellKey(y, m)] ?? '') ?? 0;

export const yearTotal = (g: ScheduleGrid, year: number) =>
  round4(MONTHS.reduce((sum, m) => sum + value(g, year, m), 0));

export const grandTotal = (g: ScheduleGrid) => round4(g.years.reduce((sum, y) => sum + yearTotal(g, y), 0));

/** contracted − scheduled (null without a contracted total). */
export const scheduleDifference = (contracted: number | null | undefined, total: number) =>
  contracted === null || contracted === undefined ? null : round4(contracted - total);

/** Keys of cells holding something that isn't a number ≥ 0. */
export function invalidCells(g: ScheduleGrid): string[] {
  return Object.entries(g.cells)
    .filter(([, v]) => v.trim() && (isInvalidDecimal(v) || (numOrNull(v) ?? 0) < 0))
    .map(([k]) => k);
}

/** Months whose amount changed against the stored schedule — a cleared
 * cell that held a value is sent as 0; untouched months are left out. */
export function changedMonths(g: ScheduleGrid, original: BaselineSchedule | null): BaselineMonth[] {
  const before = new Map((original?.months ?? []).map((m) => [cellKey(m.year, m.month), m.amount]));
  const out: BaselineMonth[] = [];
  for (const y of g.years) {
    for (const m of MONTHS) {
      const k = cellKey(y, m);
      const raw = g.cells[k];
      const now = raw === undefined || !raw.trim() ? null : numOrNull(raw);
      const prev = before.get(k) ?? null;
      if (now === null && prev === null) continue;
      if ((now ?? 0) !== (prev ?? 0)) out.push({ year: y, month: m, amount: now ?? 0 });
    }
  }
  return out;
}

/** "YYYY-MM" codes from a 409 monthClosed body → grid keys (read-only cells). */
export const closedKeys = (months: unknown): Set<string> =>
  new Set((Array.isArray(months) ? months : [])
    .filter((x): x is string => typeof x === 'string' && /^\d{4}-\d{2}$/.test(x))
    .map((x) => cellKey(Number(x.slice(0, 4)), Number(x.slice(5, 7)))));

function round4(n: number) {
  return Math.round(n * 10000) / 10000;
}
