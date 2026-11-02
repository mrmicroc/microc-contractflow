import { describe, expect, it } from 'vitest';
import { additionalPadShortfall, overrunParts } from './bookings';

describe('overrunParts', () => {
  it('lists non-zero sources in allocation order ①–④', () => {
    expect(overrunParts({
      fromCurrentFree: 0.25, fromAdditional: 0, fromNextMonthBaseline: 0.5,
      autoAdditional: 0.125, nextMonthConflict: true,
    })).toEqual([
      { source: 'fromCurrentFree', amount: 0.25 },
      { source: 'fromNextMonthBaseline', amount: 0.5 },
      { source: 'autoAdditional', amount: 0.125 },
    ]);
  });

  it('is empty without an overrun', () => {
    expect(overrunParts(null)).toEqual([]);
    expect(overrunParts({
      fromCurrentFree: 0, fromAdditional: 0, fromNextMonthBaseline: 0,
      autoAdditional: 0, nextMonthConflict: false,
    })).toEqual([]);
  });
});

describe('additionalPadShortfall', () => {
  it('requests the part free PAD cannot cover (partial availability: 0.5 free + 0.5 additional)', () => {
    expect(additionalPadShortfall(1, 0.5)).toBe(0.5);
  });
  it('ignores negative free PAD', () => {
    expect(additionalPadShortfall(1, -0.25)).toBe(1);
  });
  it('rounds up to cover fractional needs and is zero when covered', () => {
    expect(additionalPadShortfall(1.125, 0)).toBe(1.13);
    expect(additionalPadShortfall(1, 2)).toBe(0);
  });
});
