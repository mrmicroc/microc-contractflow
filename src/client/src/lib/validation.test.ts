import { describe, expect, it } from 'vitest';
import {
  invalidNumericFields, isInvalidDecimal, isInvalidInteger, parseDecimal,
} from './validation';

describe('parseDecimal', () => {
  it('empty input is unset, not an error', () => {
    expect(parseDecimal('')).toEqual({ ok: true, value: null });
    expect(parseDecimal('   ')).toEqual({ ok: true, value: null });
  });

  it('accepts plain decimals with dot or comma', () => {
    expect(parseDecimal('1250')).toEqual({ ok: true, value: 1250 });
    expect(parseDecimal('8.5')).toEqual({ ok: true, value: 8.5 });
    expect(parseDecimal('8,5')).toEqual({ ok: true, value: 8.5 });
    expect(parseDecimal('0.75')).toEqual({ ok: true, value: 0.75 });
    expect(parseDecimal('-100')).toEqual({ ok: true, value: -100 });
  });

  it('rejects letters, junk and Number() edge cases', () => {
    for (const bad of ['abc', '10h', 'e5', '1e3', '0x10', 'Infinity', 'NaN',
      '1.2.3', '--5', '.', '5.', '.5', '1 000', 'kr', '5%'])
      expect(parseDecimal(bad).ok).toBe(false);
  });
});

describe('isInvalidDecimal / isInvalidInteger', () => {
  it('flags only non-empty garbage', () => {
    expect(isInvalidDecimal('')).toBe(false);
    expect(isInvalidDecimal('12')).toBe(false);
    expect(isInvalidDecimal('12x')).toBe(true);
  });

  it('integer fields reject decimals and text', () => {
    expect(isInvalidInteger('4')).toBe(false);
    expect(isInvalidInteger('')).toBe(false);
    expect(isInvalidInteger('4.5')).toBe(true);
    expect(isInvalidInteger('fyra')).toBe(true);
  });
});

describe('invalidNumericFields', () => {
  it('returns the labels of bad fields only', () => {
    expect(invalidNumericFields(
      { 'Hourly rate': '1000', 'Day rate': 'tusen', 'PAD rate': '' },
      { 'OT after': '8', 'Deadline': 'två' },
    )).toEqual(['Day rate', 'Deadline']);
    expect(invalidNumericFields({ a: '1' })).toEqual([]);
  });
});
