import { describe, expect, it } from 'vitest';
import { emptyTerms, fromTerms, termsFormToBody } from './terms';
import type { ContractTerms } from './types';

const base = { ...emptyTerms, effectiveFrom: '2026-01-01' };

describe('termsFormToBody', () => {
  it('an explicit zero survives (no Number(x) || default fallbacks)', () => {
    const body = termsFormToBody({
      ...base, padEnabled: true,
      carryOverMaxPercent: '0', minPadPerBookedDay: '0', overtimePercent: '0',
      cancellationDeadlineBusinessDays: '0', expenseMarkupPercent: '0',
    });
    expect(body.carryOverMaxPercent).toBe(0);
    expect(body.minPadPerBookedDay).toBe(0);
    expect(body.overtimePercent).toBe(0);
    expect(body.cancellationDeadlineBusinessDays).toBe(0);
    expect(body.expenseMarkupPercent).toBe(0);
  });

  it('empty rule fields fall back to the contract defaults', () => {
    const body = termsFormToBody({
      ...base, overtimeAfterHours: '', overtimePercent: '', padHoursPerDay: '',
      minPadPerBookedDay: '', carryOverMaxPercent: '', carryOverMonths: '',
      cancellationDeadlineBusinessDays: '',
    });
    expect(body).toMatchObject({
      overtimeAfterHours: 8, overtimePercent: 150, padHoursPerDay: 8, minPadPerBookedDay: 1,
      carryOverMaxPercent: 20, carryOverMonths: 1, cancellationDeadlineBusinessDays: 4,
    });
  });

  it('parses Swedish comma decimals (§5: expense 1 200 + 10 % mark-up)', () => {
    const body = termsFormToBody({
      ...base, hourlyRate: '1250,50', expenseMarkupPercent: '10', minBillableHoursPerDay: '7,5',
    });
    expect(body.hourlyRate).toBe(1250.5);
    expect(body.expenseMarkupPercent).toBe(10);
    expect(body.minBillableHoursPerDay).toBe(7.5);
  });

  it('only persists fields of the chosen components', () => {
    const hourly = termsFormToBody({ ...base, timeBilling: 'hourly', hourlyRate: '100', dayRate: '900' });
    expect(hourly.hourlyRate).toBe(100);
    expect(hourly.dayRate).toBeNull();
    const daily = termsFormToBody({ ...base, timeBilling: 'dailyRate', hourlyRate: '100', dayRate: '900,5' });
    expect(daily.hourlyRate).toBeNull();
    expect(daily.minBillableHoursPerDay).toBeNull();
    expect(daily.dayRate).toBe(900.5);
    const noPad = termsFormToBody({ ...base, padEnabled: false, padRate: '800' });
    expect(noPad.padRate).toBeNull();
    const fixedOff = termsFormToBody({ ...base, fixedPriceEnabled: false, fixedPriceAmount: '5000' });
    expect(fixedOff.fixedPriceAmount).toBeNull();
  });

  it('carry-over months is at least 1; empty ladder rows are dropped', () => {
    const body = termsFormToBody({
      ...base, carryOverMonths: '0',
      cancellationSteps: [
        { minBusinessDaysBefore: '4', chargeHours: '', chargeAmount: '' },
        { minBusinessDaysBefore: '2', chargeHours: '4', chargeAmount: '' },
        { minBusinessDaysBefore: '', chargeHours: '8', chargeAmount: '' },
      ],
    });
    expect(body.carryOverMonths).toBe(1);
    expect(body.cancellationSteps).toEqual([
      { minBusinessDaysBefore: 4, chargeHours: null, chargeAmount: null },
      { minBusinessDaysBefore: 2, chargeHours: 4, chargeAmount: null },
    ]);
  });
});

describe('fromTerms', () => {
  it('round-trips a version into the form and back', () => {
    const v: ContractTerms = {
      ...termsFormToBody({ ...base, padEnabled: true, padRate: '800', carryOverMaxPercent: '0' }),
      id: 't', contractId: 'c', version: 3, createdUtc: '2026-01-01T00:00:00Z',
    };
    const form = fromTerms(v, '2026-10-20');
    expect(form.effectiveFrom).toBe('2026-10-20');
    expect(form.carryOverMaxPercent).toBe('0');
    expect(form.padRate).toBe('800');
    expect(form.hourlyRate).toBe('');
    const again = termsFormToBody(form);
    expect(again.carryOverMaxPercent).toBe(0);
    expect(again.padRate).toBe(800);
  });
});
