import { describe, expect, it } from 'vitest';
import { calendarLabel, countryName, currencyName } from './countries';

describe('countryName / currencyName', () => {
  it('localizes ISO codes', () => {
    expect(countryName('SE', 'en')).toBe('Sweden');
    expect(countryName('SE', 'sv')).toBe('Sverige');
    expect(currencyName('EUR', 'en')).toBe('Euro');
  });

  it('falls back to a dash for empty and to the code for unknown values', () => {
    expect(countryName(null, 'en')).toBe('—');
    expect(currencyName('', 'en')).toBe('—');
    expect(countryName('not-a-code', 'en')).toBe('not-a-code');
  });
});

describe('calendarLabel', () => {
  it('uses the none label for NONE', () => {
    expect(calendarLabel('NONE', 'None', 'en', 'Weekends only')).toBe('Weekends only');
  });

  it('localizes the country and keeps the region suffix of variants', () => {
    expect(calendarLabel('GB-SCT', 'United Kingdom — Scotland', 'en', '-'))
      .toBe('United Kingdom — Scotland');
    expect(calendarLabel('GB-NIR', 'GB-NIR', 'en', '-')).toBe('United Kingdom (GB-NIR)');
  });

  it('plain codes get "Country (CODE)"', () => {
    expect(calendarLabel('DE', 'Germany', 'en', '-')).toBe('Germany (DE)');
  });
});
