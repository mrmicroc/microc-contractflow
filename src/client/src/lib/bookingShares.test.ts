import { describe, expect, it } from 'vitest';
import {
  actualHoursBody, perProjectLine, shareProjectOptions, sharesBody, shortfallsOf,
} from './bookingShares';
import type { ProjectSummary } from './types';

const p = (id: string, contractId: string, isActive = true): ProjectSummary =>
  ({ id, contractId, name: `Project ${id}`, code: null, isActive });

describe('shareProjectOptions', () => {
  it('offers active projects of the same contract, never the primary', () => {
    const list = [p('a', 'k1'), p('b', 'k1'), p('c', 'k2'), p('d', 'k1', false)];
    expect(shareProjectOptions(list, 'a').map((x) => x.id)).toEqual(['b']);
    expect(shareProjectOptions(list, 'zzz')).toEqual([]);
  });
});

describe('sharesBody', () => {
  it('no extra rows → a single-project booking', () => {
    expect(sharesBody('a', '8', [])).toEqual({ shares: [], plannedHours: 8 });
    expect(sharesBody('a', '8', [{ projectId: '', hours: '' }])).toEqual({ shares: [], plannedHours: 8 });
  });

  it('the document example: 2 h + 4 h → shares and a 6 h day', () => {
    expect(sharesBody('a', '2', [{ projectId: 'b', hours: '4' }])).toEqual({
      shares: [{ projectId: 'a', plannedHours: 2 }, { projectId: 'b', plannedHours: 4 }],
      plannedHours: 6,
    });
  });

  it('sums a project picked twice and parses comma decimals', () => {
    expect(sharesBody('a', '3,5', [{ projectId: 'b', hours: '1' }, { projectId: 'b', hours: '0,5' }]))
      .toEqual({
        shares: [{ projectId: 'a', plannedHours: 3.5 }, { projectId: 'b', plannedHours: 1.5 }],
        plannedHours: 5,
      });
  });

  it('refuses non-numeric or non-positive hours', () => {
    expect(sharesBody('a', 'x', [])).toBeNull();
    expect(sharesBody('a', '0', [])).toBeNull();
    expect(sharesBody('a', '4', [{ projectId: 'b', hours: '0' }])).toBeNull();
  });
});

describe('actualHoursBody', () => {
  it('keeps typed values, skips blanks, refuses garbage', () => {
    expect(actualHoursBody({ a: '6', b: '6,5', c: '' })).toEqual({ a: 6, b: 6.5 });
    expect(actualHoursBody({ a: '-1' })).toBeNull();
    expect(actualHoursBody({ a: 'abc' })).toBeNull();
  });
});

describe('shortfallsOf', () => {
  it('reads the per-project shortfall list of a 409 insufficientPad', () => {
    expect(shortfallsOf({
      error: 'insufficientPad',
      shortfalls: [
        { projectId: 'b', freePad: 0.25, neededPad: 0.67, shortfallPad: 0.42 },
        { projectId: 'a', freePad: 2, neededPad: 0.33, shortfallPad: 0 },
      ],
    })).toEqual([{ projectId: 'b', freePad: 0.25, neededPad: 0.67, shortfallPad: 0.42 }]);
    expect(shortfallsOf({ error: 'insufficientPad' })).toEqual([]);
    expect(shortfallsOf(null)).toEqual([]);
  });

  it('formats per-project amounts with names', () => {
    expect(perProjectLine([{ projectId: 'a', amount: 0.75 }, { projectId: 'x', amount: 0.75 }], { a: 'Alpha' }))
      .toBe('Alpha 0.75, x 0.75');
  });
});
