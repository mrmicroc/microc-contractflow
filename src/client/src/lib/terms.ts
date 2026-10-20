// Contract-terms form ⇄ API body mapping — pure so the commercial rules (zero
// survives, comma decimals, only the chosen components persist) are covered
// by Vitest (AGENTS.md testing discipline).
import { numOr, numOrNull } from './versioned';
import type { ContractTerms, ContractTermsRequestBody, TimeBilling } from './types';

/** Cancellation-ladder row in the form — strings while editing. */
export type StepRow = {
  minBusinessDaysBefore: string;
  chargeHours: string;
  chargeAmount: string;
};

export type TermsForm = {
  effectiveFrom: string;
  currency: string;
  timeBilling: TimeBilling;
  padEnabled: boolean;
  fixedPriceEnabled: boolean;
  padRate: string;
  hourlyRate: string;
  saturdayRate: string;
  sundayRate: string;
  holidayRate: string;
  overtimeAfterHours: string;
  overtimePercent: string;
  mobilisationFee: string;
  expenseMarkupPercent: string;
  minBillableHoursPerDay: string;
  dayRate: string;
  saturdayDayRate: string;
  sundayDayRate: string;
  holidayDayRate: string;
  halfDayRate: string;
  halfDayMaxHours: string;
  fixedPriceAmount: string;
  fixedPriceDate: string;
  fixedPriceNote: string;
  padHoursPerDay: string;
  minPadPerBookedDay: string;
  carryOverMaxPercent: string;
  carryOverMonths: string;
  cancellationDeadlineBusinessDays: string;
  cancellationSteps: StepRow[];
  holidayCalendar: string;
  travelExpensePolicy: string;
  indexation: string;
  notes: string;
};

export const emptyTerms: TermsForm = {
  effectiveFrom: '', currency: 'SEK',
  timeBilling: 'hourly', padEnabled: false, fixedPriceEnabled: false,
  padRate: '', hourlyRate: '', saturdayRate: '', sundayRate: '', holidayRate: '',
  overtimeAfterHours: '8', overtimePercent: '150',
  mobilisationFee: '', expenseMarkupPercent: '0', minBillableHoursPerDay: '',
  dayRate: '', saturdayDayRate: '', sundayDayRate: '', holidayDayRate: '',
  halfDayRate: '', halfDayMaxHours: '',
  fixedPriceAmount: '', fixedPriceDate: '', fixedPriceNote: '',
  padHoursPerDay: '8', minPadPerBookedDay: '1', carryOverMaxPercent: '20',
  carryOverMonths: '1', cancellationDeadlineBusinessDays: '4',
  cancellationSteps: [], holidayCalendar: 'SE',
  travelExpensePolicy: '', indexation: '', notes: '',
};

const s = (n: number | null) => (n === null ? '' : String(n));

/** Prefill a new version from the version currently in force — corrections
 * start from today's values instead of a blank slate. */
export const fromTerms = (v: ContractTerms, effectiveFrom: string): TermsForm => ({
  effectiveFrom, currency: v.currency,
  timeBilling: v.timeBilling, padEnabled: v.padEnabled,
  fixedPriceEnabled: v.fixedPriceEnabled,
  padRate: s(v.padRate), hourlyRate: s(v.hourlyRate),
  saturdayRate: s(v.saturdayRate), sundayRate: s(v.sundayRate),
  holidayRate: s(v.holidayRate),
  overtimeAfterHours: String(v.overtimeAfterHours),
  overtimePercent: String(v.overtimePercent),
  mobilisationFee: s(v.mobilisationFee),
  expenseMarkupPercent: String(v.expenseMarkupPercent),
  minBillableHoursPerDay: s(v.minBillableHoursPerDay),
  dayRate: s(v.dayRate),
  saturdayDayRate: s(v.saturdayDayRate),
  sundayDayRate: s(v.sundayDayRate),
  holidayDayRate: s(v.holidayDayRate),
  halfDayRate: s(v.halfDayRate),
  halfDayMaxHours: s(v.halfDayMaxHours),
  fixedPriceAmount: s(v.fixedPriceAmount),
  fixedPriceDate: v.fixedPriceDate ?? '', fixedPriceNote: v.fixedPriceNote ?? '',
  padHoursPerDay: String(v.padHoursPerDay),
  minPadPerBookedDay: String(v.minPadPerBookedDay),
  carryOverMaxPercent: String(v.carryOverMaxPercent),
  carryOverMonths: String(v.carryOverMonths),
  cancellationDeadlineBusinessDays: String(v.cancellationDeadlineBusinessDays),
  cancellationSteps: v.cancellationSteps.map((st) => ({
    minBusinessDaysBefore: String(st.minBusinessDaysBefore),
    chargeHours: s(st.chargeHours),
    chargeAmount: s(st.chargeAmount),
  })),
  holidayCalendar: v.holidayCalendar,
  travelExpensePolicy: v.travelExpensePolicy ?? '', indexation: v.indexation ?? '',
  notes: v.notes ?? '',
});

/**
 * API body for a new terms version. Fields the chosen components don't use
 * stay editable in the form but are never persisted — the saved version must
 * not carry dead rates that imply a different model. Defaults apply only to
 * empty fields: an explicit 0 (no carry-over, no minimum PAD, 0-day deadline)
 * survives, and Swedish comma decimals parse.
 */
export function termsFormToBody(tf: TermsForm): ContractTermsRequestBody {
  const isHourly = tf.timeBilling === 'hourly';
  const isDaily = tf.timeBilling === 'dailyRate';
  return {
    effectiveFrom: tf.effectiveFrom,
    currency: tf.currency,
    timeBilling: tf.timeBilling,
    padEnabled: tf.padEnabled,
    fixedPriceEnabled: tf.fixedPriceEnabled,
    padRate: tf.padEnabled ? numOrNull(tf.padRate) : null,
    hourlyRate: isHourly ? numOrNull(tf.hourlyRate) : null,
    saturdayRate: isHourly ? numOrNull(tf.saturdayRate) : null,
    sundayRate: isHourly ? numOrNull(tf.sundayRate) : null,
    holidayRate: isHourly ? numOrNull(tf.holidayRate) : null,
    overtimeAfterHours: numOr(tf.overtimeAfterHours, 8),
    overtimePercent: numOr(tf.overtimePercent, 150),
    mobilisationFee: numOrNull(tf.mobilisationFee),
    expenseMarkupPercent: numOr(tf.expenseMarkupPercent, 0),
    minBillableHoursPerDay: isHourly ? numOrNull(tf.minBillableHoursPerDay) : null,
    dayRate: isDaily ? numOrNull(tf.dayRate) : null,
    saturdayDayRate: isDaily ? numOrNull(tf.saturdayDayRate) : null,
    sundayDayRate: isDaily ? numOrNull(tf.sundayDayRate) : null,
    holidayDayRate: isDaily ? numOrNull(tf.holidayDayRate) : null,
    halfDayRate: isDaily ? numOrNull(tf.halfDayRate) : null,
    halfDayMaxHours: isDaily ? numOrNull(tf.halfDayMaxHours) : null,
    fixedPriceAmount: tf.fixedPriceEnabled ? numOrNull(tf.fixedPriceAmount) : null,
    fixedPriceDate: tf.fixedPriceEnabled ? tf.fixedPriceDate || null : null,
    fixedPriceNote: tf.fixedPriceEnabled ? tf.fixedPriceNote || null : null,
    padHoursPerDay: numOr(tf.padHoursPerDay, 8),
    minPadPerBookedDay: numOr(tf.minPadPerBookedDay, 1),
    carryOverMaxPercent: numOr(tf.carryOverMaxPercent, 20),
    carryOverMonths: Math.max(1, numOr(tf.carryOverMonths, 1)),
    cancellationDeadlineBusinessDays: numOr(tf.cancellationDeadlineBusinessDays, 4),
    cancellationSteps: tf.cancellationSteps
      .map((st) => ({
        minBusinessDaysBefore: numOr(st.minBusinessDaysBefore, 0),
        chargeHours: numOrNull(st.chargeHours),
        chargeAmount: numOrNull(st.chargeAmount),
      }))
      .filter((st) => st.minBusinessDaysBefore > 0),
    holidayCalendar: tf.holidayCalendar,
    travelExpensePolicy: tf.travelExpensePolicy || null,
    indexation: tf.indexation || null,
    notes: tf.notes || null,
  };
}
