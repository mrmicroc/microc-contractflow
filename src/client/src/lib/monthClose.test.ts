import { describe, expect, it } from 'vitest';
import { blockedPreview, canSubmitForce, closeView, type ClosePreview } from './monthClose';

const result = {
  baseline: 10, utilised: 11, releasedOpenCommitments: 0, carryOverOut: 1,
  expired: 0, carryOverInNextMonth: 1, nextYear: 2026, nextMonth: 3,
};

const preview = (over: Partial<ClosePreview> = {}): ClosePreview => ({
  year: 2026, month: 2, canClose: true, orderingErrors: [], checklist: [],
  steps: [
    { code: 'baseline', amount: 10, explanation: 'Baseline 10' },
    { code: 'carryOverIn', amount: 2, explanation: 'Carry-over in 2' },
    { code: 'carryOverOut', amount: 1, explanation: 'Carry-over out 1' },
  ],
  result,
  ...over,
});

describe('closeView', () => {
  it('offers a plain close to an admin when nothing blocks', () => {
    const v = closeView(preview(), true);
    expect(v.mode).toBe('close');
    expect(v.steps.map((s) => s.code)).toEqual(['baseline', 'carryOverIn', 'carryOverOut']);
  });

  it('splits blocking items from warnings and offers force to an admin', () => {
    const v = closeView(preview({
      canClose: false,
      checklist: [
        { code: 'unresolvedBookings', count: 2, blocking: true },
        { code: 'planningLeftovers', count: 1, blocking: false },
      ],
    }), true);
    expect(v.mode).toBe('force');
    expect(v.blocking.map((c) => c.code)).toEqual(['unresolvedBookings']);
    expect(v.warnings.map((c) => c.code)).toEqual(['planningLeftovers']);
  });

  it('warnings alone still allow a plain close', () => {
    const v = closeView(preview({
      checklist: [{ code: 'planningLeftovers', count: 3, blocking: false }],
    }), true);
    expect(v.mode).toBe('close');
  });

  it('never offers a close past ordering errors, even with force', () => {
    const v = closeView(preview({
      canClose: false,
      orderingErrors: ['previousMonthOpen'],
      checklist: [{ code: 'unsubmittedTime', count: 1, blocking: true }],
    }), true);
    expect(v.mode).toBe('none');
    expect(v.orderingErrors).toEqual(['previousMonthOpen']);
  });

  it('flags an already closed month', () => {
    expect(closeView(preview({ orderingErrors: ['alreadyClosed'] }), true).alreadyClosed).toBe(true);
  });

  it('offers nothing to non-admins (allowed vs forbidden path)', () => {
    expect(closeView(preview(), false).mode).toBe('none');
    expect(closeView(preview(), true).mode).toBe('close');
  });
});

describe('canSubmitForce', () => {
  it('requires a non-blank reason', () => {
    expect(canSubmitForce('')).toBe(false);
    expect(canSubmitForce('   ')).toBe(false);
    expect(canSubmitForce('Customer confirmed by mail')).toBe(true);
  });
});

describe('blockedPreview', () => {
  it('extracts the preview from a 409 closeBlocked body', () => {
    const body = { error: 'closeBlocked', message: 'The month cannot be closed yet.', preview: preview({ canClose: false }) };
    const p = blockedPreview(new Error(JSON.stringify(body)));
    expect(p?.year).toBe(2026);
    expect(p?.canClose).toBe(false);
  });

  it('returns null for other errors', () => {
    expect(blockedPreview(new Error(JSON.stringify({ error: 'monthClosed' })))).toBeNull();
    expect(blockedPreview(new Error('plain text'))).toBeNull();
  });
});
