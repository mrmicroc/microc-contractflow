import { Fragment, useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createWorkPackage,
  createWorkPackageRate,
  createQuantity,
  deleteWorkPackage,
  deleteWorkPackageRate,
  deleteProject,
  deleteQuantity,
  errorMessage,
  getWorkPackageRates,
  getProject,
  getProjectAccess,
  getResources,
  grantProjectAccess,
  revokeProjectAccess,
} from '@/lib/api';
import { numOrNull } from '@/lib/versioned';
import { invalidNumericFields } from '@/lib/validation';
import type {
  WorkPackageRate, ProjectAccess, ProjectDetail as ProjectDetailDto,
  ResourceSummary,
} from '@/lib/types';
import { canSeeCommercial, isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, NumInput, Panel,
  SearchSelect, tdCls, thCls,
} from '@/components/forms';
import { cn } from '@/lib/utils';

export function ProjectDetail({
  projectId,
  onDeleted,
}: {
  projectId: string;
  onDeleted?: () => void;
}) {
  const { t } = useTranslation();
  const me = useMe();
  const [project, setProject] = useState<ProjectDetailDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [lf, setLf] = useState({ name: '', address: '', notes: '' });
  const [qf, setQf] = useState({ label: '', quantity: '', unit: '', notes: '' });
  const [ratesFor, setRatesFor] = useState<string | null>(null);
  const [access, setAccess] = useState<ProjectAccess[]>([]);
  const [resources, setResources] = useState<ResourceSummary[]>([]);
  const [grantId, setGrantId] = useState('');

  const canWrite = isAdmin(me);
  const canGrant = me !== null
    && ['Administrator', 'ProjectManager'].includes(me.role);

  // Parent mounts this with key={projectId}, so a new selection remounts with
  // fresh state — the effect only needs to fetch.
  const reload = useCallback(
    () => getProject(projectId).then(setProject).catch(() => setProject(null)),
    [projectId],
  );
  useEffect(() => { void reload(); }, [reload]);
  useEffect(() => {
    getProjectAccess(projectId).then(setAccess).catch(() => {});
    getResources().then(setResources).catch(() => {});
  }, [projectId]);

  if (!project) return <p className="text-sm text-slate-400">{t('common.loading')}</p>;

  const saveWorkPackage = async () => {
    await createWorkPackage({
      projectId: project.id,
      name: lf.name.trim(),
      address: lf.address.trim() || null,
      notes: lf.notes.trim() || null,
    });
    setLf({ name: '', address: '', notes: '' });
    await reload();
  };

  const saveQuantity = async () => {
    const bad = invalidNumericFields({
      [t('quantities.quantity')]: qf.quantity,
    });
    if (bad.length) { setError(t('common.invalidNumbers', { fields: bad.join(', ') })); return; }
    setError(null);
    await createQuantity({
      projectId: project.id,
      label: qf.label.trim(),
      quantity: numOrNull(qf.quantity) ?? 0,
      unit: qf.unit.trim(),
      notes: qf.notes.trim() || null,
    });
    setQf({ label: '', quantity: '', unit: '', notes: '' });
    await reload();
  };

  const remove = async () => {
    if (!window.confirm(t('common.confirmDelete'))) return;
    setError(null);
    try {
      await deleteProject(project.id);
      onDeleted?.();
    } catch (e) {
      setError(errorMessage(e));
    }
  };

  return (
    <div>
      {isAdmin(me) && (
        <div className="mb-2 flex items-center justify-end gap-2">
          {error && <span className="rounded bg-red-50 px-3 py-1 text-sm text-red-700">{error}</span>}
          <button className={btnDangerCls} onClick={() => void remove()}>
            {t('projects.deleteProject')}
          </button>
        </div>
      )}
      {!isAdmin(me) && error && (
        <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>
      )}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      <Panel title={`${t('workPackages.title')} — ${project.name}`} help={t('workPackages.help')}>
        <table className="mb-3 w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
              <th className={thCls}>{t('common.name')}</th>
              <th className={thCls}>{t('workPackages.address')}</th>
              {canSeeCommercial(me) && <th className={thCls}>{t('common.actions')}</th>}
            </tr>
          </thead>
          <tbody>
            {project.workPackages.map((l) => (
              <Fragment key={l.id}>
                <tr className="border-b border-slate-100">
                  <td className={tdCls}>{l.name}</td>
                  <td className={tdCls}>{l.address ?? '—'}</td>
                  {canSeeCommercial(me) && (
                    <td className={tdCls}>
                      <span className="flex gap-1">
                        <button className={btnSecondaryCls}
                          onClick={() => setRatesFor(ratesFor === l.id ? null : l.id)}>
                          {t('workPackageRates.toggle')}
                        </button>
                        {canWrite && (
                          <button className={btnDangerCls}
                            onClick={() => void deleteWorkPackage(l.id).then(reload)}>
                            {t('common.delete')}
                          </button>
                        )}
                      </span>
                    </td>
                  )}
                </tr>
                {ratesFor === l.id && (
                  <tr className="border-b border-slate-100 bg-slate-50">
                    <td colSpan={3} className="px-3 py-2">
                      <WorkPackageRates workPackageId={l.id} canWrite={canWrite} />
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
            {project.workPackages.length === 0 && (
              <tr><td className={tdCls} colSpan={3}>{t('common.none')}</td></tr>
            )}
          </tbody>
        </table>
        {canWrite && (
          <div className="flex flex-wrap items-end gap-2">
            <Field label={t('common.name')}>
              <input className={inputCls} value={lf.name}
                onChange={(e) => setLf({ ...lf, name: e.target.value })} />
            </Field>
            <Field label={t('workPackages.address')}>
              <input className={inputCls} value={lf.address}
                onChange={(e) => setLf({ ...lf, address: e.target.value })} />
            </Field>
            <button className={btnCls} disabled={!lf.name.trim()}
              onClick={() => void saveWorkPackage()}>
              {t('workPackages.add')}
            </button>
          </div>
        )}
      </Panel>

      <Panel title={t('quantities.title')} help={t('quantities.help')}>
        <table className="mb-3 w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
              <th className={thCls}>{t('quantities.label')}</th>
              <th className={thCls}>{t('quantities.quantity')}</th>
              <th className={thCls}>{t('quantities.unit')}</th>
              {isAdmin(me) && <th className={thCls}>{t('common.actions')}</th>}
            </tr>
          </thead>
          <tbody>
            {project.estimatedQuantities.map((q) => (
              <tr key={q.id} className="border-b border-slate-100">
                <td className={tdCls}>{q.label}</td>
                <td className={tdCls}>{q.quantity}</td>
                <td className={tdCls}>{q.unit}</td>
                {isAdmin(me) && (
                  <td className={tdCls}>
                    <button className={btnDangerCls}
                      onClick={() => void deleteQuantity(q.id).then(reload)}>
                      {t('common.delete')}
                    </button>
                  </td>
                )}
              </tr>
            ))}
            {project.estimatedQuantities.length === 0 && (
              <tr><td className={tdCls} colSpan={4}>{t('common.none')}</td></tr>
            )}
          </tbody>
        </table>
        {isAdmin(me) && (
          <div className="flex flex-wrap items-end gap-2">
            <Field label={t('quantities.label')}>
              <input className={inputCls} value={qf.label}
                onChange={(e) => setQf({ ...qf, label: e.target.value })} />
            </Field>
            <Field label={t('quantities.quantity')} help={t('quantities.hQuantity')}>
              <NumInput className={inputCls} value={qf.quantity}
                onChange={(v) => setQf({ ...qf, quantity: v })} />
            </Field>
            <Field label={t('quantities.unit')} help={t('quantities.hUnit')}>
              <input className={inputCls} value={qf.unit} list="unit-suggestions"
                onChange={(e) => setQf({ ...qf, unit: e.target.value })} />
              <datalist id="unit-suggestions">
                {(['pcs', 'hours', 'days', 'km', 'm', 'm2', 'm3', 'ton'] as const)
                  .map((u) => <option key={u} value={t(`quantities.units.${u}`)} />)}
              </datalist>
            </Field>
            <button className={btnCls} disabled={!qf.label.trim() || !qf.unit.trim()}
              onClick={() => void saveQuantity()}>
              {t('quantities.add')}
            </button>
          </div>
        )}
      </Panel>
      </div>

      {canSeeCommercial(me) && (
        <Panel title={t('access.title')} help={t('access.help')}>
          <p className="mb-2 text-xs text-slate-500">{t('access.note')}</p>
          <table className="mb-3 w-full border-collapse">
            <tbody>
              {access.map((a) => (
                <tr key={a.resourceId} className="border-b border-slate-100">
                  <td className={tdCls}>{a.resourceName}</td>
                  <td className={cn(tdCls, 'text-xs text-slate-500')}>
                    {t('access.since')} {a.createdUtc.slice(0, 10)}
                  </td>
                  <td className={tdCls}>
                    {canGrant && (
                      <button className={btnDangerCls}
                        onClick={() => void revokeProjectAccess(project.id, a.resourceId)
                          .then(() => getProjectAccess(project.id).then(setAccess))
                          .catch((e) => setError(errorMessage(e)))}>
                        {t('common.delete')}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
              {access.length === 0 && (
                <tr><td className={tdCls} colSpan={3}>{t('access.empty')}</td></tr>
              )}
            </tbody>
          </table>
          {canGrant && (
            <div className="flex items-end gap-2">
              <Field label={t('access.grantLabel')} help={t('access.grantHelp')}>
                <SearchSelect
                  value={grantId}
                  onChange={setGrantId}
                  emptyLabel={t('access.pick')}
                  options={resources
                    .filter((r) => !access.some((a) => a.resourceId === r.id))
                    .map((r) => ({ value: r.id, label: r.displayName }))}
                />
              </Field>
              <button className={btnCls} disabled={!grantId}
                onClick={() => void grantProjectAccess(project.id, grantId)
                  .then(() => getProjectAccess(project.id).then(setAccess))
                  .then(() => setGrantId(''))
                  .catch((e) => setError(errorMessage(e)))}>
                {t('access.grant')}
              </button>
            </div>
          )}
        </Panel>
      )}
    </div>
  );
}

/** Versioned package-rate overrides for one work package — set fields win over the
 * contract terms; fields left empty inherit (per-field fallback). */
function WorkPackageRates({ workPackageId, canWrite }: { workPackageId: string; canWrite: boolean }) {
  const { t } = useTranslation();
  const [rates, setRates] = useState<WorkPackageRate[]>([]);
  const [err, setErr] = useState<string | null>(null);
  const empty = {
    effectiveFrom: new Date().toISOString().slice(0, 10),
    hourlyRate: '', saturdayRate: '', sundayRate: '', holidayRate: '',
    dayRate: '', saturdayDayRate: '', sundayDayRate: '', holidayDayRate: '',
    halfDayRate: '', halfDayMaxHours: '', mobilisationFee: '', notes: '',
  };
  const [f, setF] = useState(empty);

  const reload = useCallback(
    () => getWorkPackageRates(workPackageId).then(setRates).catch(() => setRates([])),
    [workPackageId],
  );
  useEffect(() => { void reload(); }, [reload]);

  const save = async () => {
    const bad = invalidNumericFields({
      [t('terms.hourlyRate')]: f.hourlyRate,
      [t('terms.saturdayRate')]: f.saturdayRate,
      [t('terms.sundayRate')]: f.sundayRate,
      [t('terms.holidayRate')]: f.holidayRate,
      [t('terms.dayRate')]: f.dayRate,
      [t('terms.saturdayDayRate')]: f.saturdayDayRate,
      [t('terms.sundayDayRate')]: f.sundayDayRate,
      [t('terms.holidayDayRate')]: f.holidayDayRate,
      [t('terms.halfDayRate')]: f.halfDayRate,
      [t('terms.halfDayMaxHours')]: f.halfDayMaxHours,
      [t('terms.mobilisationFee')]: f.mobilisationFee,
    });
    if (bad.length) { setErr(t('common.invalidNumbers', { fields: bad.join(', ') })); return; }
    setErr(null);
    try {
      await createWorkPackageRate(workPackageId, {
        effectiveFrom: f.effectiveFrom,
        hourlyRate: numOrNull(f.hourlyRate), saturdayRate: numOrNull(f.saturdayRate),
        sundayRate: numOrNull(f.sundayRate), holidayRate: numOrNull(f.holidayRate),
        dayRate: numOrNull(f.dayRate), saturdayDayRate: numOrNull(f.saturdayDayRate),
        sundayDayRate: numOrNull(f.sundayDayRate), holidayDayRate: numOrNull(f.holidayDayRate),
        halfDayRate: numOrNull(f.halfDayRate),
        halfDayMaxHours: numOrNull(f.halfDayMaxHours),
        mobilisationFee: numOrNull(f.mobilisationFee),
        notes: f.notes.trim() || null,
      });
      setF(empty);
      await reload();
    } catch (e) { setErr(errorMessage(e)); }
  };

  const cell = (v: number | null) => v?.toString() ?? '—';
  return (
    <div>
      <p className="mb-1 text-xs text-slate-500">{t('workPackageRates.help')}</p>
      {rates.length > 0 && (
        <table className="mb-2 w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200 text-left">
              <th className={thCls}>{t('common.effectiveFrom')}</th>
              <th className={thCls}>{t('terms.hourlyRate')}</th>
              <th className={thCls}>{t('terms.weekendRates')}</th>
              <th className={thCls}>{t('terms.dayRate')}</th>
              <th className={thCls}>{t('terms.mobilisationFee')}</th>
              {canWrite && <th className={thCls}></th>}
            </tr>
          </thead>
          <tbody>
            {rates.map((r) => (
              <tr key={r.id} className="border-b border-slate-100">
                <td className={tdCls}>{r.effectiveFrom}</td>
                <td className={tdCls}>{cell(r.hourlyRate)}</td>
                <td className={tdCls}>
                  {[r.saturdayRate, r.sundayRate, r.holidayRate]
                    .map((v) => (v == null ? '—' : v)).join(' / ')}
                </td>
                <td className={tdCls}>
                  {[r.dayRate, r.saturdayDayRate, r.sundayDayRate, r.holidayDayRate]
                    .map((v) => (v == null ? '—' : v)).join(' / ')}
                  {r.halfDayRate != null && ` · ½ ${r.halfDayRate}`}
                </td>
                <td className={tdCls}>{cell(r.mobilisationFee)}</td>
                {canWrite && (
                  <td className={tdCls}>
                    <button className={btnDangerCls}
                      onClick={() => void deleteWorkPackageRate(workPackageId, r.id)
                        .then(reload).catch((e) => setErr(errorMessage(e)))}>
                      {t('common.delete')}
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {rates.length === 0 && (
        <p className="mb-2 text-xs text-slate-400">{t('workPackageRates.empty')}</p>
      )}
      {err && <p className="mb-1 text-xs text-red-600">{err}</p>}
      {canWrite && (
        <div className="flex flex-wrap items-end gap-2">
          <Field label={t('common.effectiveFrom')} help={t('workPackageRates.hEffective')}>
            <input className={inputCls} type="date" value={f.effectiveFrom}
              onChange={(e) => setF({ ...f, effectiveFrom: e.target.value })} />
          </Field>
          {([
            ['hourlyRate', 'terms.hourlyRate'],
            ['saturdayRate', 'terms.saturdayRate'],
            ['sundayRate', 'terms.sundayRate'],
            ['holidayRate', 'terms.holidayRate'],
            ['dayRate', 'terms.dayRate'],
            ['halfDayRate', 'terms.halfDayRate'],
            ['mobilisationFee', 'terms.mobilisationFee'],
          ] as const).map(([key, label]) => (
            <Field key={key} label={t(label)}>
              <NumInput className={cn(inputCls, 'w-24')} placeholder="—"
                value={f[key]}
                onChange={(v) => setF({ ...f, [key]: v })} />
            </Field>
          ))}
          <Field label={t('common.notes')}>
            <input className={inputCls} value={f.notes}
              onChange={(e) => setF({ ...f, notes: e.target.value })} />
          </Field>
          <button className={btnCls} onClick={() => void save()}>
            {t('workPackageRates.add')}
          </button>
        </div>
      )}
    </div>
  );
}
