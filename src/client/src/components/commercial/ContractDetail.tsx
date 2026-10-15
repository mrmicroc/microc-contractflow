import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createContractTerms, createProject, deleteContractTerms, errorMessage, getContract,
} from '@/lib/api';
import { numOrNull, resolveVersion } from '@/lib/versioned';
import type { ContractDetail as ContractDetailDto } from '@/lib/types';
import { cn } from '@/lib/utils';
import { isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, Panel, tdCls, thCls,
} from '@/components/forms';

const todayIso = () => new Date().toISOString().slice(0, 10);

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
  const [tf, setTf] = useState({
    effectiveFrom: '', currency: 'SEK', padRate: '', hourlyRate: '', saturdayRate: '',
    sundayRate: '', holidayRate: '', overtimeAfterHours: '8', overtimePercent: '150',
    mobilisationFee: '', expenseMarkupPercent: '0', padHoursPerDay: '8',
    minPadPerBookedDay: '1', carryOverMaxPercent: '20',
    cancellationDeadlineBusinessDays: '4', holidayCalendar: 'SE',
    travelExpensePolicy: '', indexation: '', notes: '',
  });
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

  const saveTerms = async () => {
    await createContractTerms(contract.id, {
      effectiveFrom: tf.effectiveFrom,
      currency: tf.currency,
      padRate: numOrNull(tf.padRate),
      hourlyRate: numOrNull(tf.hourlyRate),
      saturdayRate: numOrNull(tf.saturdayRate),
      sundayRate: numOrNull(tf.sundayRate),
      holidayRate: numOrNull(tf.holidayRate),
      overtimeAfterHours: Number(tf.overtimeAfterHours) || 8,
      overtimePercent: Number(tf.overtimePercent) || 150,
      mobilisationFee: numOrNull(tf.mobilisationFee),
      expenseMarkupPercent: Number(tf.expenseMarkupPercent) || 0,
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

  return (
    <div className="space-y-4">
      <Panel
        title={`${contract.name} · ${contract.customerName}`}
        help={t('contracts.detailHelp')}
        actions={isAdmin(me) && !termsFormOpen && (
          <button className={btnSecondaryCls} onClick={() => setTermsFormOpen(true)}>
            {t('terms.newVersion')}
          </button>
        )}
      >
        <p className="mb-3 text-xs text-slate-500">
          {contract.model === 'padBased' ? t('contracts.modelPadBased') : t('contracts.modelHourly')}
          {' · '}{contract.startDate} → {contract.endDate ?? '—'}
          {contract.endClientName && <> · {t('contracts.endClient')}: {contract.endClientName}</>}
          {' · '}{t('terms.immutable')}
        </p>
        {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}

        {termsFormOpen && (
          <div className="mb-4 rounded border border-slate-200 bg-slate-50 p-3">
            <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
              <Field label={t('common.effectiveFrom')}>
                <input type="date" className={inputCls} value={tf.effectiveFrom}
                  onChange={(e) => setTf({ ...tf, effectiveFrom: e.target.value })} />
              </Field>
              <Field label={t('common.currency')}>
                <input className={inputCls} value={tf.currency} maxLength={3}
                  onChange={(e) => setTf({ ...tf, currency: e.target.value })} />
              </Field>
              <Field label={t('terms.padRate')}>
                <input className={inputCls} inputMode="decimal" value={tf.padRate}
                  onChange={(e) => setTf({ ...tf, padRate: e.target.value })} />
              </Field>
              <Field label={t('terms.hourlyRate')}>
                <input className={inputCls} inputMode="decimal" value={tf.hourlyRate}
                  onChange={(e) => setTf({ ...tf, hourlyRate: e.target.value })} />
              </Field>
              <Field label={t('terms.saturdayRate')}>
                <input className={inputCls} inputMode="decimal" value={tf.saturdayRate}
                  onChange={(e) => setTf({ ...tf, saturdayRate: e.target.value })} />
              </Field>
              <Field label={t('terms.sundayRate')}>
                <input className={inputCls} inputMode="decimal" value={tf.sundayRate}
                  onChange={(e) => setTf({ ...tf, sundayRate: e.target.value })} />
              </Field>
              <Field label={t('terms.holidayRate')}>
                <input className={inputCls} inputMode="decimal" value={tf.holidayRate}
                  onChange={(e) => setTf({ ...tf, holidayRate: e.target.value })} />
              </Field>
              <Field label={t('terms.mobilisationFee')}>
                <input className={inputCls} inputMode="decimal" value={tf.mobilisationFee}
                  onChange={(e) => setTf({ ...tf, mobilisationFee: e.target.value })} />
              </Field>
              <Field label={t('terms.overtimeAfterHours')}>
                <input className={inputCls} inputMode="numeric" value={tf.overtimeAfterHours}
                  onChange={(e) => setTf({ ...tf, overtimeAfterHours: e.target.value })} />
              </Field>
              <Field label={t('terms.overtimePercent')}>
                <input className={inputCls} inputMode="decimal" value={tf.overtimePercent}
                  onChange={(e) => setTf({ ...tf, overtimePercent: e.target.value })} />
              </Field>
              <Field label={t('terms.expenseMarkupPercent')}>
                <input className={inputCls} inputMode="decimal" value={tf.expenseMarkupPercent}
                  onChange={(e) => setTf({ ...tf, expenseMarkupPercent: e.target.value })} />
              </Field>
              <Field label={t('terms.padHoursPerDay')}>
                <input className={inputCls} inputMode="decimal" value={tf.padHoursPerDay}
                  onChange={(e) => setTf({ ...tf, padHoursPerDay: e.target.value })} />
              </Field>
              <Field label={t('terms.minPadPerBookedDay')}>
                <input className={inputCls} inputMode="decimal" value={tf.minPadPerBookedDay}
                  onChange={(e) => setTf({ ...tf, minPadPerBookedDay: e.target.value })} />
              </Field>
              <Field label={t('terms.carryOverMaxPercent')}>
                <input className={inputCls} inputMode="decimal" value={tf.carryOverMaxPercent}
                  onChange={(e) => setTf({ ...tf, carryOverMaxPercent: e.target.value })} />
              </Field>
              <Field label={t('terms.cancellationDeadlineBusinessDays')}>
                <input className={inputCls} inputMode="numeric" value={tf.cancellationDeadlineBusinessDays}
                  onChange={(e) => setTf({ ...tf, cancellationDeadlineBusinessDays: e.target.value })} />
              </Field>
              <Field label={t('terms.holidayCalendar')}>
                <input className={inputCls} value={tf.holidayCalendar}
                  onChange={(e) => setTf({ ...tf, holidayCalendar: e.target.value })} />
              </Field>
            </div>
            <div className="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-3">
              <Field label={t('terms.travelExpensePolicy')}>
                <input className={inputCls} value={tf.travelExpensePolicy}
                  onChange={(e) => setTf({ ...tf, travelExpensePolicy: e.target.value })} />
              </Field>
              <Field label={t('terms.indexation')}>
                <input className={inputCls} value={tf.indexation}
                  onChange={(e) => setTf({ ...tf, indexation: e.target.value })} />
              </Field>
              <Field label={t('common.notes')}>
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
              <th className={thCls}>{t('terms.padRate')}</th>
              <th className={thCls}>{t('terms.hourlyRate')}</th>
              <th className={thCls}>{t('terms.mobilisationFee')}</th>
              <th className={thCls}>{t('terms.carryOverMaxPercent')}</th>
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
                <td className={tdCls}>{fmt(v.padRate)}</td>
                <td className={tdCls}>{fmt(v.hourlyRate)}</td>
                <td className={tdCls}>{fmt(v.mobilisationFee)}</td>
                <td className={tdCls}>{v.carryOverMaxPercent}%</td>
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
              <tr><td className={tdCls} colSpan={9}>{t('common.none')}</td></tr>
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
              <Field label={t('common.name')}>
                <input className={inputCls} value={pf.name}
                  onChange={(e) => setPf({ ...pf, name: e.target.value })} />
              </Field>
              <Field label={t('projects.code')}>
                <input className={inputCls} value={pf.code}
                  onChange={(e) => setPf({ ...pf, code: e.target.value })} />
              </Field>
              <Field label={t('projects.description')}>
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
