import { describe, expect, it } from 'vitest';
import { agreementFormToBody, emptyAgreement } from './agreements';

describe('agreementFormToBody', () => {
  it('parses comma decimals and keeps an explicit zero', () => {
    const body = agreementFormToBody({
      ...emptyAgreement, effectiveFrom: '2026-01-01', currency: 'sek',
      hourlyRate: '612,50', holidayRate: '900', overtimeAfterHours: '0', overtimePercent: '0',
      mileageRate: '2,5',
    });
    expect(body.hourlyRate).toBe(612.5);
    expect(body.holidayRate).toBe(900);
    expect(body.overtimeAfterHours).toBe(0);
    expect(body.overtimePercent).toBe(0);
    expect(body.mileageRate).toBe(2.5);
    expect(body.currency).toBe('SEK');
  });

  it('defaults only empty OT fields (8 h / 150 %)', () => {
    const body = agreementFormToBody({
      ...emptyAgreement, overtimeAfterHours: '', overtimePercent: '',
    });
    expect(body.overtimeAfterHours).toBe(8);
    expect(body.overtimePercent).toBe(150);
  });

  it('scopes a work package only under a contract', () => {
    expect(agreementFormToBody({ ...emptyAgreement, workPackageId: 'wp' }).workPackageId).toBeNull();
    const scoped = agreementFormToBody({ ...emptyAgreement, contractId: 'c', workPackageId: 'wp' });
    expect(scoped).toMatchObject({ contractId: 'c', workPackageId: 'wp' });
  });

  it('keeps the per-diem amount only for a custom policy', () => {
    expect(agreementFormToBody({ ...emptyAgreement, perDiem: 'statutory', perDiemAmount: '300' })
      .perDiemAmount).toBeNull();
    expect(agreementFormToBody({ ...emptyAgreement, perDiem: 'custom', perDiemAmount: '300' })
      .perDiemAmount).toBe(300);
  });

  it('blank texts become null', () => {
    const body = agreementFormToBody({ ...emptyAgreement, expensePolicy: '  ', notes: 'x' });
    expect(body.expensePolicy).toBeNull();
    expect(body.notes).toBe('x');
  });
});
