import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createContractTerms, createProject, deleteContractTerms, errorMessage, getCalendars,
  getContract, getCurrencies,
} from '@/lib/api';
import { calendarLabel, currencyName } from '@/lib/countries';
import { localTodayIso as todayIso } from '@/lib/dates';
import { confirmAction } from '@/lib/confirm';
import {
  emptyTerms, fromTerms, padRuleTags, termsFormToBody, type StepRow, type TermsForm,
} from '@/lib/terms';
import { resolveVersion } from '@/lib/versioned';
import { invalidNumericFields, isInvalidDecimal, isInvalidInteger } from '@/lib/validation';
import type {
  CarryOverBasis, ContractDetail as ContractDetailDto, ContractTerms, MetaEntry, PadInvoicing,
  SharedDayMinimum, TimeBilling,
} from '@/lib/types';
import { ScheduledChargesPanel } from './ScheduledChargesPanel';
import { RepresentativesPanel } from './RepresentativesPanel';
import { VisitsPanel } from './VisitsPanel';
import { endClientLabel } from '@/lib/organisations';
import { cn } from '@/lib/utils';
import { isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, NumInput, Panel, SearchSelect, tdCls, thCls,
} from '@/components/forms';

const grpCls =
  'mb-1.5 mt-3 border-b border-slate-200 pb-0.5 text-[11px] font-semibold uppercase tracking-wide text-slate-500';
const checkCls = 'flex items-center gap-1.5 text-sm text-slate-700';

export function ContractDetail({
  contractId,
  selectedProjectId,
  onSelectProject,
  onChanged,
}: {
  contractId: string;
  selectedProjectId: string | null;
  onSelectProject: (id: string) => void;
  onChanged?: () => void;
}) {
  const { t, i18n } = useTranslation();
  const me = useMe();
  const [contract, setContract] = useState<ContractDetailDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [termsFormOpen, setTermsFormOpen] = useState(false);
  const [projectFormOpen, setProjectFormOpen] = useState(false);
  const [tf, setTf] = useState<TermsForm>(emptyTerms);
  const [pf, setPf] = useState({ name: '', code: '', description: '', isActive: true });
  const [calendars, setCalendars] = useState<MetaEntry[]>([]);
  const [currencies, setCurrencies] = useState<MetaEntry[]>([]);

  // Parent mounts this with key={contractId}, so a new selection remounts with
  // fresh state — the effect only needs to fetch.
  const reload = useCallback(
    () => getContract(contractId).then(setContract).catch(() => setContract(null)),
    [contractId],
  );
  useEffect(() => { void reload(); }, [reload]);
  useEffect(() => {
    getCalendars().then(setCalendars).catch(() => setCalendars([]));
    getCurrencies().then(setCurrencies).catch(() => setCurrencies([]));
  }, []);

  if (!contract) return <p className="text-sm text-slate-400">{t('common.loading')}</p>;

  const inForce = resolveVersion(contract.termsVersions, todayIso());

  const openTermsForm = () => {
    setTf(inForce ? fromTerms(inForce, todayIso()) : emptyTerms);
    setTermsFormOpen(true);
  };

  const saveTerms = async () => {
    setError(null);
    // Block the save on any non-empty non-numeric value — letters must never
    // silently degrade to "empty"/default (the user typed something).
    const bad = invalidNumericFields(
      {
        [t('terms.padRate')]: tf.padRate,
        [t('terms.hourlyRate')]: tf.hourlyRate,
        [t('terms.saturdayRate')]: tf.saturdayRate,
        [t('terms.sundayRate')]: tf.sundayRate,
        [t('terms.holidayRate')]: tf.holidayRate,
        [t('terms.overtimePercent')]: tf.overtimePercent,
        [t('terms.mobilisationFee')]: tf.mobilisationFee,
        [t('terms.expenseMarkupPercent')]: tf.expenseMarkupPercent,
        [t('terms.minBillableHoursPerDay')]: tf.minBillableHoursPerDay,
        [t('terms.dayRate')]: tf.dayRate,
        [t('terms.saturdayDayRate')]: tf.saturdayDayRate,
        [t('terms.sundayDayRate')]: tf.sundayDayRate,
        [t('terms.holidayDayRate')]: tf.holidayDayRate,
        [t('terms.halfDayRate')]: tf.halfDayRate,
        [t('terms.halfDayMaxHours')]: tf.halfDayMaxHours,
        [t('terms.additionalPadRate')]: tf.additionalPadRate,
        [t('terms.carryOverRoundUpTo')]: tf.carryOverRoundUpTo,
        [t('terms.padHoursPerDay')]: tf.padHoursPerDay,
        [t('terms.minPadPerBookedDay')]: tf.minPadPerBookedDay,
        [t('terms.carryOverMaxPercent')]: tf.carryOverMaxPercent,
      },
      {
        [t('terms.overtimeAfterHours')]: tf.overtimeAfterHours,
        [t('terms.carryOverMonths')]: tf.carryOverMonths,
        [t('terms.cancellationDeadlineBusinessDays')]: tf.cancellationDeadlineBusinessDays,
        [t('terms.unbookedTimeIncrementMinutes')]: tf.unbookedTimeIncrementMinutes,
        [t('terms.startTimeNoticeBusinessDays')]: tf.startTimeNoticeBusinessDays,
        [t('terms.includedVisits')]: tf.includedVisits,
      },
    );
    tf.cancellationSteps.forEach((s, i) => {
      const tag = `${t('terms.cancellationSteps')} #${i + 1}`;
      if (s.minBusinessDaysBefore.trim() && isInvalidInteger(s.minBusinessDaysBefore))
        bad.push(`${tag} · ${t('terms.stepDaysBefore')}`);
      if (isInvalidDecimal(s.chargeHours)) bad.push(`${tag} · ${t('terms.stepChargeHours')}`);
      if (isInvalidDecimal(s.chargeAmount)) bad.push(`${tag} · ${t('terms.stepChargeAmount')}`);
      if (isInvalidDecimal(s.chargeDayFraction)) bad.push(`${tag} · ${t('terms.stepChargeDayFraction')}`);
    });
    if (!tf.effectiveFrom) bad.push(t('common.effectiveFrom'));
    if (bad.length) {
      setError(t('common.invalidNumbers', { fields: bad.join(', ') }));
      return;
    }
    try {
      await createContractTerms(contract.id, termsFormToBody(tf));
    } catch (e) {
      setError(errorMessage(e));
      return;
    }
    setTermsFormOpen(false);
    await reload();
    onChanged?.(); // refresh the contract list — badges show the new version
  };

  const saveProject = async () => {
    setError(null);
    try {
      await createProject({
        contractId: contract.id,
        name: pf.name.trim(),
        code: pf.code.trim() || null,
        description: pf.description.trim() || null,
        isActive: pf.isActive,
      });
    } catch (e) {
      setError(errorMessage(e));
      return;
    }
    setProjectFormOpen(false);
    setPf({ name: '', code: '', description: '', isActive: true });
    await reload();
    onChanged?.();
  };

  const removeTerms = async (termId: string) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    setError(null);
    try { await deleteContractTerms(contract.id, termId); await reload(); onChanged?.(); }
    catch (e) { setError(errorMessage(e)); }
  };

  const fmt = (n: number | null) => (n === null ? '—' : String(n));

  // Overtime applies to both hourly and daily-rate billing — shared pair.
  const otFields = (
    <>
      <Field label={t('terms.overtimeAfterHours')} help={t('terms.hOvertimeAfterHours')}>
        <NumInput integerOnly className={inputCls} value={tf.overtimeAfterHours}
          onChange={(v) => setTf({ ...tf, overtimeAfterHours: v })} />
      </Field>
      <Field label={t('terms.overtimePercent')} help={t('terms.hOvertimePercent')}>
        <NumInput className={inputCls} value={tf.overtimePercent}
          onChange={(v) => setTf({ ...tf, overtimePercent: v })} />
      </Field>
    </>
  );

  const setStep = (i: number, patch: Partial<StepRow>) =>
    setTf({
      ...tf,
      cancellationSteps: tf.cancellationSteps.map((s, j) => (j === i ? { ...s, ...patch } : s)),
    });

  /** Per-version billing-component summary for the versions table, e.g.
   * "Timpris 1000 · PAD 800". */
  const componentTags = (v: ContractTerms): string[] => {
    const tags: string[] = [];
    if (v.timeBilling === 'hourly')
      tags.push(`${t('terms.hourlyRate')}: ${fmt(v.hourlyRate)} ${v.currency}`);
    if (v.timeBilling === 'dailyRate')
      tags.push(`${t('terms.dayRate')}: ${fmt(v.dayRate)} ${v.currency}`);
    if (v.padEnabled)
      tags.push(`${t('terms.padTag')}: ${fmt(v.padRate)} ${v.currency}`);
    for (const rule of padRuleTags(v)) tags.push(t(rule.key, rule.params));
    if (tags.length === 0) tags.push(t('terms.noComponents'));
    return tags;
  };

  return (
    <div className="space-y-4">
      <Panel
        title={`${contract.name} · ${contract.customerName}`}
        help={t('contracts.detailHelp')}
        actions={isAdmin(me) && !termsFormOpen && (
          <button className={btnSecondaryCls} onClick={openTermsForm}>
            {t('terms.newVersion')}
          </button>
        )}
      >
        <p className="mb-3 flex flex-wrap items-center gap-1 text-xs text-slate-500">
          {contract.components.map((k) => (
            <span key={k}
              className="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-700">
              {t(`contracts.comp.${k}`)}
            </span>
          ))}
          <span>{contract.startDate} → {contract.endDate ?? '—'}</span>
          <span>· {t('contracts.party')}: {contract.customerName}</span>
          <span>· {t('contracts.endClient')}: {endClientLabel(contract.endClientName, contract.endClientReference) ?? t('contracts.directContract')}</span>
          <span>· {t('terms.immutable')}</span>
        </p>
        {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}

        {termsFormOpen && (
          <div className="mb-4 rounded border border-slate-200 bg-slate-50 p-3">
            <h4 className={grpCls}>{t('terms.groupGeneral')}</h4>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              <Field label={t('common.effectiveFrom')} help={t('terms.hEffectiveFrom')}>
                <input type="date" className={inputCls} value={tf.effectiveFrom}
                  onChange={(e) => setTf({ ...tf, effectiveFrom: e.target.value })} />
              </Field>
              <Field label={t('common.currency')} help={t('terms.hCurrency')}>
                <SearchSelect
                  value={tf.currency}
                  options={currencies.map((c) => ({
                    value: c.code,
                    label: `${currencyName(c.code, i18n.language)} (${c.code})`,
                  }))}
                  onChange={(v) => setTf({ ...tf, currency: v })}
                />
              </Field>
            </div>

            <h4 className={grpCls}>{t('terms.groupComponents')}</h4>
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
              <Field label={t('terms.timeBilling')} help={t('terms.hTimeBilling')}>
                <select className={inputCls} value={tf.timeBilling}
                  onChange={(e) => setTf({ ...tf, timeBilling: e.target.value as TimeBilling })}>
                  <option value="none">{t('terms.tbNone')}</option>
                  <option value="hourly">{t('terms.tbHourly')}</option>
                  <option value="dailyRate">{t('terms.tbDaily')}</option>
                </select>
              </Field>
              <Field label={t('terms.padEnabled')} help={t('terms.hPadEnabled')}>
                <span className={checkCls}>
                  <input type="checkbox" checked={tf.padEnabled}
                    onChange={(e) => setTf({ ...tf, padEnabled: e.target.checked })} />
                  {tf.padEnabled ? t('common.yes') : t('common.no')}
                </span>
              </Field>
            </div>

            {tf.timeBilling === 'hourly' && (
              <>
                <h4 className={grpCls}>{t('terms.groupHourly')}</h4>
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                  <Field label={t('terms.hourlyRate')} help={t('terms.hHourlyRate')}>
                    <NumInput className={inputCls} value={tf.hourlyRate}
                      onChange={(v) => setTf({ ...tf, hourlyRate: v })} />
                  </Field>
                  <Field label={t('terms.saturdayRate')} help={t('terms.hSaturdayRate')}>
                    <NumInput className={inputCls} value={tf.saturdayRate}
                      onChange={(v) => setTf({ ...tf, saturdayRate: v })} />
                  </Field>
                  <Field label={t('terms.sundayRate')} help={t('terms.hSundayRate')}>
                    <NumInput className={inputCls} value={tf.sundayRate}
                      onChange={(v) => setTf({ ...tf, sundayRate: v })} />
                  </Field>
                  <Field label={t('terms.holidayRate')} help={t('terms.hHolidayRate')}>
                    <NumInput className={inputCls} value={tf.holidayRate}
                      onChange={(v) => setTf({ ...tf, holidayRate: v })} />
                  </Field>
                  {otFields}
                  <Field label={t('terms.minBillableHoursPerDay')} help={t('terms.hMinBillable')}>
                    <NumInput className={inputCls} value={tf.minBillableHoursPerDay}
                      onChange={(v) => setTf({ ...tf, minBillableHoursPerDay: v })} />
                  </Field>
                </div>
              </>
            )}

            {tf.timeBilling === 'dailyRate' && (
              <>
                <h4 className={grpCls}>{t('terms.groupDaily')}</h4>
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                  <Field label={t('terms.dayRate')} help={t('terms.hDayRate')}>
                    <NumInput className={inputCls} value={tf.dayRate}
                      onChange={(v) => setTf({ ...tf, dayRate: v })} />
                  </Field>
                  <Field label={t('terms.saturdayDayRate')} help={t('terms.hSaturdayDayRate')}>
                    <NumInput className={inputCls} value={tf.saturdayDayRate}
                      onChange={(v) => setTf({ ...tf, saturdayDayRate: v })} />
                  </Field>
                  <Field label={t('terms.sundayDayRate')} help={t('terms.hSundayDayRate')}>
                    <NumInput className={inputCls} value={tf.sundayDayRate}
                      onChange={(v) => setTf({ ...tf, sundayDayRate: v })} />
                  </Field>
                  <Field label={t('terms.holidayDayRate')} help={t('terms.hHolidayDayRate')}>
                    <NumInput className={inputCls} value={tf.holidayDayRate}
                      onChange={(v) => setTf({ ...tf, holidayDayRate: v })} />
                  </Field>
                  <Field label={t('terms.halfDayRate')} help={t('terms.hHalfDayRate')}>
                    <NumInput className={inputCls} value={tf.halfDayRate}
                      onChange={(v) => setTf({ ...tf, halfDayRate: v })} />
                  </Field>
                  <Field label={t('terms.halfDayMaxHours')} help={t('terms.hHalfDayMaxHours')}>
                    <NumInput className={inputCls} value={tf.halfDayMaxHours}
                      onChange={(v) => setTf({ ...tf, halfDayMaxHours: v })} />
                  </Field>
                  {otFields}
                </div>
              </>
            )}

            {tf.padEnabled && (
              <>
                <h4 className={grpCls}>{t('terms.groupPad')}</h4>
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                  <Field label={t('terms.padRate')} help={t('terms.hPadRate')}>
                    <NumInput className={inputCls} value={tf.padRate}
                      onChange={(v) => setTf({ ...tf, padRate: v })} />
                  </Field>
                  <Field label={t('terms.padHoursPerDay')} help={t('terms.hPadHoursPerDay')}>
                    <NumInput className={inputCls} value={tf.padHoursPerDay}
                      onChange={(v) => setTf({ ...tf, padHoursPerDay: v })} />
                  </Field>
                  <Field label={t('terms.minPadPerBookedDay')} help={t('terms.hMinPadPerBookedDay')}>
                    <NumInput className={inputCls} value={tf.minPadPerBookedDay}
                      onChange={(v) => setTf({ ...tf, minPadPerBookedDay: v })} />
                  </Field>
                  <Field label={t('terms.carryOverMaxPercent')} help={t('terms.hCarryOver')}>
                    <NumInput className={inputCls} value={tf.carryOverMaxPercent}
                      onChange={(v) => setTf({ ...tf, carryOverMaxPercent: v })} />
                  </Field>
                  <Field label={t('terms.carryOverMonths')} help={t('terms.hCarryOverMonths')}>
                    <NumInput integerOnly className={inputCls} value={tf.carryOverMonths}
                      onChange={(v) => setTf({ ...tf, carryOverMonths: v })} />
                  </Field>
                  <Field label={t('terms.carryOverBasis')} help={t('terms.hCarryOverBasis')}>
                    <select className={inputCls} value={tf.carryOverBasis}
                      onChange={(e) => setTf({ ...tf, carryOverBasis: e.target.value as CarryOverBasis })}>
                      <option value="unusedBaseline">{t('terms.cobUnused')}</option>
                      <option value="baseline">{t('terms.cobBaseline')}</option>
                    </select>
                  </Field>
                  <Field label={t('terms.carryOverRoundUpTo')} help={t('terms.hCarryOverRoundUpTo')}>
                    <NumInput className={inputCls} value={tf.carryOverRoundUpTo}
                      onChange={(v) => setTf({ ...tf, carryOverRoundUpTo: v })} />
                  </Field>
                  <Field label={t('terms.padInvoicing')} help={t('terms.hPadInvoicing')}>
                    <select className={inputCls} value={tf.padInvoicing}
                      onChange={(e) => setTf({ ...tf, padInvoicing: e.target.value as PadInvoicing })}>
                      <option value="utilised">{t('terms.piUtilised')}</option>
                      <option value="allocationConsumed">{t('terms.piAllocation')}</option>
                    </select>
                  </Field>
                  <Field label={t('terms.additionalPadRate')} help={t('terms.hAdditionalPadRate')}>
                    <NumInput className={inputCls} value={tf.additionalPadRate}
                      onChange={(v) => setTf({ ...tf, additionalPadRate: v })} />
                  </Field>
                  <Field label={t('terms.sharedDayMinimum')} help={t('terms.hSharedDayMinimum')}>
                    <select className={inputCls} value={tf.sharedDayMinimum}
                      onChange={(e) => setTf({ ...tf, sharedDayMinimum: e.target.value as SharedDayMinimum })}>
                      <option value="proportional">{t('terms.sdmProportional')}</option>
                      <option value="perProject">{t('terms.sdmPerProject')}</option>
                    </select>
                  </Field>
                  <Field label={t('terms.unbookedTimeConsumesPad')} help={t('terms.hUnbookedTimeConsumesPad')}>
                    <span className={checkCls}>
                      <input type="checkbox" checked={tf.unbookedTimeConsumesPad}
                        onChange={(e) => setTf({ ...tf, unbookedTimeConsumesPad: e.target.checked })} />
                      {tf.unbookedTimeConsumesPad ? t('common.yes') : t('common.no')}
                    </span>
                  </Field>
                  {tf.unbookedTimeConsumesPad && (
                    <Field label={t('terms.unbookedTimeIncrementMinutes')} help={t('terms.hUnbookedIncrement')}>
                      <NumInput integerOnly className={inputCls} value={tf.unbookedTimeIncrementMinutes}
                        onChange={(v) => setTf({ ...tf, unbookedTimeIncrementMinutes: v })} />
                    </Field>
                  )}
                </div>
              </>
            )}

            <h4 className={grpCls}>{t('terms.groupCharges')}</h4>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              <Field label={t('terms.mobilisationFee')} help={t('terms.hMobilisationFee')}>
                <NumInput className={inputCls} value={tf.mobilisationFee}
                  onChange={(v) => setTf({ ...tf, mobilisationFee: v })} />
              </Field>
              <Field label={t('terms.expenseMarkupPercent')} help={t('terms.hExpenseMarkup')}>
                <NumInput className={inputCls} value={tf.expenseMarkupPercent}
                  onChange={(v) => setTf({ ...tf, expenseMarkupPercent: v })} />
              </Field>
              <Field label={t('terms.includedVisits')} help={t('terms.hIncludedVisits')}>
                <NumInput integerOnly className={inputCls} value={tf.includedVisits}
                  onChange={(v) => setTf({ ...tf, includedVisits: v })} />
              </Field>
            </div>

            <h4 className={grpCls}>{t('terms.groupBooking')}</h4>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              <Field label={t('terms.cancellationDeadlineBusinessDays')} help={t('terms.hCancelDeadline')}>
                <NumInput integerOnly className={inputCls} value={tf.cancellationDeadlineBusinessDays}
                  onChange={(v) => setTf({ ...tf, cancellationDeadlineBusinessDays: v })} />
              </Field>
              <Field label={t('terms.holidayCalendar')} help={t('terms.hHolidayCalendar')}>
                <SearchSelect
                  value={tf.holidayCalendar}
                  options={calendars.map((c) => ({
                    value: c.code,
                    label: calendarLabel(c.code, c.name, i18n.language, t('terms.calendarNone')),
                  }))}
                  onChange={(v) => setTf({ ...tf, holidayCalendar: v || 'SE' })}
                />
              </Field>
              <Field label={t('terms.defaultStartTime')} help={t('terms.hDefaultStartTime')}>
                <input type="time" className={inputCls} value={tf.defaultStartTime}
                  onChange={(e) => setTf({ ...tf, defaultStartTime: e.target.value })} />
              </Field>
              <Field label={t('terms.startTimeNoticeBusinessDays')} help={t('terms.hStartTimeNotice')}>
                <NumInput integerOnly className={inputCls} value={tf.startTimeNoticeBusinessDays}
                  onChange={(v) => setTf({ ...tf, startTimeNoticeBusinessDays: v })} />
              </Field>
            </div>
            <div className="mt-2">
              <p className="mb-1 flex items-center gap-1.5 text-xs font-medium text-slate-600">
                {t('terms.cancellationSteps')}
                <span className="cursor-help rounded-full border border-slate-300 px-1 text-[10px] text-slate-500"
                  title={t('terms.hCancelSteps')}>?</span>
              </p>
              {tf.cancellationSteps.map((s, i) => (
                <div key={i} className="mb-1 flex items-center gap-2">
                  <NumInput integerOnly className={cn(inputCls, 'w-32')}
                    placeholder={t('terms.stepDaysBefore')}
                    value={s.minBusinessDaysBefore}
                    onChange={(v) => setStep(i, { minBusinessDaysBefore: v })} />
                  <NumInput className={cn(inputCls, 'w-32')}
                    placeholder={t('terms.stepChargeHours')} value={s.chargeHours}
                    onChange={(v) => setStep(i, { chargeHours: v })} />
                  <NumInput className={cn(inputCls, 'w-32')}
                    placeholder={t('terms.stepChargeAmount')} value={s.chargeAmount}
                    onChange={(v) => setStep(i, { chargeAmount: v })} />
                  {tf.timeBilling === 'dailyRate' && (
                    <NumInput className={cn(inputCls, 'w-44')}
                      placeholder={t('terms.stepChargeDayFraction')} value={s.chargeDayFraction}
                      onChange={(v) => setStep(i, { chargeDayFraction: v })} />
                  )}
                  <button className={btnDangerCls}
                    onClick={() => setTf({ ...tf, cancellationSteps: tf.cancellationSteps.filter((_, j) => j !== i) })}>
                    {t('common.delete')}
                  </button>
                </div>
              ))}
              <button className={btnSecondaryCls}
                onClick={() => setTf({
                  ...tf,
                  cancellationSteps: [...tf.cancellationSteps,
                    { minBusinessDaysBefore: '', chargeHours: '', chargeAmount: '', chargeDayFraction: '' }],
                })}>
                {t('terms.addStep')}
              </button>
            </div>

            <h4 className={grpCls}>{t('terms.groupOther')}</h4>
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
              <Field label={t('terms.travelExpensePolicy')} help={t('terms.hTravelPolicy')}>
                <input className={inputCls} value={tf.travelExpensePolicy}
                  onChange={(e) => setTf({ ...tf, travelExpensePolicy: e.target.value })} />
              </Field>
              <Field label={t('terms.indexation')} help={t('terms.hIndexation')}>
                <input className={inputCls} value={tf.indexation}
                  onChange={(e) => setTf({ ...tf, indexation: e.target.value })} />
              </Field>
              <Field label={t('common.notes')} help={t('terms.hNotes')}>
                <input className={inputCls} value={tf.notes}
                  onChange={(e) => setTf({ ...tf, notes: e.target.value })} />
              </Field>
            </div>
            <div className="mt-3 flex gap-2">
              <button className={btnCls}
                disabled={!tf.effectiveFrom || !tf.currency.trim() || !tf.holidayCalendar.trim()}
                onClick={() => void saveTerms()}>
                {t('common.save')}
              </button>
              <button className={btnSecondaryCls} onClick={() => setTermsFormOpen(false)}>
                {t('common.cancel')}
              </button>
            </div>
          </div>
        )}

        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
              <th className={thCls}>{t('common.version')}</th>
              <th className={thCls}>{t('common.effectiveFrom')}</th>
              <th className={thCls}>{t('common.currency')}</th>
              <th className={thCls}>{t('contracts.components')}</th>
              <th className={thCls}>{t('terms.mobilisationFee')}</th>
              <th className={thCls}>{t('terms.cancellationDeadlineBusinessDays')}</th>
              {isAdmin(me) && <th className={thCls}>{t('common.actions')}</th>}
            </tr>
          </thead>
          <tbody>
            {contract.termsVersions.map((v) => (
              <tr key={v.id} className="border-b border-slate-100">
                <td className={cn(tdCls, 'font-medium')}>
                  v{v.version}
                  {inForce?.id === v.id && (
                    <span className="ml-2 rounded bg-green-100 px-1.5 py-0.5 text-[10px] font-medium text-green-800">
                      {t('terms.inForceToday')}
                    </span>
                  )}
                </td>
                <td className={tdCls}>{v.effectiveFrom}</td>
                <td className={tdCls}>{v.currency}</td>
                <td className={tdCls}>
                  <span className="flex flex-wrap gap-1">
                    {componentTags(v).map((tag) => (
                      <span key={tag}
                        className="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-700">
                        {tag}
                      </span>
                    ))}
                  </span>
                </td>
                <td className={tdCls}>{fmt(v.mobilisationFee)}</td>
                <td className={tdCls}>
                  {v.cancellationSteps.length > 0
                    ? t('terms.stepsConfigured', { count: v.cancellationSteps.length })
                    : v.cancellationDeadlineBusinessDays}
                </td>
                {isAdmin(me) && (
                  <td className={tdCls}>
                    <button className={btnDangerCls} onClick={() => void removeTerms(v.id)}>
                      {t('common.delete')}
                    </button>
                  </td>
                )}
              </tr>
            ))}
            {contract.termsVersions.length === 0 && (
              <tr><td className={tdCls} colSpan={7}>{t('common.none')}</td></tr>
            )}
          </tbody>
        </table>
      </Panel>

      <ScheduledChargesPanel contractId={contract.id} projects={contract.projects}
        currency={inForce?.currency ?? 'SEK'} canEdit={isAdmin(me)} />
      <RepresentativesPanel contract={contract} canEdit={isAdmin(me)}
        onSaved={() => { void reload(); }} />
      <VisitsPanel contractId={contract.id} />

      <Panel
        title={t('contracts.projects')}
        help={t('contracts.projectsHelp')}
        actions={isAdmin(me) && !projectFormOpen && (
          <button className={btnSecondaryCls} onClick={() => setProjectFormOpen(true)}>
            {t('projects.newProject')}
          </button>
        )}
      >
        {projectFormOpen && (
          <div className="mb-4 rounded border border-slate-200 bg-slate-50 p-3">
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
              <Field label={t('common.name')} help={t('projects.hName')}>
                <input className={inputCls} value={pf.name}
                  onChange={(e) => setPf({ ...pf, name: e.target.value })} />
              </Field>
              <Field label={t('projects.code')} help={t('projects.hCode')}>
                <input className={inputCls} value={pf.code}
                  onChange={(e) => setPf({ ...pf, code: e.target.value })} />
              </Field>
              <Field label={t('projects.description')} help={t('projects.hDescription')}>
                <input className={inputCls} value={pf.description}
                  onChange={(e) => setPf({ ...pf, description: e.target.value })} />
              </Field>
            </div>
            <div className="mt-3 flex gap-2">
              <button className={btnCls} disabled={!pf.name.trim()}
                onClick={() => void saveProject()}>
                {t('common.save')}
              </button>
              <button className={btnSecondaryCls} onClick={() => setProjectFormOpen(false)}>
                {t('common.cancel')}
              </button>
            </div>
          </div>
        )}

        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
              <th className={cn(thCls, 'w-6')}></th>
              <th className={thCls}>{t('common.name')}</th>
              <th className={thCls}>{t('projects.code')}</th>
              <th className={thCls}>{t('common.active')}</th>
            </tr>
          </thead>
          <tbody>
            {contract.projects.map((p) => (
              <tr
                key={p.id}
                onClick={() => onSelectProject(p.id)}
                className={cn(
                  'cursor-pointer border-b border-slate-100 hover:bg-blue-50/50',
                  selectedProjectId === p.id && 'bg-blue-50',
                )}
              >
                <td className={cn(tdCls, 'w-6 text-slate-400')}>
                  {selectedProjectId === p.id ? '▾' : '▸'}
                </td>
                <td className={cn(tdCls, 'font-medium')}>{p.name}</td>
                <td className={tdCls}>{p.code ?? '—'}</td>
                <td className={tdCls}>{p.isActive ? t('common.active') : t('common.inactive')}</td>
              </tr>
            ))}
            {contract.projects.length === 0 && (
              <tr><td className={tdCls} colSpan={4}>{t('common.none')}</td></tr>
            )}
          </tbody>
        </table>
      </Panel>
    </div>
  );
}
