import { isAdmin } from './roles';
import type { Timesheet, TimesheetReview, TimesheetStatus } from './types';

interface DraftRow { timesheetId: string | null }

/** Statuses that freeze a sheet's rows (server: ReportingControllerBase). */
export const LOCKED_STATUSES: ReadonlySet<TimesheetStatus> =
  new Set<TimesheetStatus>(['submitted', 'approved', 'invoiced']);

/** Badge colours per timesheet status — one source for every list. */
export function sheetStatusCls(status: TimesheetStatus): string {
  switch (status) {
    case 'approved': return 'bg-emerald-50 text-emerald-700';
    case 'invoiced': return 'bg-indigo-50 text-indigo-700';
    case 'submitted': return 'bg-amber-50 text-amber-700';
    case 'returned':
    case 'rejected': return 'bg-red-50 text-red-700';
    default: return 'bg-slate-100 text-slate-600';
  }
}

/** Review history oldest first, with blank notes normalised to null. */
export function reviewHistory(reviews: TimesheetReview[] | undefined): TimesheetReview[] {
  return [...(reviews ?? [])]
    .map((r) => ({ ...r, note: r.note?.trim() || null }))
    .sort((a, b) => a.atUtc.localeCompare(b.atUtc));
}

/** Only an administrator may reopen, and only an approved (not yet
 * invoiced) week — the server also refuses closed or billed periods. */
export const canReopen = (status: TimesheetStatus, me: { role: string } | null | undefined) =>
  status === 'approved' && isAdmin(me);

export interface WeekSheetState {
  /** The week's timesheet (one per resource and week), if any. */
  sheet: Timesheet | null;
  /** Submitted/approved — rows are frozen until returned. */
  locked: boolean;
  /** Rows not yet inside a submission. */
  draftCount: number;
  /** The reviewer's comment when the sheet came back (returned/rejected). */
  returnNote: string | null;
  /** True when the sheet was sent back and needs correction + resubmission. */
  needsResubmit: boolean;
}

/**
 * The resource's view of one ISO week: which sheet applies, whether rows are
 * locked, how many drafts wait, and — for a returned or rejected sheet — the
 * reviewer's note so the resource knows what to fix (design §6.1).
 */
export function weekSheetState(
  sheets: Timesheet[], year: number, week: number,
  rows: DraftRow[], resourceId?: string,
): WeekSheetState {
  const sheet = sheets.find((s) => s.year === year && s.isoWeek === week
    && (!resourceId || s.resourceId === resourceId)) ?? null;
  const locked = sheet ? LOCKED_STATUSES.has(sheet.status) : false;
  const sentBack = sheet?.status === 'returned' || sheet?.status === 'rejected';
  return {
    sheet,
    locked,
    draftCount: rows.filter((r) => !r.timesheetId).length,
    returnNote: sentBack ? sheet?.reviewerNote?.trim() || null : null,
    needsResubmit: sentBack,
  };
}

/** A return/reject decision needs a reviewer comment (server enforces it too). */
export const reviewNoteValid = (note: string) => note.trim().length > 0;
