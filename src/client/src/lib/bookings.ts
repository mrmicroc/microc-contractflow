import { monthRange } from './dates';
import type { Booking, BookingRequestBody, BookingStatus } from './types';

/** Actions the UI can offer per booking status + caller rights. */
export type BookingAction =
  | 'edit' | 'delete' | 'confirm' | 'acknowledge' | 'start' | 'complete'
  | 'cancel' | 'reschedule';

/**
 * Mirrors the server's transition rules (BookingsController):
 * - manage (Admin/PM): full lifecycle incl. confirm/complete/cancel/reschedule
 * - owner (the booking's resource): acknowledge/start only — completing
 *   consumes customer PAD, which follows MICROC approval (§6.1)
 */
export function availableActions(
  status: BookingStatus,
  canManage: boolean,
  isOwner: boolean,
): BookingAction[] {
  const actions = new Set<BookingAction>();
  switch (status) {
    case 'forecast':
    case 'tentative':
    case 'requested':
      if (canManage) {
        actions.add('edit');
        actions.add('delete');
        actions.add('confirm');
        actions.add('cancel');
      }
      break;
    case 'confirmed':
      if (canManage || isOwner) { actions.add('acknowledge'); actions.add('start'); }
      if (canManage) { actions.add('complete'); actions.add('cancel'); actions.add('reschedule'); }
      break;
    case 'acknowledged':
      if (canManage || isOwner) actions.add('start');
      if (canManage) { actions.add('complete'); actions.add('cancel'); actions.add('reschedule'); }
      break;
    case 'inProgress':
      if (canManage) { actions.add('complete'); actions.add('cancel'); actions.add('reschedule'); }
      break;
    default:
      break; // terminal statuses — no actions
  }
  return [...actions];
}

export const PRE_COMMITMENT: BookingStatus[] = ['forecast', 'tentative', 'requested'];
export const TERMINAL: BookingStatus[] = ['completed', 'cancelled', 'rescheduled'];

export const isTerminal = (s: BookingStatus) => TERMINAL.includes(s);

/**
 * Monday-first month grid for the planning calendar. Returns weeks of ISO
 * yyyy-mm-dd date strings; null cells pad outside the month.
 */
export function monthGrid(year: number, month: number): (string | null)[][] {
  const first = new Date(Date.UTC(year, month - 1, 1));
  const daysInMonth = new Date(Date.UTC(year, month, 0)).getUTCDate();
  // JS: Sun=0..Sat=6 → Monday-first column index
  const offset = (first.getUTCDay() + 6) % 7;

  const cells: (string | null)[] = Array(offset).fill(null);
  for (let d = 1; d <= daysInMonth; d++) {
    cells.push(`${year}-${String(month).padStart(2, '0')}-${String(d).padStart(2, '0')}`);
  }
  while (cells.length % 7 !== 0) cells.push(null);

  const weeks: (string | null)[][] = [];
  for (let i = 0; i < cells.length; i += 7) weeks.push(cells.slice(i, i + 7));
  return weeks;
}

/**
 * The seven ISO dates of the Monday-first week containing anchorIso.
 */
export function weekDates(anchorIso: string): string[] {
  const d = new Date(`${anchorIso}T00:00:00Z`);
  const offset = (d.getUTCDay() + 6) % 7;
  const monday = d.getTime() - offset * 86400_000;
  return Array.from({ length: 7 }, (_, i) =>
    new Date(monday + i * 86400_000).toISOString().slice(0, 10));
}

export const addDays = (iso: string, days: number) =>
  new Date(new Date(`${iso}T00:00:00Z`).getTime() + days * 86400_000)
    .toISOString().slice(0, 10);

/** Status → chip styling, per design §7.1 colour rules. */
export function statusClasses(status: BookingStatus): string {
  switch (status) {
    case 'forecast': return 'border border-dashed border-slate-400 bg-white text-slate-500';
    case 'tentative': return 'border border-dashed border-blue-300 bg-blue-50 text-blue-600';
    case 'requested': return 'border border-blue-400 bg-blue-50 text-blue-700';
    case 'confirmed': return 'bg-blue-600 text-white';
    case 'acknowledged': return 'bg-teal-600 text-white';
    case 'inProgress': return 'bg-indigo-600 text-white';
    case 'completed': return 'bg-green-700 text-white';
    case 'cancelled': return 'bg-slate-200 text-slate-500 line-through';
    case 'rescheduled': return 'bg-slate-200 text-slate-500 line-through';
  }
}

export type CalendarView = 'month' | 'week' | 'day';

/**
 * The date window the planning calendar loads for a view: the real month
 * (28–31 days — never "-31" on short months), the Monday–Sunday week around
 * the anchor, or the anchor day itself.
 */
export function viewRange(
  view: CalendarView, ym: { year: number; month: number }, anchorIso: string,
): { from: string; to: string } {
  if (view === 'month') return monthRange(ym.year, ym.month);
  if (view === 'week') {
    const w = weekDates(anchorIso);
    return { from: w[0], to: w[6] };
  }
  return { from: anchorIso, to: anchorIso };
}

/** The editable subset of a booking update (the Plan edit form). */
export interface BookingEdits {
  date: string;
  plannedHours: number | null;
  workDescription: string;
  activityRef: string;
  status: BookingRequestBody['status'];
  /** Optional — omitted means "keep the booking's value". */
  customerContact?: string;
  evidenceRef?: string;
}

/**
 * Full PUT body for a booking edit. The server replaces fields wholesale, so
 * every field the form doesn't touch (work package, customer contact,
 * evidence ref, resource, project) is carried over from the booking.
 */
export function bookingUpdateBody(b: Booking, edits: BookingEdits): BookingRequestBody {
  return {
    projectId: b.projectId,
    resourceId: b.resourceId,
    workPackageId: b.workPackageId,
    date: edits.date,
    plannedHours: edits.plannedHours ?? b.plannedHours,
    activityRef: edits.activityRef.trim() || null,
    workDescription: edits.workDescription.trim() || null,
    customerContact: edits.customerContact === undefined
      ? b.customerContact : edits.customerContact.trim() || null,
    evidenceRef: edits.evidenceRef === undefined
      ? b.evidenceRef : edits.evidenceRef.trim() || null,
    status: edits.status,
  };
}

/**
 * The committed booking a time row on `date` + `projectId` reports against —
 * so booked (PAD-covered) days are linked and never billed twice as hours.
 * Terminal and pre-commitment bookings never match; ambiguous days (several
 * candidates) don't auto-link.
 */
export function matchBooking(
  bookings: Booking[], date: string, projectId: string, resourceId?: string,
): Booking | null {
  const live = bookings.filter((b) =>
    b.date === date && b.projectId === projectId
    && (!resourceId || b.resourceId === resourceId)
    && !isTerminal(b.status) && !PRE_COMMITMENT.includes(b.status));
  return live.length === 1 ? live[0] : null;
}

/** PAD consequences the cancel/reschedule endpoints report. */
export const PAD_EFFECTS = ['none', 'released', 'utilised', 'partial', 'charged'] as const;

/** i18n key describing a cancellation's PAD consequence (unknown → none). */
export function padEffectKey(effect: string | null | undefined): string {
  return `booking.padEffect.${(PAD_EFFECTS as readonly string[]).includes(effect ?? '') ? effect : 'none'}`;
}

/** i18n key for what happened to a reschedule's replacement booking — the
 * user must learn when it stayed unconfirmed (insufficient PAD, closed month
 * or a pre-commitment original). */
export function rescheduleOutcomeKey(r: {
  replacementConfirmed: boolean; insufficientPad?: boolean; replacement?: { status: BookingStatus };
}): string {
  if (r.replacementConfirmed) return 'booking.rescheduleConfirmed';
  if (r.insufficientPad) return 'booking.rescheduleInsufficientPad';
  if (r.replacement && PRE_COMMITMENT.includes(r.replacement.status))
    return 'booking.rescheduleKeptStatus';
  return 'booking.rescheduleNotConfirmed';
}

/** Continuation-overrun split returned by POST /complete (§4.3 ①–④). */
export interface OverrunSplit {
  fromCurrentFree: number;
  fromAdditional: number;
  fromNextMonthBaseline: number;
  autoAdditional: number;
  nextMonthConflict: boolean;
}

export const OVERRUN_SOURCES = [
  'fromCurrentFree', 'fromAdditional', 'fromNextMonthBaseline', 'autoAdditional',
] as const;

/** The non-zero overrun sources in allocation order — what the planner is
 * told after completing a booking that ran over its commitment. */
export function overrunParts(s: OverrunSplit | null | undefined):
  { source: (typeof OVERRUN_SOURCES)[number]; amount: number }[] {
  if (!s) return [];
  return OVERRUN_SOURCES
    .map((source) => ({ source, amount: s[source] }))
    .filter((p) => p.amount > 0);
}

/** Additional PAD a refused confirm still needs: needed − free (free never
 * counts below zero), rounded up to 2 decimals so the request covers it. */
export function additionalPadShortfall(neededPad: number, freePad: number): number {
  const short = neededPad - Math.max(0, freePad);
  return short <= 0 ? 0 : Math.ceil(short * 100 - 1e-9) / 100;
}

/** Conflict-warning tooltip text for a booking chip. */
export function bookingTooltip(
  b: Booking, warningLabel: (code: string) => string,
): string {
  const base = `${b.resourceName} · ${b.projectName}`;
  return b.warnings.length ? `${base}\n⚠ ${b.warnings.map(warningLabel).join(', ')}` : base;
}
