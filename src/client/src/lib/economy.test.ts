import { describe, expect, it } from 'vitest';
import {
  IRREVERSIBLE_STEPS, nextInvoiceStep, nextStatementStep, parseSupplierLines,
} from './economy';

describe('nextInvoiceStep', () => {
  it('walks draft → customer review → final → issued → paid', () => {
    expect(nextInvoiceStep('draft')).toBe('review');
    expect(nextInvoiceStep('customerReview')).toBe('finalize');
    expect(nextInvoiceStep('final')).toBe('issue');
    expect(nextInvoiceStep('issued')).toBe('pay');
    expect(nextInvoiceStep('paid')).toBeNull();
  });
});

describe('nextStatementStep', () => {
  it('walks draft → final → paid', () => {
    expect(nextStatementStep('draft')).toBe('finalize');
    expect(nextStatementStep('final')).toBe('pay');
    expect(nextStatementStep('paid')).toBeNull();
  });

  it('finalize/issue/pay are irreversible, review is not', () => {
    expect(IRREVERSIBLE_STEPS.has('issue')).toBe(true);
    expect(IRREVERSIBLE_STEPS.has('pay')).toBe(true);
    expect(IRREVERSIBLE_STEPS.has('review')).toBe(false);
  });
});

describe('parseSupplierLines', () => {
  it('parses kind, comma or dot amounts and descriptions', () => {
    const r = parseSupplierLines('hours 4800,50 March work\nexpense 1200.25\n\n  mileage 250  ');
    expect(r).toEqual({
      ok: true,
      lines: [
        { kind: 'hours', amount: 4800.5, description: 'March work' },
        { kind: 'expense', amount: 1200.25, description: 'expense' },
        { kind: 'mileage', amount: 250, description: 'mileage' },
      ],
    });
  });

  it('accepts kinds case-insensitively', () => {
    const r = parseSupplierLines('TravelTime 600');
    expect(r.ok && r.lines[0].kind).toBe('travelTime');
  });

  it('reports unknown kinds and bad amounts by line number', () => {
    expect(parseSupplierLines('hours 100\nbonus 50\nexpense abc\nallowance')).toEqual({
      ok: false,
      errors: [
        { line: 2, reason: 'kind' },
        { line: 3, reason: 'amount' },
        { line: 4, reason: 'amount' },
      ],
    });
  });

  it('empty text yields no lines', () => {
    expect(parseSupplierLines('  \n ')).toEqual({ ok: true, lines: [] });
  });
});
