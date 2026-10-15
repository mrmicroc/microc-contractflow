import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createContractTerms, createProject, deleteContractTerms, errorMessage, getContract,
} from '@/lib/api';
import { numOrNull, resolveVersion } from '@/lib/versioned';
import type { ContractDetail as ContractDetailDto, ContractTerms, TimeBilling } from '@/lib/types';
import { cn } from '@/lib/utils';
import { isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, Panel, tdCls, thCls,
} from '@/components/forms';

const todayIso = () => new Date().toISOString().slice(0, 10);
const grpCls =
  'mb-1.5 mt-3 border-b border-slate-200 pb-0.5 text-[11px] font-semibold uppercase tracking-wide text-slate-500';
const checkCls = 'flex items-center gap-1.5 text-sm text-slate-700';

type TermsForm = {
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
  halfDayRate: string;
  halfDayMaxHours: string;
  fixedPriceAmount: string;
  fixedPriceDate: string;
  fixedPriceNote: string;
  padHoursPerDay: string;
  minPadPerBookedDay: string;
  carryOverMaxPercent: string;
  cancellationDeadlineBusinessDays: string;
  holidayCalendar: string;
  travelExpensePolicy: string;
  indexation: string;
  notes: string;
};

const emptyTerms: TermsForm = {
  effectiveFrom: '', currency: 'SEK',
  timeBilling: 'hourly', padEnabled: false, fixedPriceEnabled: false,
  padRate: '', hourlyRate: '', saturdayRate: '', sundayRate: '', holidayRate: '',
  overtimeAfterHours: '8', overtimePercent: '150',
  mobilisationFee: '', expenseMarkupPercent: '0', minBillableHoursPerDay: '',
  dayRate: '', halfDayRate: '', halfDayMaxHours: '',
  fixedPriceAmount: '', fixedPriceDate: '', fixedPriceNote: '',
  padHoursPerDay: '8', minPadPerBookedDay: '1', carryOverMaxPercent: '20',
  cancellationDeadlineBusinessDays: '4', holidayCalendar: 'SE',
  travelExpensePolicy: '', indexation: '', notes: '',
};

/** Prefill a new version from the version currently in force — corrections
 * start from today's values instead of a blank slate. */
const fromTerms = (v: ContractTerms): TermsForm => ({
  effectiveFrom: todayIso(), currency: v.currency,
  timeBilling: v.timeBilling, padEnabled: v.padEnabled,
  fixedPriceEnabled: v.fixedPriceEnabled,
  padRate: v.padRate?.toString() ?? '', hourlyRate: v.hourlyRate?.toString() ?? '',
  saturdayRate: v.saturdayRate?.toString() ?? '', sundayRate: v.sundayRate?.toString() ?? '',
  holidayRate: v.holidayRate?.toString() ?? '',
  overtimeAfterHours: v.overtimeAfterHours.toString(),
  overtimePercent: v.overtimePercent.toString(),
  mobilisationFee: v.mobilisationFee?.toString() ?? '',
  expenseMarkupPercent: v.expenseMarkupPercent.toString(),
  minBillableHoursPerDay: v.minBillableHoursPerDay?.toString() ?? '',
  dayRate: v.dayRate?.toString() ?? '', halfDayRate: v.halfDayRate?.toString() ?? '',
  halfDayMaxHours: v.halfDayMaxHours?.toString() ?? '',
  fixedPriceAmount: v.fixedPriceAmount?.toString() ?? '',
  fixedPriceDate: v.fixedPriceDate ?? '', fixedPriceNote: v.fixedPriceNote ?? '',
  padHoursPerDay: v.padHoursPerDay.toString(),
  minPadPerBookedDay: v.minPadPerBookedDay.toString(),
  carryOverMaxPercent: v.carryOverMaxPercent.toString(),
  cancellationDeadlineBusinessDays: v.cancellationDeadlineBusinessDays.toString(),
  holidayCalendar: v.holidayCalendar,
  travelExpensePolicy: v.travelExpensePolicy ?? '', indexation: v.indexation ?? '',
  notes: v.notes ?? '',
});

export function ContractDetail({
  contractId,
  selectedProjectId,
  onSelectProject,
}: {
  contractId: string;
  selectedProjectId: string | null;
  onSelectProject: (id: string) => void;
}) {
  const { t } = useTranslation();
  const me = useMe();
  const [contract, setContract] = useState<ContractDetailDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [termsFormOpen, setTermsFormOpen] = useState(false);
  const [projectFormOpen, setProjectFormOpen] = useState(false);
  const [tf, setTf] = useState<TermsForm>(emptyTerms);
  const [pf, setPf] = useState({ name: '', code: '', description: '', isActive: true });

  // Parent mounts this with key={contractId}, so a new selection remounts with
  // fresh state — the effect only needs to fetch.
  const reload = useCallback(
    () => getContract(contractId).then(setContract).catch(() => setContract(null)),
    [contractId],
  );
  useEffect(() => { void reload(); }, [reload]);

  if (!contract) return <p className="text-sm text-slate-400">{t('common.loading')}</p>;

  const inForce = resolveVersion(contract.termsVersions, todayIso());

  const openTermsForm = () => {
    setTf(inForce ? fromTerms(inForce) : emptyTerms);
    setTermsFormOpen(true);
  };

  const saveTerms = async () => {
    await createContractTerms(contract.id, {
      effectiveFrom: tf.effectiveFrom,
      currency: tf.currency,
      timeBilling: tf.timeBilling,
      padEnabled: tf.padEnabled,
      fixedPriceEnabled: tf.fixedPriceEnabled,
      padRate: numOrNull(tf.padRate),
      hourlyRate: numOrNull(tf.hourlyRate),
      saturdayRate: numOrNull(tf.saturdayRate),
      sundayRate: numOrNull(tf.sundayRate),
      holidayRate: numOrNull(tf.holidayRate),
      overtimeAfterHours: Number(tf.overtimeAfterHours) || 8,
      overtimePercent: Number(tf.overtimePercent) || 150,
      mobilisationFee: numOrNull(tf.mobilisationFee),
      expenseMarkupPercent: Number(tf.expenseMarkupPercent) || 0,
      minBillableHoursPerDay: numOrNull(tf.minBillableHoursPerDay),
      dayRate: numOrNull(tf.dayRate),
      halfDayRate: numOrNull(tf.halfDayRate),
      halfDayMaxHours: numOrNull(tf.halfDayMaxHours),
      fixedPriceAmount: numOrNull(tf.fixedPriceAmount),
      fixedPriceDate: tf.fixedPriceDate || null,
      fixedPriceNote: tf.fixedPriceNote || null,
      padHoursPerDay: Number(tf.padHoursPerDay) || 8,
      minPadPerBookedDay: Number(tf.minPadPerBookedDay) || 1,
      carryOverMaxPercent: Number(tf.carryOverMaxPercent) || 20,
      cancellationDeadlineBusinessDays: Number(tf.cancellationDeadlineBusinessDays) || 4,
      holidayCalendar: tf.holidayCalendar,
      travelExpensePolicy: tf.travelExpensePolicy || null,
      indexation: tf.indexation || null,
      notes: tf.notes || null,
    });
    setTermsFormOpen(false);
    await reload();
  };

  const saveProject = async () => {
    await createProject({
      contractId: contract.id,
      name: pf.name.trim(),
      code: pf.code.trim() || null,
      description: pf.description.trim() || null,
      isActive: pf.isActive,
    });
    setProjectFormOpen(false);
    setPf({ name: '', code: '', description: '', isActive: true });
    await reload();
  };

  const removeTerms = async (termId: string) => {
    if (!window.confirm(t('common.confirmDelete'))) return;
    setError(null);
    try { await deleteContractTerms(contract.id, termId); await reload(); }
    catch (e) { setError(errorMessage(e)); }
  };

  const fmt = (n: number | null) => (n === null ? '—' : String(n));

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
    if (v.fixedPriceEnabled)
      tags.push(`${t('terms.fixedPriceTag')}: ${fmt(v.fixedPriceAmount)} ${v.currency}`);
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
          {contract.endClientName && <span>· {t('contracts.endClient')}: {contract.endClientName}</span>}
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
                <input className={inputCls} value={tf.currency} maxLength={3}
                  onChange={(e) => setTf({ ...tf, currency: e.target.value })} />
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
              <Field label={t('terms.fixedPriceEnabled')} help={t('terms.hFixedPriceEnabled')}>
                <span className={checkCls}>
                  <input type="checkbox" checked={tf.fixedPriceEnabled}
                    onChange={(e) => setTf({ ...tf, fixedPriceEnabled: e.target.checked })} />
                  {tf.fixedPriceEnabled ? t('common.yes') : t('common.no')}
                </span>
              </Field>
            </div>

            {tf.timeBilling === 'hourly' && (
              <>
                <h4 className={grpCls}>{t('terms.groupHourly')}</h4>
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                  <Field label={t('terms.hourlyRate')} help={t('terms.hHourlyRate')}>
                    <input className={inputCls} inputMode="decimal" value={tf.hourlyRate}
                      onChange={(e) => setTf({ ...tf, hourlyRate: e.target.value })} />
                  </Field>
                  <Field label={t('terms.saturdayRate')} help={t('terms.hSaturdayRate')}>
                    <input className={inputCls} inputMode="decimal" value={tf.saturdayRate}
                      onChange={(e) => setTf({ ...tf, saturdayRate: e.target.value })} />
                  </Field>
                  <Field label={t('terms.sundayRate')} help={t('terms.hSundayRate')}>
                    <input className={inputCls} inputMode="decimal" value={tf.sundayRate}
                      onChange={(e) => setTf({ ...tf, sundayRate: e.target.value })} />
                  </Field>
                  <Field label={t('terms.holidayRate')} help={t('terms.hHolidayRate')}>
                    <input className={inputCls} inputMode="decimal" value={tf.holidayRate}
                      onChange={(e) => setTf({ ...tf, holidayRate: e.target.value })} />
                  </Field>
                  <Field label={t('terms.overtimeAfterHours')} help={t('terms.hOvertimeAfterHours')}>
                    <input className={inputCls} inputMode="numeric" value={tf.overtimeAfterHours}
                      onChange={(e) => setTf({ ...tf, overtimeAfterHours: e.target.value })} />
                  </Field>
                  <Field label={t('terms.overtimePercent')} help={t('terms.hOvertimePercent')}>
                    <input className={inputCls} inputMode="decimal" value={tf.overtimePercent}
                      onChange={(e) => setTf({ ...tf, overtimePercent: e.target.value })} />
                  </Field>
                  <Field label={t('terms.minBillableHoursPerDay')} help={t('terms.hMinBillable')}>
                    <input className={inputCls} inputMode="decimal" value={tf.minBillableHoursPerDay}
                      onChange={(e) => setTf({ ...tf, minBillableHoursPerDay: e.target.value })} />
                  </Field>
                </div>
              </>
            )}

            {tf.timeBilling === 'dailyRate' && (
              <>
                <h4 className={grpCls}>{t('terms.groupDaily')}</h4>
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                  <Field label={t('terms.dayRate')} help={t('terms.hDayRate')}>
                    <input className={inputCls} inputMode="decimal" value={tf.dayRate}
                      onChange={(e) => setTf({ ...tf, dayRate: e.target.value })} />
                  </Field>
                  <Field label={t('terms.halfDayRate')} help={t('terms.hHalfDayRate')}>
                    <input className={inputCls} inputMode="decimal" value={tf.halfDayRate}
                      onChange={(e) => setTf({ ...tf, halfDayRate: e.target.value })} />
                  </Field>
                  <Field label={t('terms.halfDayMaxHours')} help={t('terms.hHalfDayMaxHours')}>
                    <input className={inputCls} inputMode="decimal" value={tf.halfDayMaxHours}
                      onChange={(e) => setTf({ ...tf, halfDayMaxHours: e.target.value })} />
                  </Field>
                </div>
              </>
            )}

            {tf.padEnabled && (
              <>
                <h4 className={grpCls}>{t('terms.groupPad')}</h4>
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                  <Field label={t('terms.padRate')} help={t('terms.hPadRate')}>
                    <input className={inputCls} inputMode="decimal" value={tf.padRate}
                      onChange={(e) => setTf({ ...tf, padRate: e.target.value })} />
                  </Field>
                  <Field label={t('terms.padHoursPerDay')} help={t('terms.hPadHoursPerDay')}>
                    <input className={inputCls} inputMode="decimal" value={tf.padHoursPerDay}
                      onChange={(e) => setTf({ ...tf, padHoursPerDay: e.target.value })} />
                  </Field>
                  <Field label={t('terms.minPadPerBookedDay')} help={t('terms.hMinPadPerBookedDay')}>
                    <input className={inputCls} inputMode="decimal" value={tf.minPadPerBookedDay}
                      onChange={(e) => setTf({ ...tf, minPadPerBookedDay: e.target.value })} />
                  </Field>
                  <Field label={t('terms.carryOverMaxPercent')} help={t('terms.hCarryOver')}>
                    <input className={inputCls} inputMode="decimal" value={tf.carryOverMaxPercent}
                      onChange={(e) => setTf({ ...tf, carryOverMaxPercent: e.target.value })} />
                  </Field>
                </div>
              </>
            )}

            {tf.fixedPriceEnabled && (
              <>
                <h4 className={grpCls}>{t('terms.groupFixedPrice')}</h4>
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                  <Field label={t('terms.fixedPriceAmount')} help={t('terms.hFixedPriceAmount')}>
                    <input className={inputCls} inputMode="decimal" value={tf.fixedPriceAmount}
                      onChange={(e) => setTf({ ...tf, fixedPriceAmount: e.target.value })} />
                  </Field>
                  <Field label={t('terms.fixedPriceDate')} help={t('terms.hFixedPriceDate')}>
                    <input type="date" className={inputCls} value={tf.fixedPriceDate}
                      onChange={(e) => setTf({ ...tf, fixedPriceDate: e.target.value })} />
                  </Field>
                  <Field label={t('terms.fixedPriceNote')} help={t('terms.hFixedPriceNote')}>
                    <input className={inputCls} value={tf.fixedPriceNote}
                      onChange={(e) => setTf({ ...tf, fixedPriceNote: e.target.value })} />
                  </Field>
                </div>
              </>
            )}

            <h4 className={grpCls}>{t('terms.groupCharges')}</h4>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              <Field label={t('terms.mobilisationFee')} help={t('terms.hMobilisationFee')}>
                <input className={inputCls} inputMode="decimal" value={tf.mobilisationFee}
                  onChange={(e) => setTf({ ...tf, mobilisationFee: e.target.value })} />
              </Field>
              <Field label={t('terms.expenseMarkupPercent')} help={t('terms.hExpenseMarkup')}>
                <input className={inputCls} inputMode="decimal" value={tf.expenseMarkupPercent}
                  onChange={(e) => setTf({ ...tf, expenseMarkupPercent: e.target.value })} />
              </Field>
            </div>

            <h4 className={grpCls}>{t('terms.groupBooking')}</h4>
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              <Field label={t('terms.cancellationDeadlineBusinessDays')} help={t('terms.hCancelDeadline')}>
                <input className={inputCls} inputMode="numeric" value={tf.cancellationDeadlineBusinessDays}
                  onChange={(e) => setTf({ ...tf, cancellationDeadlineBusinessDays: e.target.value })} />
              </Field>
              <Field label={t('terms.holidayCalendar')} help={t('terms.hHolidayCalendar')}>
                <input className={inputCls} value={tf.holidayCalendar}
                  onChange={(e) => setTf({ ...tf, holidayCalendar: e.target.value })} />
              </Field>
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
                <td className={tdCls}>{v.cancellationDeadlineBusinessDays}</td>
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
