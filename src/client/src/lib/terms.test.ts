import { describe, expect, it } from 'vitest';
import {
  clampFraction, emptyTerms, fromTerms, hhmm, padRuleTags, termsFormToBody, toTimeOnly,
} from './terms';
import type { ContractTerms, ContractTermsRequestBody, PadRules } from './types';

const base = { ...emptyTerms, effectiveFrom: '2026-01-01' };
const step = (min: string, hours = '', amount = '', fraction = '', percent = '') =>  ({ minBusinessDaysBefore: min, chargeHours: hours, chargeAmount: amount, chargeDayFraction: fraction, chargePercent: percent });

/** A stored version as the API returns it: PAD rules nested, not top-level. */
function asStored(body: ContractTermsRequestBody, version = 3): ContractTerms {
  const {
    carryOverBasis, carryOverRounding, carryOverRoundingStep, carryOverMaxPad, carryOverConsumption,
    padInvoicing, additionalPadRate, sharedDayMinimum,
    unbookedTimeConsumesPad, unbookedTimeIncrementMinutes, defaultStartTime,
    startTimeNoticeBusinessDays, ...rest
  } = body;
  const padRules: PadRules = {
    carryOverBasis, carryOverRounding, carryOverRoundingStep, carryOverMaxPad, carryOverConsumption,
    padInvoicing, additionalPadRate, sharedDayMinimum,
    unbookedTimeConsumesPad, unbookedTimeIncrementMinutes, defaultStartTime,
    startTimeNoticeBusinessDays,
  };
  return { ...rest, padRules, id: 't', contractId: 'c', version, createdUtc: '2026-01-01T00:00:00Z' };
}

describe('termsFormToBody', () => {
  it('an explicit zero survives (no Number(x) || default fallbacks)', () => {
    const body = termsFormToBody({
      ...base, padEnabled: true,
      carryOverMaxPercent: '0', minPadPerBookedDay: '0', overtimePercent: '0',
      cancellationDeadlineBusinessDays: '0', expenseMarkupPercent: '0',
      carryOverRoundingStep: '0', startTimeNoticeBusinessDays: '0', includedVisits: '0',
    });
    expect(body.carryOverMaxPercent).toBe(0);
    expect(body.minPadPerBookedDay).toBe(0);
    expect(body.overtimePercent).toBe(0);
    expect(body.cancellationDeadlineBusinessDays).toBe(0);
    expect(body.expenseMarkupPercent).toBe(0);
    expect(body.carryOverRoundingStep).toBe(0);
    expect(body.startTimeNoticeBusinessDays).toBe(0);
    expect(body.includedVisits).toBe(0);
  });

  it('empty rule fields fall back to the contract defaults', () => {
    const body = termsFormToBody({
      ...base, overtimeAfterHours: '', overtimePercent: '', padHoursPerDay: '',
      minPadPerBookedDay: '', carryOverMaxPercent: '', carryOverMonths: '',
      cancellationDeadlineBusinessDays: '', carryOverRoundingStep: '',
      unbookedTimeIncrementMinutes: '', startTimeNoticeBusinessDays: '', defaultStartTime: '',
    });
    expect(body).toMatchObject({
      overtimeAfterHours: 8, overtimePercent: 150, padHoursPerDay: 8, minPadPerBookedDay: 1,
      carryOverMaxPercent: 20, carryOverMonths: 1, cancellationDeadlineBusinessDays: 4,
      carryOverRoundingStep: 1, unbookedTimeIncrementMinutes: 15, startTimeNoticeBusinessDays: 4,
      defaultStartTime: '08:00:00', includedVisits: null,
    });
  });

  it('defaults reproduce the lump-sum document rules except the two opt-ins', () => {
    const body = termsFormToBody(base);
    expect(body).toMatchObject({
      carryOverBasis: 'unusedBaseline', padInvoicing: 'utilised',
      sharedDayMinimum: 'proportional', unbookedTimeConsumesPad: false,
    });
  });

  it('parses Swedish comma decimals (§5: expense 1 200 + 10 % mark-up)', () => {
    const body = termsFormToBody({
      ...base, hourlyRate: '1250,50', expenseMarkupPercent: '10', minBillableHoursPerDay: '7,5',
      padEnabled: true, additionalPadRate: '9500,5', carryOverRoundingStep: '0,5',
    });
    expect(body.hourlyRate).toBe(1250.5);
    expect(body.expenseMarkupPercent).toBe(10);
    expect(body.minBillableHoursPerDay).toBe(7.5);
    expect(body.additionalPadRate).toBe(9500.5);
    expect(body.carryOverRoundingStep).toBe(0.5);
  });

  it('only persists fields of the chosen components', () => {
    const hourly = termsFormToBody({ ...base, timeBilling: 'hourly', hourlyRate: '100', dayRate: '900' });
    expect(hourly.hourlyRate).toBe(100);
    expect(hourly.dayRate).toBeNull();
    const daily = termsFormToBody({ ...base, timeBilling: 'dailyRate', hourlyRate: '100', dayRate: '900,5' });
    expect(daily.hourlyRate).toBeNull();
    expect(daily.minBillableHoursPerDay).toBeNull();
    expect(daily.dayRate).toBe(900.5);
    const noPad = termsFormToBody({
      ...base, padEnabled: false, padRate: '800', additionalPadRate: '900', unbookedTimeConsumesPad: true,
    });
    expect(noPad.padRate).toBeNull();
    expect(noPad.additionalPadRate).toBeNull();
    expect(noPad.unbookedTimeConsumesPad).toBe(false);
    expect(noPad).not.toHaveProperty('fixedPriceAmount');
  });

  it('carry-over months is at least 1; empty ladder rows are dropped', () => {
    const body = termsFormToBody({
      ...base, carryOverMonths: '0',
      cancellationSteps: [step('4'), step('2', '4'), step('', '8')],
    });
    expect(body.carryOverMonths).toBe(1);
    expect(body.cancellationSteps).toEqual([
      { minBusinessDaysBefore: 4, chargeHours: null, chargeAmount: null, chargeDayFraction: null, chargePercent: null },
      { minBusinessDaysBefore: 2, chargeHours: 4, chargeAmount: null, chargeDayFraction: null, chargePercent: null },
    ]);
  });

  it('day fraction on the ladder persists only on day-rate terms, clamped to 0–1', () => {
    const daily = termsFormToBody({
      ...base, timeBilling: 'dailyRate', cancellationSteps: [step('2', '', '', '0,5'), step('1', '', '', '3')],
    });
    expect(daily.cancellationSteps.map((s) => s.chargeDayFraction)).toEqual([0.5, 1]);
    const hourly = termsFormToBody({ ...base, cancellationSteps: [step('2', '', '', '0,5')] });
    expect(hourly.cancellationSteps[0].chargeDayFraction).toBeNull();
  });
});

describe('time helpers', () => {
  it('hhmm trims server TimeOnly to a time-input value', () => {
    expect(hhmm('08:00:00')).toBe('08:00');
    expect(hhmm('07:30')).toBe('07:30');
    expect(hhmm(null)).toBe('');
  });

  it('toTimeOnly normalises input and falls back on garbage', () => {
    expect(toTimeOnly('7:30')).toBe('07:30:00');
    expect(toTimeOnly('')).toBe('08:00:00');
    expect(toTimeOnly('25:00')).toBe('08:00:00');
    expect(toTimeOnly('x', '06:15')).toBe('06:15:00');
  });

  it('clampFraction keeps a day share within 0–1', () => {
    expect(clampFraction(-1)).toBe(0);
    expect(clampFraction(0.25)).toBe(0.25);
    expect(clampFraction(2)).toBe(1);
    expect(clampFraction(null)).toBeNull();
  });
});

describe('fromTerms', () => {
  it('round-trips a version into the form and back', () => {
    const v = asStored(termsFormToBody({ ...base, padEnabled: true, padRate: '800', carryOverMaxPercent: '0' }));
    const form = fromTerms(v, '2026-10-20');
    expect(form.effectiveFrom).toBe('2026-10-20');
    expect(form.carryOverMaxPercent).toBe('0');
    expect(form.padRate).toBe('800');
    expect(form.hourlyRate).toBe('');
    const again = termsFormToBody(form);
    expect(again.carryOverMaxPercent).toBe(0);
    expect(again.padRate).toBe(800);
  });

  it('round-trips the PAD rules (nested on the stored version)', () => {
    const v = asStored(termsFormToBody({
      ...base, padEnabled: true, padRate: '1000',
      carryOverBasis: 'baseline', carryOverRounding: 'nearest', carryOverRoundingStep: '0,5',
      carryOverMaxPad: '2,5', carryOverConsumption: 'baselineFirst', padInvoicing: 'allocationConsumed',
      additionalPadRate: '1200', sharedDayMinimum: 'perProject', unbookedTimeConsumesPad: true,
      unbookedTimeIncrementMinutes: '30', defaultStartTime: '07:00', startTimeNoticeBusinessDays: '3',
      includedVisits: '20',
    }));
    const form = fromTerms(v, '2026-11-01');
    expect(form).toMatchObject({
      carryOverBasis: 'baseline', carryOverRounding: 'nearest', carryOverRoundingStep: '0.5',
      carryOverMaxPad: '2.5', carryOverConsumption: 'baselineFirst', padInvoicing: 'allocationConsumed',
      additionalPadRate: '1200', sharedDayMinimum: 'perProject', unbookedTimeConsumesPad: true,
      unbookedTimeIncrementMinutes: '30', defaultStartTime: '07:00', startTimeNoticeBusinessDays: '3',
      includedVisits: '20',
    });
    expect(termsFormToBody(form)).toMatchObject({ padInvoicing: 'allocationConsumed', includedVisits: 20 });
  });

  it('a version without PAD rules (older API shape) prefills the defaults', () => {
    const v = { ...asStored(termsFormToBody(base)), padRules: null };
    const form = fromTerms(v, '2026-11-01');
    expect(form.carryOverBasis).toBe('unusedBaseline');
    expect(form.defaultStartTime).toBe('08:00');
  });
});

describe('padRuleTags', () => {
  it('names the non-default rules of a PAD version', () => {
    const v = asStored(termsFormToBody({
      ...base, padEnabled: true, padInvoicing: 'allocationConsumed', unbookedTimeConsumesPad: true,
      sharedDayMinimum: 'perProject', includedVisits: '20',
    }));
    expect(padRuleTags(v).map((x) => x.key)).toEqual([
      'terms.tagCarryUnused', 'terms.tagLumpSum', 'terms.tagSharedPerProject',
      'terms.tagUnbooked', 'terms.tagVisits',
    ]);
    expect(padRuleTags(v).at(-1)?.params).toEqual({ count: 20 });
  });

  it('a version without PAD shows no PAD rule tags', () => {
    expect(padRuleTags(asStored(termsFormToBody(base)))).toEqual([]);
  });
});

describe('carry-over settings', () => {
  it('an empty ceiling means no ceiling; defaults round up to whole PAD, carry first', () => {
    const body = termsFormToBody(emptyTerms);
    expect(body.carryOverMaxPad).toBeNull();
    expect(body.carryOverRounding).toBe('up');
    expect(body.carryOverRoundingStep).toBe(1);
    expect(body.carryOverConsumption).toBe('carryOverFirst');
  });
});

describe('cancellation percent rungs', () => {
  it('keeps the percentage and clamps it to 0–100', () => {
    const body = termsFormToBody({
      ...emptyTerms, effectiveFrom: '2026-01-01',
      cancellationSteps: [
        { minBusinessDaysBefore: '5', chargeHours: '', chargeAmount: '', chargeDayFraction: '', chargePercent: '50' },
        { minBusinessDaysBefore: '2', chargeHours: '', chargeAmount: '', chargeDayFraction: '', chargePercent: '150' },
      ],
    });
    expect(body.cancellationSteps.map((s) => s.chargePercent)).toEqual([50, 100]);
  });
});
