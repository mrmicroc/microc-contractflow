// Role-aware dashboard aggregation (design §8) — pure: the page fetches,
// this turns the raw lists into the tiles each role should see.
import { addDays, isTerminal } from './bookings';
import type {
  AdditionalPadRequest, Booking, InvoiceSpecification, SubcontractorStatement,
  SupplierInvoice, Timesheet,
} from './types';

export type TileTone = 'neutral' | 'attention' | 'ok';

export interface DashboardTile {
  /** i18n key under dashboard.tile.* */
  key: string;
  count: number;
  /** Route the tile links to. */
  to: string;
  tone: TileTone;
}

export interface DashboardData {
  today: string;
  /** Own bookings (resource) or all bookings in the window (managers). */
  bookings?: Booking[];
  /** Timesheets visible to the caller. */
  timesheets?: Timesheet[];
  /** Own draft rows not yet submitted (resource). */
  draftRows?: number;
  padRequests?: AdditionalPadRequest[];
  invoices?: InvoiceSpecification[];
  statements?: SubcontractorStatement[];
  supplierInvoices?: SupplierInvoice[];
  /** ISO year/week of today — for "this week's timesheet". */
  currentWeek?: { year: number; week: number };
}

/** Horizon for "upcoming" and "awaiting acknowledgement" (days). */
export const UPCOMING_DAYS = 14;

const tone = (count: number, attention = true): TileTone =>
  count === 0 ? 'ok' : attention ? 'attention' : 'neutral';

const inHorizon = (b: Booking, today: string) =>
  b.date >= today && b.date <= addDays(today, UPCOMING_DAYS);

/** The resource's own to-dos: acknowledge, report, fix returned weeks. */
export function resourceTiles(d: DashboardData): DashboardTile[] {
  const bookings = d.bookings ?? [];
  const sheets = d.timesheets ?? [];
  const awaitingAck = bookings.filter((b) => b.status === 'confirmed' && b.date >= d.today).length;
  const upcoming = bookings.filter((b) => !isTerminal(b.status) && inHorizon(b, d.today)).length;
  const returned = sheets.filter((s) => s.status === 'returned' || s.status === 'rejected').length;
  const awaitingReview = sheets.filter((s) => s.status === 'submitted').length;
  return [
    { key: 'awaitingAck', count: awaitingAck, to: '/my', tone: tone(awaitingAck) },
    { key: 'upcoming', count: upcoming, to: '/my', tone: 'neutral' },
    { key: 'draftRows', count: d.draftRows ?? 0, to: '/my', tone: tone(d.draftRows ?? 0) },
    { key: 'returnedSheets', count: returned, to: '/my', tone: tone(returned) },
    { key: 'sheetsInReview', count: awaitingReview, to: '/my', tone: 'neutral' },
  ];
}

/** Planner/approver queue (Admin + PM). */
export function managerTiles(d: DashboardData): DashboardTile[] {
  const sheets = d.timesheets ?? [];
  const submitted = sheets.filter((s) => s.status === 'submitted').length;
  const padPending = (d.padRequests ?? []).filter((r) => r.status === 'submitted').length;
  const unacked = (d.bookings ?? [])
    .filter((b) => b.status === 'confirmed' && inHorizon(b, d.today)).length;
  const preCommit = (d.bookings ?? []).filter((b) => b.status === 'requested' && inHorizon(b, d.today)).length;
  return [
    { key: 'sheetsToApprove', count: submitted, to: '/approvals', tone: tone(submitted) },
    // Only when PAD requests were loaded (PAD feature on).
    ...(d.padRequests === undefined ? []
      : [{ key: 'padRequestsPending', count: padPending, to: '/approvals', tone: tone(padPending) } as DashboardTile]),
    { key: 'unacknowledged', count: unacked, to: '/plan', tone: tone(unacked) },
    { key: 'requestedToConfirm', count: preCommit, to: '/plan', tone: tone(preCommit) },
  ];
}

/** Money pipeline (Admin + Finance; PM sees invoice drafts only). */
export function moneyTiles(d: DashboardData, includePay: boolean): DashboardTile[] {
  const invoices = d.invoices ?? [];
  const draftInv = invoices.filter((i) => i.status === 'draft' || i.status === 'customerReview').length;
  const toIssue = invoices.filter((i) => i.status === 'final').length;
  const unpaid = invoices.filter((i) => i.status === 'issued').length;
  const tiles: DashboardTile[] = [
    { key: 'invoiceDrafts', count: draftInv, to: '/economy', tone: tone(draftInv, false) },
    { key: 'invoicesToIssue', count: toIssue, to: '/economy', tone: tone(toIssue) },
    { key: 'invoicesUnpaid', count: unpaid, to: '/economy', tone: tone(unpaid, false) },
  ];
  if (includePay) {
    const draftSt = (d.statements ?? []).filter((s) => s.status === 'draft').length;
    const supplierOpen = (d.supplierInvoices ?? [])
      .filter((s) => s.status === 'registered' || s.status === 'discrepancy').length;
    tiles.push(
      { key: 'statementDrafts', count: draftSt, to: '/economy', tone: tone(draftSt, false) },
      { key: 'supplierToMatch', count: supplierOpen, to: '/economy', tone: tone(supplierOpen) },
    );
  }
  return tiles;
}

/** All tiles for a role, in display order. */
export function dashboardTiles(role: string | null | undefined, d: DashboardData): DashboardTile[] {
  switch (role) {
    case 'Resource': return resourceTiles(d);
    case 'ProjectManager': return [...managerTiles(d), ...moneyTiles(d, false)];
    case 'Administrator': return [...managerTiles(d), ...moneyTiles(d, true)];
    case 'Finance': return moneyTiles(d, true);
    default: return [];
  }
}
