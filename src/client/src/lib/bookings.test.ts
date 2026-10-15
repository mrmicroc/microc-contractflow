import { describe, expect, it } from 'vitest';
import { availableActions, monthGrid } from './bookings';

describe('availableActions', () => {
  it('manager can confirm/edit/cancel pre-commitment bookings', () => {
    expect(availableActions('requested', true, false))
      .toEqual(expect.arrayContaining(['confirm', 'edit', 'cancel']));
  });

  it('owner can acknowledge/start/complete but not cancel or confirm', () => {
    const actions = availableActions('confirmed', false, true);
    expect(actions).toEqual(expect.arrayContaining(['acknowledge', 'start', 'complete']));
    expect(actions).not.toContain('cancel');
    expect(actions).not.toContain('confirm');
    expect(actions).not.toContain('reschedule');
  });

  it('non-owner non-manager sees nothing on active bookings', () => {
    expect(availableActions('confirmed', false, false)).toEqual([]);
    expect(availableActions('inProgress', false, false)).toEqual([]);
  });

  it('terminal statuses offer no actions', () => {
    for (const s of ['completed', 'cancelled', 'rescheduled'] as const) {
      expect(availableActions(s, true, true)).toEqual([]);
    }
  });

  it('in-progress bookings can complete or be rescheduled by manager', () => {
    const actions = availableActions('inProgress', true, false);
    expect(actions).toEqual(expect.arrayContaining(['complete', 'cancel', 'reschedule']));
  });
});

describe('monthGrid', () => {
  it('produces Monday-first weeks covering the whole month', () => {
    const weeks = monthGrid(2026, 10); // October 2026 starts on a Thursday
    expect(weeks[0].slice(0, 3)).toEqual([null, null, null]);
    expect(weeks[0][3]).toBe('2026-10-01');
    expect(weeks[0][6]).toBe('2026-10-04');
    const days = weeks.flat().filter(Boolean);
    expect(days).toHaveLength(31);
    expect(days.at(-1)).toBe('2026-10-31');
    expect(days.at(-2)).toBe('2026-10-30'); // ends Saturday — last cell is padding
  });

  it('handles months starting on Monday with no padding', () => {
    const weeks = monthGrid(2027, 2); // 2027-02-01 is a Monday
    expect(weeks[0][0]).toBe('2027-02-01');
    expect(weeks.flat().filter(Boolean)).toHaveLength(28);
  });

  it('pads the last week to seven cells', () => {
    const weeks = monthGrid(2026, 10);
    for (const week of weeks) expect(week).toHaveLength(7);
  });
});
