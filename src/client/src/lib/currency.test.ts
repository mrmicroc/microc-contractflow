import { describe, expect, it } from 'vitest';
import {
  defaultCurrencyFor, isCurrencyCode, isMissingRateWarning, markupRowsFrom, markupRowsToBody,
  nextFreeCategory, originalLabel, rateFormToBody, type RateForm,
} from './currency';

describe('defaultCurrencyFor', () => {
  const projects = [{ id: 'p1', currency: 'EUR' }, { id: 'p2', currency: null }];
  it("prefers the project's contract currency", () => expect(defaultCurrencyFor('p1', projects)).toBe('EUR'));
  it('falls back when the project has none or is unknown', () => {
    expect(defaultCurrencyFor('p2', projects, 'NOK')).toBe('NOK');
    expect(defaultCurrencyFor('', projects)).toBe('SEK');
  });
});

describe('isCurrencyCode', () => {
  it('accepts three letters only', () => {
    expect(isCurrencyCode('eur')).toBe(true);
    expect(isCurrencyCode('EURO')).toBe(false);
    expect(isCurrencyCode('E1R')).toBe(false);
  });
});

describe('originalLabel', () => {
  it('shows the original amount, currency and rate', () =>
    expect(originalLabel({ originalAmount: 1200, originalCurrency: 'EUR', exchangeRate: 11.5 }))
      .toBe('1200 EUR × 11.5'));
  it('is null for lines already in the document currency', () =>
    expect(originalLabel({ originalAmount: null, originalCurrency: null, exchangeRate: null })).toBeNull());
});

describe('isMissingRateWarning', () => {
  it('recognises the server warning', () => {
    expect(isMissingRateWarning('No EUR→SEK rate for 2026-10 — enter it under exchange rates; …')).toBe(true);
    expect(isMissingRateWarning('Unreviewed rate cell used: X')).toBe(false);
  });
});

describe('rateFormToBody', () => {
  const ok: RateForm = { year: '2026', month: '10', fromCurrency: 'eur', toCurrency: 'sek', rate: '11,5', note: ' ' };
  it('normalises codes, parses a comma rate, drops a blank note', () =>
    expect(rateFormToBody(ok).body).toEqual(
      { year: 2026, month: 10, fromCurrency: 'EUR', toCurrency: 'SEK', rate: 11.5, note: null }));
  it('rejects bad input with an i18n key', () => {
    expect(rateFormToBody({ ...ok, month: '13' }).error).toBe('fx.errMonth');
    expect(rateFormToBody({ ...ok, toCurrency: 'EUR' }).error).toBe('fx.errSame');
    expect(rateFormToBody({ ...ok, rate: '0' }).error).toBe('fx.errRate');
    expect(rateFormToBody({ ...ok, fromCurrency: 'EU' }).error).toBe('fx.errCurrency');
    expect(rateFormToBody({ ...ok, year: '1999' }).error).toBe('fx.errYear');
  });
});

describe('markupRowsToBody', () => {
  it('keeps filled rows (comma decimals), drops blanks', () =>
    expect(markupRowsToBody([
      { category: 'material', percent: '10' },
      { category: 'travel', percent: '0' },
      { category: 'meal', percent: '' },
      { category: 'equipment', percent: '2,5' },
    ]).rows).toEqual([
      { category: 'material', percent: 10 },
      { category: 'travel', percent: 0 },
      { category: 'equipment', percent: 2.5 },
    ]));
  it('rejects out-of-range percents and repeated categories', () => {
    expect(markupRowsToBody([{ category: 'material', percent: '1001' }]).error).toBe('terms.errMarkupPercent');
    expect(markupRowsToBody([{ category: 'material', percent: '-1' }]).error).toBe('terms.errMarkupPercent');
    expect(markupRowsToBody([
      { category: 'meal', percent: '1' }, { category: 'meal', percent: '2' },
    ]).error).toBe('terms.errMarkupDuplicate');
  });
  it('a new row starts at the first category without an override', () => {
    expect(nextFreeCategory([])).toBe('material');
    expect(nextFreeCategory([{ category: 'material' }, { category: 'travel' }])).toBe('accommodation');
  });
  it('round-trips stored rows', () =>
    expect(markupRowsFrom([{ category: 'material', percent: 10 }]))
      .toEqual([{ category: 'material', percent: '10' }]));
});
