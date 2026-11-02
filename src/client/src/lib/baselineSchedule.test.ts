import { describe, expect, it } from 'vitest';
import {
  addYear, cellKey, changedMonths, closedKeys, grandTotal, gridFromSchedule, invalidCells,
  scheduleDifference, yearTotal,
} from './baselineSchedule';
import type { BaselineSchedule } from './types';

const stored: BaselineSchedule = {
  months: [
    { year: 2026, month: 9, amount: 6 }, { year: 2026, month: 10, amount: 12 },
    { year: 2026, month: 11, amount: 8 }, { year: 2026, month: 12, amount: 8 },
    { year: 2027, month: 1, amount: 8 },
  ],
  scheduled: 42, contractedTotal: 50, difference: 8, warning: 'x',
};

describe('baseline schedule grid', () => {
  it('builds years from the stored months and totals per year and overall', () => {
    const g = gridFromSchedule(stored, 2030);
    expect(g.years).toEqual([2026, 2027]);
    expect(yearTotal(g, 2026)).toBe(34);
    expect(yearTotal(g, 2027)).toBe(8);
    expect(grandTotal(g)).toBe(42);
    expect(scheduleDifference(50, grandTotal(g))).toBe(8);
    expect(scheduleDifference(null, 42)).toBeNull();
  });

  it('an empty schedule opens on the fallback year; years can be added either side', () => {
    const g = gridFromSchedule(null, 2027);
    expect(g.years).toEqual([2027]);
    expect(addYear(g).years).toEqual([2027, 2028]);
    expect(addYear(g, true).years).toEqual([2026, 2027]);
  });

  it('sends only changed months; a cleared cell becomes 0; comma decimals parse', () => {
    const g = gridFromSchedule(stored, 2030);
    g.cells[cellKey(2026, 10)] = '10,5'; // changed
    g.cells[cellKey(2026, 11)] = '';      // cleared → 0
    g.cells[cellKey(2027, 2)] = '8';      // new month
    g.cells[cellKey(2027, 3)] = '';       // still empty → not sent
    expect(changedMonths(g, stored)).toEqual([
      { year: 2026, month: 10, amount: 10.5 },
      { year: 2026, month: 11, amount: 0 },
      { year: 2027, month: 2, amount: 8 },
    ]);
  });

  it('flags non-numeric and negative cells', () => {
    const g = gridFromSchedule(null, 2027);
    g.cells[cellKey(2027, 1)] = 'abc';
    g.cells[cellKey(2027, 2)] = '-1';
    g.cells[cellKey(2027, 3)] = '4';
    expect(invalidCells(g).sort()).toEqual(['2027-1', '2027-2']);
  });

  it('maps the 409 closed-month list onto grid keys', () => {
    expect(closedKeys(['2026-09', 'junk', 3])).toEqual(new Set(['2026-9']));
    expect(closedKeys(undefined).size).toBe(0);
  });
});
