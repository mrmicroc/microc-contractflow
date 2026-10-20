import { describe, expect, it } from 'vitest';
import { reviewNoteValid, weekSheetState } from './timesheets';
import type { Timesheet, TimesheetStatus } from './types';

const sheet = (status: TimesheetStatus, over: Partial<Timesheet> = {}): Timesheet => ({
  id: `s-${status}`, resourceId: 'r1', resourceName: 'Alex', year: 2026, isoWeek: 43, status,
  submittedUtc: null, reviewedUtc: null, reviewerNote: null, entryCount: 0, travelCount: 0,
  expenseCount: 0, allowanceCount: 0, totalActualHours: 0, ...over,
});
const rows = [{ timesheetId: null }, { timesheetId: 's' }, { timesheetId: null }];

describe('weekSheetState', () => {
  it('no sheet yet: unlocked, drafts counted', () => {
    const s = weekSheetState([], 2026, 43, rows);
    expect(s).toMatchObject({ sheet: null, locked: false, draftCount: 2, returnNote: null, needsResubmit: false });
  });

  it('submitted and approved sheets lock the week', () => {
    expect(weekSheetState([sheet('submitted')], 2026, 43, rows).locked).toBe(true);
    expect(weekSheetState([sheet('approved')], 2026, 43, rows).locked).toBe(true);
  });

  it('a returned sheet surfaces the reviewer note and asks for resubmission', () => {
    const s = weekSheetState([sheet('returned', { reviewerNote: '  Split the travel day  ' })], 2026, 43, rows);
    expect(s.locked).toBe(false);
    expect(s.needsResubmit).toBe(true);
    expect(s.returnNote).toBe('Split the travel day');
  });

  it('rejected without a note still needs resubmission', () => {
    const s = weekSheetState([sheet('rejected')], 2026, 43, rows);
    expect(s.needsResubmit).toBe(true);
    expect(s.returnNote).toBeNull();
  });

  it('matches only the requested week (and resource when given)', () => {
    const other = sheet('submitted', { isoWeek: 44 });
    expect(weekSheetState([other], 2026, 43, rows).sheet).toBeNull();
    const someoneElse = sheet('submitted', { resourceId: 'r2' });
    expect(weekSheetState([someoneElse], 2026, 43, rows, 'r1').sheet).toBeNull();
    expect(weekSheetState([someoneElse], 2026, 43, rows, 'r2').locked).toBe(true);
  });
});

describe('reviewNoteValid', () => {
  it('requires non-blank text', () => {
    expect(reviewNoteValid('')).toBe(false);
    expect(reviewNoteValid('   ')).toBe(false);
    expect(reviewNoteValid('Fix Tuesday')).toBe(true);
  });
});
