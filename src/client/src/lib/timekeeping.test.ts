import { describe, expect, it } from 'vitest';
import { isoWeekDates, isoWeekLabel, isoWeekMonday, isoWeekOf, isoWeekStart } from './timekeeping';

describe('isoWeekLabel', () => {
  it('zero-pads the week', () => {
    expect(isoWeekLabel(2026, 3)).toBe('2026-W03');
    expect(isoWeekLabel(2026, 43)).toBe('2026-W43');
  });
});

describe('isoWeekMonday', () => {
  it('returns the Monday of the containing week', () => {
    expect(isoWeekMonday('2026-10-14')).toBe('2026-10-12'); // Wednesday
    expect(isoWeekMonday('2026-10-12')).toBe('2026-10-12'); // Monday → itself
    expect(isoWeekMonday('2026-10-18')).toBe('2026-10-12'); // Sunday → same week
  });
});

describe('isoWeekOf', () => {
  it('known ISO weeks', () => {
    expect(isoWeekOf('2026-10-12')).toEqual({ year: 2026, week: 42 });
    expect(isoWeekOf('2026-01-01')).toEqual({ year: 2026, week: 1 });
    // Dec 29 2025 is Monday of ISO 2026-W01 (Jan 1 2026 is a Thursday).
    expect(isoWeekOf('2025-12-29')).toEqual({ year: 2026, week: 1 });
    // Jan 1 2027 is a Friday → still ISO 2026-W53.
    expect(isoWeekOf('2027-01-01')).toEqual({ year: 2026, week: 53 });
  });
});

describe('isoWeekStart / isoWeekDates', () => {
  it('inverts isoWeekOf', () => {
    const { year, week } = isoWeekOf('2027-11-18');
    expect(isoWeekStart(year, week)).toBe('2027-11-15'); // that week's Monday
  });
  it('spans Monday→Sunday', () => {
    const days = isoWeekDates(2026, 42);
    expect(days).toHaveLength(7);
    expect(days[0]).toBe('2026-10-12');
    expect(days[6]).toBe('2026-10-18');
  });
});
