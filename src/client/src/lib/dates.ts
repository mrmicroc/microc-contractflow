// Local-calendar date helpers. `new Date().toISOString()` is UTC — in Sweden
// between 00:00 and 02:00 it still says yesterday — so "today" and "this
// month" always come from the local clock here.

const pad2 = (n: number) => String(n).padStart(2, '0');

/** Today's date in the browser's local time zone, as yyyy-mm-dd. */
export function localTodayIso(now: Date = new Date()): string {
  return `${now.getFullYear()}-${pad2(now.getMonth() + 1)}-${pad2(now.getDate())}`;
}

/** The current local month, `{ year, month }` with month 1–12. */
export function localMonth(now: Date = new Date()): { year: number; month: number } {
  return { year: now.getFullYear(), month: now.getMonth() + 1 };
}

/** yyyy-mm for a year/month pair. */
export const monthKey = (year: number, month: number) => `${year}-${pad2(month)}`;

/** Number of days in a month (month 1–12), leap years included. */
export function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

/** First and last ISO date of a month. */
export function monthRange(year: number, month: number): { from: string; to: string } {
  return {
    from: `${monthKey(year, month)}-01`,
    to: `${monthKey(year, month)}-${pad2(daysInMonth(year, month))}`,
  };
}

/** `{year, month}` shifted by `delta` months. */
export function shiftMonth(year: number, month: number, delta: number): { year: number; month: number } {
  const idx = year * 12 + (month - 1) + delta;
  return { year: Math.floor(idx / 12), month: (idx % 12 + 12) % 12 + 1 };
}
