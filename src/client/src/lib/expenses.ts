import { errorCode } from './errors';
import type { ExpenseRequestBody } from './types';

/** Form-level expense rules mirroring the server (§6.2): included-in-rate
 * excludes reimburse/recharge; a recharge needs a project to land on an
 * invoice specification. Returns i18n keys of the violated rules. */
export function expenseRuleErrors(b: {
  projectId?: string | null;
  reimbursableToResource: boolean;
  rechargeableToCustomer: boolean;
  includedInRate: boolean;
}): string[] {
  const errs: string[] = [];
  if (b.includedInRate && (b.reimbursableToResource || b.rechargeableToCustomer))
    errs.push('myWork.ruleInRateExclusive');
  if (b.rechargeableToCustomer && !b.projectId)
    errs.push('myWork.ruleRechargeNeedsProject');
  return errs;
}

/**
 * Saves an expense; when the server warns `possibleDuplicate` (same date,
 * amount and currency already registered) asks the user and resends with
 * `confirmDuplicate: true`. Resolves null when the user declines.
 */
export async function saveExpenseConfirmingDuplicates<T>(
  body: ExpenseRequestBody,
  save: (b: ExpenseRequestBody) => Promise<T>,
  confirm: () => boolean,
): Promise<T | null> {
  try {
    return await save(body);
  } catch (e) {
    if (errorCode(e) !== 'possibleDuplicate') throw e;
    if (!confirm()) return null;
    return save({ ...body, confirmDuplicate: true });
  }
}
