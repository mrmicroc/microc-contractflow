// Shared booked day (contract §6): one inspector-day split across projects
// of the same contract. Pure helpers for the booking form, the completion
// dialog (actual hours per project) and the server's PAD responses.
import { numOrNull } from './versioned';
import type { BookingShareRequest, ProjectSummary } from './types';

/** An extra project of the day in the form — hours as typed. */
export interface ShareRow { projectId: string; hours: string }

/** Projects a day can be shared with: active, same contract, not the primary. */
export function shareProjectOptions(projects: ProjectSummary[], primaryProjectId: string): ProjectSummary[] {
  const primary = projects.find((p) => p.id === primaryProjectId);
  if (!primary) return [];
  return projects.filter((p) => p.isActive && p.contractId === primary.contractId && p.id !== primary.id);
}

/**
 * Shares + total planned hours for the request. No extra rows → a plain
 * single-project booking (empty shares). Otherwise the primary project's
 * hours lead, rows without a project or positive hours drop out, and a
 * project picked twice sums. Returns null when a typed value isn't a number.
 */
export function sharesBody(
  primaryProjectId: string, primaryHours: string, rows: ShareRow[],
): { shares: BookingShareRequest[]; plannedHours: number } | null {
  const primary = numOrNull(primaryHours);
  if (primary === null || primary <= 0) return null;
  const extra = rows.filter((r) => r.projectId && r.hours.trim());
  if (extra.some((r) => (numOrNull(r.hours) ?? 0) <= 0)) return null;
  if (extra.length === 0) return { shares: [], plannedHours: primary };
  const sums = new Map<string, number>([[primaryProjectId, primary]]);
  for (const r of extra) sums.set(r.projectId, (sums.get(r.projectId) ?? 0) + (numOrNull(r.hours) ?? 0));
  const shares = [...sums].map(([projectId, plannedHours]) => ({ projectId, plannedHours }));
  return { shares, plannedHours: shares.reduce((s, x) => s + x.plannedHours, 0) };
}

/** Actual hours per project for /complete — blank fields are left out;
 * null when any typed value isn't a non-negative number. */
export function actualHoursBody(byProject: Record<string, string>): Record<string, number> | null {
  const out: Record<string, number> = {};
  for (const [projectId, raw] of Object.entries(byProject)) {
    if (!raw.trim()) continue;
    const v = numOrNull(raw);
    if (v === null || v < 0) return null;
    out[projectId] = v;
  }
  return out;
}

export interface Shortfall { projectId: string; freePad: number; neededPad: number; shortfallPad: number }

/** The insufficientPad body's per-project shortfalls (shared day), else []. */
export function shortfallsOf(body: Record<string, unknown> | null | undefined): Shortfall[] {
  const raw = body?.shortfalls;
  if (!Array.isArray(raw)) return [];
  return raw
    .filter((x): x is Record<string, unknown> => !!x && typeof x === 'object')
    .map((x) => ({
      projectId: String(x.projectId ?? ''),
      freePad: Number(x.freePad ?? 0),
      neededPad: Number(x.neededPad ?? 0),
      shortfallPad: Number(x.shortfallPad ?? 0),
    }))
    .filter((x) => x.projectId && x.shortfallPad > 0);
}

/** "Name 0.5, Name 0.25" for messages — unknown ids show as-is. */
export const perProjectLine = (
  rows: { projectId: string; amount: number }[], names: Record<string, string>,
) => rows.map((r) => `${names[r.projectId] ?? r.projectId} ${r.amount}`).join(', ');
