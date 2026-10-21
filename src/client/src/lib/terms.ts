// Contract-terms form ⇄ API body mapping — pure so the commercial rules (zero
// survives, comma decimals, only the chosen components persist) are covered
// by Vitest (AGENTS.md testing discipline).
import { numOr, numOrNull } from './versioned';
import type {
  CarryOverBasis, ContractTerms, ContractTermsRequestBody, PadInvoicing,
  SharedDayMinimum, TimeBilling,
} from './types';

/** Cancellation-ladder row in the form — strings while editing. */
export type StepRow = {
  minBusinessDaysBefore: string;
  chargeHours: string;
  chargeAmount: string;
  /** Share of the day rate (0–1) — day-rate contracts. */
  chargeDayFraction: string;
};

/** "08:00:00" / "08:00" → "08:00" for a time input; '' stays ''. */
export const hhmm = (t: string | null | undefined): string => (t ? t.slice(0, 5) : '');

/** Time input value → API TimeOnly ("HH:mm:00"); empty → the fallback. */
export const toTimeOnly = (v: string, fallback = '08:00'): string => {
  const m = /^(\d{1,2}):(\d{2})/.exec(v.trim());
  const [h, min] = m ? [Number(m[1]), Number(m[2])] : fallback.split(':').map(Number);
  const ok = h >= 0 && h < 24 && min >= 0 && min < 60;
  const [hh, mm] = ok ? [h, min] : fallback.split(':').map(Number);
  return `${String(hh).padStart(2, '0')}:${String(mm).padStart(2, '0')}:00`;
};

/** A share of the day rate must lie within 0–1 (null = not set). */
export const clampFraction = (v: number | null): number | null =>
  v === null ? null : Math.min(1, Math.max(0, v));

export type TermsForm = {
  effectiveFrom: string;
  currency: string;
  timeBilling: TimeBilling;
  padEnabled: boolean;
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
  includedVisits: string;
  // ── PAD rules ──
  carryOverBasis: CarryOverBasis;
  carryOverRoundUpTo: string;
  padInvoicing: PadInvoicing;
  additionalPadRate: string;
  sharedDayMinimum: SharedDayMinimum;
  unbookedTimeConsumesPad: boolean;
  unbookedTimeIncrementMinutes: string;
  defaultStartTime: string;
  startTimeNoticeBusinessDays: string;
};

export const emptyTerms: TermsForm = {
  effectiveFrom: '', currency: 'SEK',
  timeBilling: 'hourly', padEnabled: false,
  padRate: '', hourlyRate: '', saturdayRate: '', sundayRate: '', holidayRate: '',
  overtimeAfterHours: '8', overtimePercent: '150',
  mobilisationFee: '', expenseMarkupPercent: '0', minBillableHoursPerDay: '',
  dayRate: '', saturdayDayRate: '', sundayDayRate: '', holidayDayRate: '',
  halfDayRate: '', halfDayMaxHours: '',
  padHoursPerDay: '8', minPadPerBookedDay: '1', carryOverMaxPercent: '20',
  carryOverMonths: '1', cancellationDeadlineBusinessDays: '4',
  cancellationSteps: [], holidayCalendar: 'SE',
  travelExpensePolicy: '', indexation: '', notes: '',
  includedVisits: '',
  carryOverBasis: 'unusedBaseline', carryOverRoundUpTo: '1',
  padInvoicing: 'utilised', additionalPadRate: '',
  sharedDayMinimum: 'proportional',
  unbookedTimeConsumesPad: false, unbookedTimeIncrementMinutes: '15',
  defaultStartTime: '08:00', startTimeNoticeBusinessDays: '4',
};

const s = (n: number | null) => (n === null ? '' : String(n));

/** Prefill a new version from the version currently in force — corrections
 * start from today's values instead of a blank slate. */
export const fromTerms = (v: ContractTerms, effectiveFrom: string): TermsForm => ({
  effectiveFrom, currency: v.currency,
  timeBilling: v.timeBilling, padEnabled: v.padEnabled,
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
  padHoursPerDay: String(v.padHoursPerDay),
  minPadPerBookedDay: String(v.minPadPerBookedDay),
  carryOverMaxPercent: String(v.carryOverMaxPercent),
  carryOverMonths: String(v.carryOverMonths),
  cancellationDeadlineBusinessDays: String(v.cancellationDeadlineBusinessDays),
  cancellationSteps: v.cancellationSteps.map((st) => ({
    minBusinessDaysBefore: String(st.minBusinessDaysBefore),
    chargeHours: s(st.chargeHours),
    chargeAmount: s(st.chargeAmount),
    chargeDayFraction: s(st.chargeDayFraction ?? null),
  })),
  holidayCalendar: v.holidayCalendar,
  travelExpensePolicy: v.travelExpensePolicy ?? '', indexation: v.indexation ?? '',
  notes: v.notes ?? '',
  includedVisits: s(v.includedVisits ?? null),
  carryOverBasis: v.padRules?.carryOverBasis ?? emptyTerms.carryOverBasis,
  carryOverRoundUpTo: v.padRules ? String(v.padRules.carryOverRoundUpTo) : emptyTerms.carryOverRoundUpTo,
  padInvoicing: v.padRules?.padInvoicing ?? emptyTerms.padInvoicing,
  additionalPadRate: s(v.padRules?.additionalPadRate ?? null),
  sharedDayMinimum: v.padRules?.sharedDayMinimum ?? emptyTerms.sharedDayMinimum,
  unbookedTimeConsumesPad: v.padRules?.unbookedTimeConsumesPad ?? false,
  unbookedTimeIncrementMinutes: v.padRules
    ? String(v.padRules.unbookedTimeIncrementMinutes) : emptyTerms.unbookedTimeIncrementMinutes,
  defaultStartTime: hhmm(v.padRules?.defaultStartTime) || emptyTerms.defaultStartTime,
  startTimeNoticeBusinessDays: v.padRules
    ? String(v.padRules.startTimeNoticeBusinessDays) : emptyTerms.startTimeNoticeBusinessDays,
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
        chargeDayFraction: isDaily ? clampFraction(numOrNull(st.chargeDayFraction)) : null,
      }))
      .filter((st) => st.minBusinessDaysBefore > 0),
    holidayCalendar: tf.holidayCalendar,
    travelExpensePolicy: tf.travelExpensePolicy || null,
    indexation: tf.indexation || null,
    notes: tf.notes || null,
    includedVisits: numOrNull(tf.includedVisits),
    // PAD rules travel with every version (defaults when PAD is off) — the
    // server stores them regardless, so a later PAD switch-on starts sane.
    carryOverBasis: tf.carryOverBasis,
    carryOverRoundUpTo: numOr(tf.carryOverRoundUpTo, 1),
    padInvoicing: tf.padInvoicing,
    additionalPadRate: tf.padEnabled ? numOrNull(tf.additionalPadRate) : null,
    sharedDayMinimum: tf.sharedDayMinimum,
    unbookedTimeConsumesPad: tf.padEnabled && tf.unbookedTimeConsumesPad,
    unbookedTimeIncrementMinutes: numOr(tf.unbookedTimeIncrementMinutes, 15),
    defaultStartTime: toTimeOnly(tf.defaultStartTime),
    startTimeNoticeBusinessDays: numOr(tf.startTimeNoticeBusinessDays, 4),
  };
}

/** i18n keys (+ params) summarising the non-default PAD rules of a version
 * for the versions table — defaults stay silent. */
export function padRuleTags(v: ContractTerms): { key: string; params?: Record<string, unknown> }[] {
  const tags: { key: string; params?: Record<string, unknown> }[] = [];
  const r = v.padRules;
  if (v.padEnabled && r) {
    tags.push({ key: r.carryOverBasis === 'baseline' ? 'terms.tagCarryBaseline' : 'terms.tagCarryUnused' });
    if (r.padInvoicing === 'allocationConsumed') tags.push({ key: 'terms.tagLumpSum' });
    if (r.sharedDayMinimum === 'perProject') tags.push({ key: 'terms.tagSharedPerProject' });
    if (r.unbookedTimeConsumesPad) tags.push({ key: 'terms.tagUnbooked' });
  }
  if (v.includedVisits !== null && v.includedVisits !== undefined)
    tags.push({ key: 'terms.tagVisits', params: { count: v.includedVisits } });
  return tags;
}
