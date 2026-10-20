import { afterEach, describe, expect, it, vi } from 'vitest';
import { daysInMonth, localMonth, localTodayIso, monthKey, monthRange, shiftMonth } from './dates';

describe('localTodayIso / localMonth', () => {
  afterEach(() => { vi.useRealTimers(); });

  it('uses the local calendar day, not the UTC one', () => {
    // 00:30 local on the 1st — toISOString() would still be the previous
    // day/month for any zone east of UTC.
    const lateNight = new Date(2026, 10, 1, 0, 30); // 2026-11-01 00:30 local
    expect(localTodayIso(lateNight)).toBe('2026-11-01');
    expect(localMonth(lateNight)).toEqual({ year: 2026, month: 11 });
  });

  it('defaults to the current clock', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date(2027, 0, 1, 0, 5)); // New Year 00:05 local
    expect(localTodayIso()).toBe('2027-01-01');
    expect(localMonth()).toEqual({ year: 2027, month: 1 });
  });

  it('pads single-digit months and days', () => {
    expect(localTodayIso(new Date(2026, 1, 3))).toBe('2026-02-03');
  });
});

describe('month helpers', () => {
  it('daysInMonth knows leap years', () => {
    expect(daysInMonth(2026, 2)).toBe(28);
    expect(daysInMonth(2028, 2)).toBe(29);
    expect(daysInMonth(2026, 11)).toBe(30);
    expect(daysInMonth(2026, 12)).toBe(31);
  });

  it('monthRange spans the real month', () => {
    expect(monthRange(2026, 11)).toEqual({ from: '2026-11-01', to: '2026-11-30' });
    expect(monthKey(2026, 3)).toBe('2026-03');
  });

  it('shiftMonth crosses year boundaries both ways', () => {
    expect(shiftMonth(2026, 12, 1)).toEqual({ year: 2027, month: 1 });
    expect(shiftMonth(2026, 1, -1)).toEqual({ year: 2025, month: 12 });
    expect(shiftMonth(2026, 5, -17)).toEqual({ year: 2024, month: 12 });
  });
});
