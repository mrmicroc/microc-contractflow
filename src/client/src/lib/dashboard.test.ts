import { describe, expect, it } from 'vitest';
import { dashboardTiles, type DashboardData } from './dashboard';
import type {
  AdditionalPadRequest, Booking, BookingStatus, InvoiceSpecification, InvoiceStatus,
  SubcontractorStatement, SupplierInvoice, Timesheet, TimesheetStatus,
} from './types';

const today = '2026-10-20';
const booking = (date: string, status: BookingStatus): Booking => ({
  id: `${date}-${status}`, projectId: 'p', projectName: 'North Link', customerName: 'Example Org',
  resourceId: 'r', resourceName: 'Alex', workPackageId: null, workPackageName: null,
  date, plannedHours: 8, activityRef: null, workDescription: null, customerContact: null,
  evidenceRef: null, status, expectedPad: 1, acknowledgedUtc: null, replacesBookingId: null,
  rescheduledToBookingId: null, warnings: [],
});
const sheet = (status: TimesheetStatus): Timesheet => ({
  id: status, resourceId: 'r', resourceName: 'Alex', year: 2026, isoWeek: 43, status,
  submittedUtc: null, reviewedUtc: null, reviewerNote: null, entryCount: 1, travelCount: 0,
  expenseCount: 0, allowanceCount: 0, totalActualHours: 8,
});
const invoice = (status: InvoiceStatus) => ({ id: status, status }) as InvoiceSpecification;
const count = (tiles: { key: string; count: number }[], key: string) =>
  tiles.find((x) => x.key === key)?.count;

const data: DashboardData = {
  today,
  bookings: [
    booking('2026-10-21', 'confirmed'),     // awaiting ack, upcoming
    booking('2026-10-19', 'confirmed'),     // past — not "awaiting ack"
    booking('2026-10-22', 'acknowledged'),  // upcoming
    booking('2026-10-23', 'requested'),     // manager: to confirm
    booking('2026-12-01', 'confirmed'),     // beyond horizon for managers
    booking('2026-10-24', 'cancelled'),     // terminal
  ],
  timesheets: [sheet('submitted'), sheet('returned'), sheet('approved')],
  draftRows: 3,
  padRequests: [
    { id: 'a', status: 'submitted' } as AdditionalPadRequest,
    { id: 'b', status: 'approved' } as AdditionalPadRequest,
  ],
  invoices: [invoice('draft'), invoice('customerReview'), invoice('final'), invoice('issued'), invoice('paid')],
  statements: [{ id: 's', status: 'draft' } as SubcontractorStatement],
  supplierInvoices: [
    { id: 'x', status: 'registered' } as SupplierInvoice,
    { id: 'y', status: 'paid' } as SupplierInvoice,
  ],
};

describe('dashboardTiles', () => {
  it('resource: own to-dos only, no money or approval tiles', () => {
    const tiles = dashboardTiles('Resource', data);
    expect(count(tiles, 'awaitingAck')).toBe(2); // 10-21 + 12-01 (future, confirmed)
    expect(count(tiles, 'upcoming')).toBe(3);    // 10-21, 10-22, 10-23 within 14 days
    expect(count(tiles, 'draftRows')).toBe(3);
    expect(count(tiles, 'returnedSheets')).toBe(1);
    expect(count(tiles, 'sheetsInReview')).toBe(1);
    expect(tiles.some((x) => x.to === '/economy' || x.to === '/approvals')).toBe(false);
  });

  it('project manager: approval queue + invoice drafts, never subcontractor pay', () => {
    const tiles = dashboardTiles('ProjectManager', data);
    expect(count(tiles, 'sheetsToApprove')).toBe(1);
    expect(count(tiles, 'padRequestsPending')).toBe(1);
    expect(count(tiles, 'unacknowledged')).toBe(1); // only the one within 14 days and ≥ today
    expect(count(tiles, 'requestedToConfirm')).toBe(1);
    expect(count(tiles, 'invoiceDrafts')).toBe(2);
    expect(count(tiles, 'statementDrafts')).toBeUndefined();
    expect(count(tiles, 'supplierToMatch')).toBeUndefined();
  });

  it('administrator: everything', () => {
    const tiles = dashboardTiles('Administrator', data);
    expect(count(tiles, 'sheetsToApprove')).toBe(1);
    expect(count(tiles, 'invoicesToIssue')).toBe(1);
    expect(count(tiles, 'invoicesUnpaid')).toBe(1);
    expect(count(tiles, 'statementDrafts')).toBe(1);
    expect(count(tiles, 'supplierToMatch')).toBe(1);
  });

  it('finance: money pipeline only', () => {
    const tiles = dashboardTiles('Finance', data);
    expect(tiles.map((x) => x.key)).toEqual([
      'invoiceDrafts', 'invoicesToIssue', 'invoicesUnpaid', 'statementDrafts', 'supplierToMatch',
    ]);
  });

  it('unknown roles see nothing; empty data gives zero counts with ok tone', () => {
    expect(dashboardTiles('CustomerReviewer', data)).toEqual([]);
    expect(dashboardTiles(null, data)).toEqual([]);
    const empty = dashboardTiles('Administrator', { today });
    expect(empty.every((x) => x.count === 0 && x.tone === 'ok')).toBe(true);
  });

  it('pending work is flagged for attention', () => {
    const tiles = dashboardTiles('Administrator', data);
    expect(tiles.find((x) => x.key === 'sheetsToApprove')?.tone).toBe('attention');
  });
});
