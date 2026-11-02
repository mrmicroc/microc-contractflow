import { describe, expect, it } from 'vitest';
import { numOr, numOrNull, resolveVersion } from './versioned';

const v = (version: number, effectiveFrom: string) => ({ version, effectiveFrom });

describe('resolveVersion', () => {
  const versions = [v(1, '2026-01-01'), v(2, '2026-06-01'), v(3, '2027-01-01')];

  it('returns null when no version is in force', () => {
    expect(resolveVersion(versions, '2025-12-31')).toBeNull();
    expect(resolveVersion([], '2026-06-15')).toBeNull();
  });

  it('picks the latest version effective on or before the date', () => {
    expect(resolveVersion(versions, '2026-01-01')!.version).toBe(1);
    expect(resolveVersion(versions, '2026-05-31')!.version).toBe(1);
    expect(resolveVersion(versions, '2026-06-01')!.version).toBe(2);
    expect(resolveVersion(versions, '2099-01-01')!.version).toBe(3);
  });

  it('handles unsorted input', () => {
    const shuffled = [v(3, '2027-01-01'), v(1, '2026-01-01'), v(2, '2026-06-01')];
    expect(resolveVersion(shuffled, '2026-06-15')!.version).toBe(2);
  });

  it('breaks same-date ties on higher version', () => {
    const tied = [v(1, '2026-01-01'), v(2, '2026-01-01')];
    expect(resolveVersion(tied, '2026-01-01')!.version).toBe(2);
  });
});

describe('numOrNull', () => {
  it('returns null for blank input', () => {
    expect(numOrNull('')).toBeNull();
    expect(numOrNull('   ')).toBeNull();
  });

  it('parses decimals', () => {
    expect(numOrNull('1200.50')).toBe(1200.5);
    expect(numOrNull('8')).toBe(8);
  });

  it('returns null for non-numeric input', () => {
    expect(numOrNull('abc')).toBeNull();
    expect(numOrNull('1e5')).toBeNull();
  });

  it('accepts the Swedish decimal comma', () => {
    expect(numOrNull('8,5')).toBe(8.5);
    expect(numOrNull(' 12,5 ')).toBe(12.5);
    expect(numOrNull('4800,50')).toBe(4800.5);
  });

  it('keeps an explicit zero', () => {
    expect(numOrNull('0')).toBe(0);
    expect(numOrNull('0,0')).toBe(0);
  });
});

describe('numOr', () => {
  it('falls back only when empty or invalid', () => {
    expect(numOr('', 20)).toBe(20);
    expect(numOr('x', 20)).toBe(20);
    expect(numOr('0', 20)).toBe(0);
    expect(numOr('7,5', 8)).toBe(7.5);
  });
});
