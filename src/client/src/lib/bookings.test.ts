import { describe, expect, it } from 'vitest';
import {
  addDays, availableActions, bookingTooltip, bookingUpdateBody, isTerminal, matchBooking, startTimeEdit,
  monthGrid, padEffectKey, rescheduleOutcomeKey, statusClasses, viewRange, weekDates,
} from './bookings';
import type { Booking, BookingStatus } from './types';

const booking = (over: Partial<Booking> = {}): Booking => ({
  id: 'b1', projectId: 'p1', projectName: 'North Link', customerName: 'Example Org',
  resourceId: 'r1', resourceName: 'Alex', workPackageId: 'wp1', workPackageName: 'Station A',
  date: '2026-11-10', plannedHours: 10, activityRef: 'ACT-1', workDescription: 'Inspection',
  customerContact: 'site lead', evidenceRef: 'mail 2026-11-01', status: 'confirmed',
  expectedPad: 1.25, acknowledgedUtc: null, replacesBookingId: null,
  rescheduledToBookingId: null, warnings: [], ...over,
});

describe('viewRange', () => {
  it('uses the real last day of the month (never -31 on short months)', () => {
    expect(viewRange('month', { year: 2026, month: 11 }, '2026-11-05'))
      .toEqual({ from: '2026-11-01', to: '2026-11-30' });
    expect(viewRange('month', { year: 2026, month: 10 }, '2026-10-05').to).toBe('2026-10-31');
  });

  it('handles February in leap and non-leap years', () => {
    expect(viewRange('month', { year: 2027, month: 2 }, '2027-02-01').to).toBe('2027-02-28');
    expect(viewRange('month', { year: 2028, month: 2 }, '2028-02-01').to).toBe('2028-02-29');
  });

  it('week view covers Monday–Sunday around the anchor; day view is the anchor', () => {
    expect(viewRange('week', { year: 2026, month: 10 }, '2026-10-15'))
      .toEqual({ from: '2026-10-12', to: '2026-10-18' });
    expect(viewRange('day', { year: 2026, month: 10 }, '2026-10-15'))
      .toEqual({ from: '2026-10-15', to: '2026-10-15' });
  });
});

describe('bookingUpdateBody', () => {
  it('keeps the fields the edit form does not touch', () => {
    const body = bookingUpdateBody(booking({ status: 'requested' }), {
      date: '2026-11-12', plannedHours: 7.5, workDescription: ' New text ',
      activityRef: '', status: 'tentative',
    });
    expect(body).toEqual({
      projectId: 'p1', resourceId: 'r1', workPackageId: 'wp1',
      date: '2026-11-12', plannedHours: 7.5, activityRef: null,
      workDescription: 'New text', customerContact: 'site lead',
      evidenceRef: 'mail 2026-11-01', status: 'tentative',
      instructedBy: null, instructedAtUtc: null,
    });
  });

  it('applies contact/evidence edits when given (blank clears)', () => {
    const body = bookingUpdateBody(booking(), {
      date: '2026-11-10', plannedHours: 8, workDescription: '', activityRef: '',
      status: 'requested', customerContact: 'new lead', evidenceRef: '  ',
    });
    expect(body.customerContact).toBe('new lead');
    expect(body.evidenceRef).toBeNull();
  });

  it('falls back to the booking hours when the field is empty', () => {
    expect(bookingUpdateBody(booking(), {
      date: '2026-11-10', plannedHours: null, workDescription: '', activityRef: '',
      status: 'requested',
    }).plannedHours).toBe(10);
  });
});

describe('booking update — instructed by and start time', () => {
  const edits = { date: '2026-11-10', plannedHours: 8, workDescription: '', activityRef: '', status: 'requested' as const };

  it('carries "instructed by" through unless edited', () => {
    const b = booking({ instructedBy: 'Sam Example', instructedAtUtc: '2026-11-01T09:00:00Z' });
    expect(bookingUpdateBody(b, edits)).toMatchObject({ instructedBy: 'Sam Example', instructedAtUtc: '2026-11-01T09:00:00Z' });
    expect(bookingUpdateBody(b, { ...edits, instructedBy: ' Kim Example ' }).instructedBy).toBe('Kim Example');
    expect(bookingUpdateBody(b, { ...edits, instructedBy: '' }).instructedBy).toBeNull();
  });

  it('sends a start time only when it changed; blank clears an agreed one', () => {
    expect(startTimeEdit('08:00:00', undefined)).toEqual({});
    expect(startTimeEdit('08:00:00', '08:00')).toEqual({});
    expect(startTimeEdit('08:00:00', '07:30')).toEqual({ startTime: '07:30:00' });
    expect(startTimeEdit(null, '06:00')).toEqual({ startTime: '06:00:00' });
    expect(startTimeEdit('08:00:00', '')).toEqual({ clearStartTime: true });
    expect(startTimeEdit(null, '')).toEqual({});
  });
});

describe('matchBooking', () => {
  const list = [
    booking({ id: 'a', date: '2026-11-10', projectId: 'p1', status: 'acknowledged' }),
    booking({ id: 'b', date: '2026-11-11', projectId: 'p1', status: 'cancelled' }),
    booking({ id: 'c', date: '2026-11-12', projectId: 'p1', status: 'forecast' }),
    booking({ id: 'd', date: '2026-11-13', projectId: 'p1' }),
    booking({ id: 'e', date: '2026-11-13', projectId: 'p1', resourceId: 'r2' }),
  ];

  it('links the single committed booking on that day and project', () => {
    expect(matchBooking(list, '2026-11-10', 'p1')?.id).toBe('a');
  });

  it('ignores terminal and pre-commitment bookings and other projects', () => {
    expect(matchBooking(list, '2026-11-11', 'p1')).toBeNull();
    expect(matchBooking(list, '2026-11-12', 'p1')).toBeNull();
    expect(matchBooking(list, '2026-11-10', 'p2')).toBeNull();
  });

  it('does not guess when several bookings match; resource narrows it', () => {
    expect(matchBooking(list, '2026-11-13', 'p1')).toBeNull();
    expect(matchBooking(list, '2026-11-13', 'p1', 'r2')?.id).toBe('e');
  });
});

describe('padEffectKey / rescheduleOutcomeKey', () => {
  it('maps known PAD effects and defaults unknown ones to none', () => {
    expect(padEffectKey('released')).toBe('booking.padEffect.released');
    expect(padEffectKey('utilised')).toBe('booking.padEffect.utilised');
    expect(padEffectKey('weird')).toBe('booking.padEffect.none');
    expect(padEffectKey(null)).toBe('booking.padEffect.none');
  });

  it('tells confirmed, short-of-PAD and kept-status replacements apart', () => {
    expect(rescheduleOutcomeKey({ replacementConfirmed: true })).toBe('booking.rescheduleConfirmed');
    expect(rescheduleOutcomeKey({ replacementConfirmed: false, insufficientPad: true }))
      .toBe('booking.rescheduleInsufficientPad');
    expect(rescheduleOutcomeKey({ replacementConfirmed: false, replacement: { status: 'forecast' } }))
      .toBe('booking.rescheduleKeptStatus');
    expect(rescheduleOutcomeKey({ replacementConfirmed: false })).toBe('booking.rescheduleNotConfirmed');
  });
});

describe('bookingTooltip', () => {
  it('appends translated warnings', () => {
    const label = (c: string) => `W:${c}`;
    expect(bookingTooltip(booking(), label)).toBe('Alex · North Link');
    expect(bookingTooltip(booking({ warnings: ['doubleBooked', 'onLeave'] }), label))
      .toBe('Alex · North Link\n⚠ W:doubleBooked, W:onLeave');
  });
});

describe('status helpers', () => {
  const all: BookingStatus[] = ['forecast', 'tentative', 'requested', 'confirmed',
    'acknowledged', 'inProgress', 'completed', 'cancelled', 'rescheduled'];

  it('isTerminal is true only for completed/cancelled/rescheduled', () => {
    expect(all.filter(isTerminal)).toEqual(['completed', 'cancelled', 'rescheduled']);
  });

  it('statusClasses: pre-commitment dashed, confirmed solid, cancelled struck', () => {
    for (const s of all) expect(statusClasses(s)).toBeTruthy();
    expect(statusClasses('forecast')).toContain('border-dashed');
    expect(statusClasses('confirmed')).toContain('bg-blue-600');
    expect(statusClasses('cancelled')).toContain('line-through');
    expect(statusClasses('rescheduled')).toContain('line-through');
  });
});

describe('availableActions', () => {
  it('manager can confirm/edit/cancel pre-commitment bookings', () => {
    expect(availableActions('requested', true, false))
      .toEqual(expect.arrayContaining(['confirm', 'edit', 'cancel']));
  });

  it('owner can acknowledge/start but not complete, cancel or confirm', () => {
    const actions = availableActions('confirmed', false, true);
    expect(actions).toEqual(expect.arrayContaining(['acknowledge', 'start']));
    expect(actions).not.toContain('complete'); // utilises PAD → manager only
    expect(actions).not.toContain('cancel');
    expect(actions).not.toContain('confirm');
    expect(actions).not.toContain('reschedule');
    expect(availableActions('inProgress', false, true)).toEqual([]);
  });

  it('manager can complete active bookings', () => {
    for (const s of ['confirmed', 'acknowledged', 'inProgress'] as const)
      expect(availableActions(s, true, false)).toContain('complete');
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

describe('weekDates', () => {
  it('returns the Monday-first week containing the anchor', () => {
    // 2026-10-15 is a Thursday
    expect(weekDates('2026-10-15')).toEqual([
      '2026-10-12', '2026-10-13', '2026-10-14', '2026-10-15',
      '2026-10-16', '2026-10-17', '2026-10-18',
    ]);
  });

  it('a Sunday anchor still maps to the same week', () => {
    expect(weekDates('2026-10-18')[0]).toBe('2026-10-12');
    expect(weekDates('2026-10-11')).toEqual(weekDates('2026-10-05'));
  });

  it('crosses month boundaries', () => {
    // 2026-11-01 is a Sunday
    expect(weekDates('2026-10-31')[6]).toBe('2026-11-01');
  });
});

describe('addDays', () => {
  it('shifts across month and year boundaries', () => {
    expect(addDays('2026-10-31', 1)).toBe('2026-11-01');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDays('2027-03-01', -1)).toBe('2027-02-28');
  });
});
