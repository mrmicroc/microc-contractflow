import type { BookingStatus } from './types';

/** Actions the UI can offer per booking status + caller rights. */
export type BookingAction =
  | 'edit' | 'confirm' | 'acknowledge' | 'start' | 'complete'
  | 'cancel' | 'reschedule';

/**
 * Mirrors the server's transition rules (BookingsController):
 * - manage (Admin/PM): full lifecycle incl. confirm/cancel/reschedule
 * - owner (the booking's resource): acknowledge/start/complete only
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
        actions.add('confirm');
        actions.add('cancel');
      }
      break;
    case 'confirmed':
      if (canManage || isOwner) { actions.add('acknowledge'); actions.add('start'); actions.add('complete'); }
      if (canManage) { actions.add('cancel'); actions.add('reschedule'); }
      break;
    case 'acknowledged':
      if (canManage || isOwner) { actions.add('start'); actions.add('complete'); }
      if (canManage) { actions.add('cancel'); actions.add('reschedule'); }
      break;
    case 'inProgress':
      if (canManage || isOwner) actions.add('complete');
      if (canManage) { actions.add('cancel'); actions.add('reschedule'); }
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
