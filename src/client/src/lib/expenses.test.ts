import { describe, expect, it, vi } from 'vitest';
import { expenseRuleErrors, saveExpenseConfirmingDuplicates } from './expenses';
import type { ExpenseRequestBody } from './types';

const flags = { reimbursableToResource: false, rechargeableToCustomer: false, includedInRate: false };
const body: ExpenseRequestBody = {
  projectId: 'p', date: '2026-10-20', category: 'material', amount: 1200, currency: 'SEK', ...flags,
  rechargeableToCustomer: true,
};
const dupError = new Error(JSON.stringify({
  error: 'possibleDuplicate', message: 'An expense with the same date, amount and currency already exists.',
  duplicates: ['e1'],
}));

describe('expenseRuleErrors', () => {
  it('included-in-rate excludes reimburse and recharge', () => {
    expect(expenseRuleErrors({ ...flags, includedInRate: true, reimbursableToResource: true }))
      .toEqual(['myWork.ruleInRateExclusive']);
    expect(expenseRuleErrors({ ...flags, includedInRate: true })).toEqual([]);
  });

  it('a recharge needs a project', () => {
    expect(expenseRuleErrors({ ...flags, rechargeableToCustomer: true, projectId: null }))
      .toEqual(['myWork.ruleRechargeNeedsProject']);
    expect(expenseRuleErrors({ ...flags, rechargeableToCustomer: true, projectId: 'p' })).toEqual([]);
  });

  it('reimbursable + rechargeable together is allowed (independent flags)', () => {
    expect(expenseRuleErrors({
      ...flags, reimbursableToResource: true, rechargeableToCustomer: true, projectId: 'p',
    })).toEqual([]);
  });
});

describe('saveExpenseConfirmingDuplicates', () => {
  it('saves straight through when there is no duplicate', async () => {
    const save = vi.fn().mockResolvedValue('ok');
    const confirm = vi.fn();
    await expect(saveExpenseConfirmingDuplicates(body, save, confirm)).resolves.toBe('ok');
    expect(save).toHaveBeenCalledTimes(1);
    expect(confirm).not.toHaveBeenCalled();
  });

  it('on possibleDuplicate asks, then resends with confirmDuplicate', async () => {
    const save = vi.fn().mockRejectedValueOnce(dupError).mockResolvedValueOnce('saved');
    const result = await saveExpenseConfirmingDuplicates(body, save, () => true);
    expect(result).toBe('saved');
    expect(save).toHaveBeenLastCalledWith({ ...body, confirmDuplicate: true });
  });

  it('returns null and does not resend when the user declines', async () => {
    const save = vi.fn().mockRejectedValueOnce(dupError);
    await expect(saveExpenseConfirmingDuplicates(body, save, () => false)).resolves.toBeNull();
    expect(save).toHaveBeenCalledTimes(1);
  });

  it('rethrows other errors untouched', async () => {
    const other = new Error(JSON.stringify({ error: 'monthClosed', message: 'closed' }));
    const save = vi.fn().mockRejectedValue(other);
    await expect(saveExpenseConfirmingDuplicates(body, save, () => true)).rejects.toBe(other);
  });
});
