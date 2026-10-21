import { describe, expect, it } from 'vitest';
import {
  approvalNeedsNote, canBackToDraft, generateMode, IRREVERSIBLE_STEPS, marginTotalsByCurrency,
  nextInvoiceStep, nextStatementStep, parseSupplierLines,
} from './economy';
import type { InvoiceSpecification, MarginRow } from './types';

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

const spec = (over: Partial<InvoiceSpecification>): InvoiceSpecification => ({
  id: 'i', projectId: 'p1', projectName: 'North Link', customerName: 'Example Org',
  endClientName: null, endClientReference: null,
  year: 2026, month: 10, revision: 0, status: 'draft', currency: 'SEK', totalAmount: 0,
  lineCount: 0, createdUtc: '', issuedUtc: null, issuedByUserId: null, paidUtc: null,
  paidByUserId: null, warnings: [], ...over,
});

describe('generateMode', () => {
  it('no spec for the project-month → new', () => {
    expect(generateMode([spec({ month: 9 }), spec({ projectId: 'p2' })], 'p1', 2026, 10)).toBe('new');
  });

  it('latest revision still a draft → regenerate', () => {
    expect(generateMode([spec({})], 'p1', 2026, 10)).toBe('regenerate');
    expect(generateMode(
      [spec({ status: 'issued' }), spec({ revision: 1, status: 'draft' })], 'p1', 2026, 10,
    )).toBe('regenerate');
  });

  it('latest revision beyond draft → supplement', () => {
    expect(generateMode([spec({ status: 'customerReview' })], 'p1', 2026, 10)).toBe('supplement');
    expect(generateMode(
      [spec({ status: 'paid' }), spec({ revision: 1, status: 'issued' })], 'p1', 2026, 10,
    )).toBe('supplement');
  });
});

describe('canBackToDraft', () => {
  it('only from customer review', () => {
    expect(canBackToDraft('customerReview')).toBe(true);
    expect(canBackToDraft('draft')).toBe(false);
    expect(canBackToDraft('final')).toBe(false);
    expect(canBackToDraft('issued')).toBe(false);
  });
});

const margin = (currency: string, billed: number, cost: number): MarginRow => ({
  projectId: `p-${currency}-${billed}`, projectName: 'North Link', customerName: 'Example Org',
  endClientId: 'o1', endClientName: 'Example Org',
  currency, billed, cost, margin: billed - cost, marginPercent: null, costBreakdown: null,
});

describe('marginTotalsByCurrency', () => {
  it('sums per currency and never across currencies', () => {
    const totals = marginTotalsByCurrency([
      margin('SEK', 1000.1, 400.05), margin('EUR', 100, 120), margin('SEK', 500.2, 100),
    ]);
    expect(totals).toEqual([
      { currency: 'EUR', billed: 100, cost: 120, margin: -20 },
      { currency: 'SEK', billed: 1500.3, cost: 500.05, margin: 1000.25 },
    ]);
  });

  it('no rows → no totals', () => {
    expect(marginTotalsByCurrency([])).toEqual([]);
  });
});

describe('approvalNeedsNote', () => {
  it('only a discrepancy needs a note', () => {
    expect(approvalNeedsNote('discrepancy')).toBe(true);
    expect(approvalNeedsNote('matched')).toBe(false);
  });
});
