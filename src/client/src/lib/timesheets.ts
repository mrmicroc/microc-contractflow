import type { Timesheet } from './types';

interface DraftRow { timesheetId: string | null }

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
  const locked = sheet?.status === 'submitted' || sheet?.status === 'approved';
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
