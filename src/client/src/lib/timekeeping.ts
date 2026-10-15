const DAY_MS = 86_400_000;

/** Monday of the ISO week containing `iso` (YYYY-MM-DD). */
export function isoWeekMonday(iso: string): string {
  const d = new Date(`${iso}T00:00:00Z`);
  const dow = d.getUTCDay() || 7; // Sunday → 7
  return new Date(d.getTime() - (dow - 1) * DAY_MS).toISOString().slice(0, 10);
}

/** ISO-8601 year + week for a date (weeks start Monday; the week's Thursday
 * decides the ISO year). */
export function isoWeekOf(iso: string): { year: number; week: number } {
  const monday = new Date(`${isoWeekMonday(iso)}T00:00:00Z`);
  const thursday = new Date(monday.getTime() + 3 * DAY_MS);
  const year = thursday.getUTCFullYear();
  // Jan 4 is always inside ISO week 1 — Jan 1 may belong to the previous year.
  const jan4 = Date.UTC(year, 0, 4);
  const firstMonday = new Date(`${isoWeekMonday(new Date(jan4).toISOString().slice(0, 10))}T00:00:00Z`);
  const week = Math.round((monday.getTime() - firstMonday.getTime()) / (7 * DAY_MS)) + 1;
  return { year, week };
}

/** Monday of `year`'s ISO week `week` — inverse of isoWeekOf. */
export function isoWeekStart(year: number, week: number): string {
  const jan4 = Date.UTC(year, 0, 4); // Jan 4 is always in ISO week 1
  const monday = new Date(`${isoWeekMonday(new Date(jan4).toISOString().slice(0, 10))}T00:00:00Z`);
  return new Date(monday.getTime() + (week - 1) * 7 * DAY_MS).toISOString().slice(0, 10);
}

/** All seven dates of an ISO week, Monday→Sunday. */
export function isoWeekDates(year: number, week: number): string[] {
  const monday = new Date(`${isoWeekStart(year, week)}T00:00:00Z`);
  return Array.from({ length: 7 }, (_, i) =>
    new Date(monday.getTime() + i * DAY_MS).toISOString().slice(0, 10));
}

/** "2026-W42"-style label. */
export const isoWeekLabel = (year: number, week: number) =>
  `${year}-W${String(week).padStart(2, '0')}`;
