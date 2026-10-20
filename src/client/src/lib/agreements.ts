// Compensation-agreement form → API body. Pure so the pay-side rules (zero
// survives, comma decimals, scope cascade) are covered by Vitest.
import type { AgreementRequestBody } from './api';
import type { PerDiemPolicy } from './types';
import { numOr, numOrNull } from './versioned';

export type AgreementForm = {
  customerId: string;
  contractId: string;
  workPackageId: string;
  effectiveFrom: string;
  currency: string;
  hourlyRate: string;
  saturdayRate: string;
  sundayRate: string;
  holidayRate: string;
  overtimeAfterHours: string;
  overtimePercent: string;
  travelTimePercent: string;
  mileageRate: string;
  perDiem: PerDiemPolicy;
  perDiemAmount: string;
  expensePolicy: string;
  notes: string;
};

export const emptyAgreement: AgreementForm = {
  customerId: '', contractId: '', workPackageId: '', effectiveFrom: '', currency: 'SEK',
  hourlyRate: '', saturdayRate: '', sundayRate: '', holidayRate: '',
  overtimeAfterHours: '8', overtimePercent: '150',
  travelTimePercent: '', mileageRate: '', perDiem: 'none',
  perDiemAmount: '', expensePolicy: '', notes: '',
};

/** API body for a new agreement version. A work package only scopes when a
 * contract is chosen; the per-diem amount only persists on a custom policy;
 * defaults apply to empty fields only — 0 % overtime stays 0. */
export function agreementFormToBody(af: AgreementForm): AgreementRequestBody {
  return {
    contractId: af.contractId || null,
    workPackageId: af.contractId ? (af.workPackageId || null) : null,
    effectiveFrom: af.effectiveFrom,
    currency: af.currency.trim().toUpperCase(),
    hourlyRate: numOrNull(af.hourlyRate),
    saturdayRate: numOrNull(af.saturdayRate),
    sundayRate: numOrNull(af.sundayRate),
    holidayRate: numOrNull(af.holidayRate),
    overtimeAfterHours: numOr(af.overtimeAfterHours, 8),
    overtimePercent: numOr(af.overtimePercent, 150),
    travelTimePercent: numOrNull(af.travelTimePercent),
    mileageRate: numOrNull(af.mileageRate),
    perDiem: af.perDiem,
    perDiemAmount: af.perDiem === 'custom' ? numOrNull(af.perDiemAmount) : null,
    expensePolicy: af.expensePolicy.trim() || null,
    notes: af.notes.trim() || null,
  };
}
