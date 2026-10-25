import { Fragment, useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  addProjectResource,
  createWorkPackage,
  createWorkPackageRate,
  createQuantity,
  deleteWorkPackage,
  deleteWorkPackageRate,
  deleteProject,
  deleteQuantity,
  describeError,
  getWorkPackageRates,
  getProject,
  getProjectAccess,
  getProjectResources,
  getResources,
  grantProjectAccess,
  removeProjectResource,
  reviewWorkPackageRate,
  revokeProjectAccess,
  updateProject,
  updateQuantity,
  updateWorkPackage,
} from '@/lib/api';
import { confirmAction } from '@/lib/confirm';
import { localTodayIso } from '@/lib/dates';
import { numOrNull } from '@/lib/versioned';
import { invalidNumericFields } from '@/lib/validation';
import type {
  EstimatedQuantity, WorkPackage, WorkPackagePricing, WorkPackageRate, ProjectAccess,
  ProjectDetail as ProjectDetailDto, ProjectResource, ResourceSummary,
} from '@/lib/types';
import { canManage, canSeeCommercial, isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, btnSecondaryCls, ErrorBox, Field, inputCls, NumInput, Panel,
  SearchSelect, tdCls, thCls,
} from '@/components/forms';
import { cn } from '@/lib/utils';
import { BaselineSchedulePanel } from './BaselineSchedulePanel';
import { useFeatures } from '@/lib/features';

const EMPTY_WP = {
  name: '', notes: '', pricingMode: 'contract' as WorkPackagePricing, countsAsVisit: false,
};

const INFO_TYPES = ['address', 'access', 'contact', 'coordinates', 'other'] as const;

export function ProjectDetail({
  projectId,
  onDeleted,
  onChanged,
}: {
  projectId: string;
  onDeleted?: () => void;
  onChanged?: () => void;
}) {
  const { t } = useTranslation();
  const me = useMe();
  const pad = useFeatures().pad;
  const [project, setProject] = useState<ProjectDetailDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [lf, setLf] = useState(EMPTY_WP);
  const [lfInfo, setLfInfo] = useState<{ type: string; description: string }[]>([]);
  // Editing an existing work package reuses the add form (wpEditId set).
  const [wpEditId, setWpEditId] = useState<string | null>(null);
  const [qf, setQf] = useState({ label: '', quantity: '', unit: '', notes: '' });
  const [qEditId, setQEditId] = useState<string | null>(null);
  const [pf, setPf] = useState<{
    name: string; code: string; description: string; isActive: boolean; contractedBaselinePad: string;
  } | null>(null);
  const [ratesFor, setRatesFor] = useState<string | null>(null);
  const [access, setAccess] = useState<ProjectAccess[]>([]);
  const [resources, setResources] = useState<ResourceSummary[]>([]);
  const [members, setMembers] = useState<ProjectResource[]>([]);
  const [grantId, setGrantId] = useState('');
  const [memberId, setMemberId] = useState('');

  const canWrite = isAdmin(me);
  const canGrant = canManage(me);

  // Parent mounts this with key={projectId}, so a new selection remounts with
  // fresh state — the effect only needs to fetch.
  const reload = useCallback(
    () => getProject(projectId).then(setProject).catch(() => setProject(null)),
    [projectId],
  );
  useEffect(() => { void reload(); }, [reload]);
  const reloadMembers = useCallback(
    () => getProjectResources(projectId).then(setMembers).catch(() => {}),
    [projectId],
  );
  useEffect(() => {
    getProjectAccess(projectId).then(setAccess).catch(() => {});
    getResources().then(setResources).catch(() => {});
    void reloadMembers();
  }, [projectId, reloadMembers]);

  if (!project) return <p className="text-sm text-slate-400">{t('common.loading')}</p>;

  // Every write reports its failure instead of failing silently.
  const guarded = async (fn: () => Promise<unknown>) => {
    setError(null);
    try { await fn(); return true; }
    catch (e) { setError(describeError(e, t)); return false; }
  };
  const confirmed = async (fn: () => Promise<unknown>) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    await guarded(fn);
  };

  const saveWorkPackage = () => guarded(async () => {
    const rows = lfInfo.filter((i) => i.type.trim() && i.description.trim());
    const body = {
      projectId: project.id,
      name: lf.name.trim(),
      notes: lf.notes.trim() || null,
      infoItems: rows.map((i) => ({ type: i.type.trim(), description: i.description.trim() })),
      pricingMode: lf.pricingMode,
      countsAsVisit: lf.countsAsVisit,
    };
    if (wpEditId) await updateWorkPackage(wpEditId, body);
    else await createWorkPackage(body);
    setLf(EMPTY_WP);
    setLfInfo([]);
    setWpEditId(null);
    await reload();
  });

  const editWorkPackage = (w: WorkPackage) => {
    setWpEditId(w.id);
    setLf({
      name: w.name, notes: w.notes ?? '',
      pricingMode: w.pricingMode ?? 'contract', countsAsVisit: w.countsAsVisit ?? false,
    });
    setLfInfo(w.infoItems.map((i) => ({ type: i.type, description: i.description })));
  };

  const saveQuantity = async () => {
    const bad = invalidNumericFields({
      [t('quantities.quantity')]: qf.quantity,
    });
    if (bad.length) { setError(t('common.invalidNumbers', { fields: bad.join(', ') })); return; }
    await guarded(async () => {
      const body = {
        projectId: project.id,
        label: qf.label.trim(),
        quantity: numOrNull(qf.quantity) ?? 0,
        unit: qf.unit.trim(),
        notes: qf.notes.trim() || null,
      };
      if (qEditId) await updateQuantity(qEditId, body);
      else await createQuantity(body);
      setQf({ label: '', quantity: '', unit: '', notes: '' });
      setQEditId(null);
      await reload();
    });
  };

  const editQuantity = (q: EstimatedQuantity) => {
    setQEditId(q.id);
    setQf({ label: q.label, quantity: String(q.quantity), unit: q.unit, notes: q.notes ?? '' });
  };

  const saveProject = async () => {
    if (!pf) return;
    const bad = invalidNumericFields({ [t('projects.contractedBaselinePad')]: pf.contractedBaselinePad });
    if (bad.length) { setError(t('common.invalidNumbers', { fields: bad.join(', ') })); return; }
    const ok = await guarded(() => updateProject(project.id, {
      contractId: project.contractId,
      name: pf.name.trim(),
      code: pf.code.trim() || null,
      description: pf.description.trim() || null,
      isActive: pf.isActive,
      contractedBaselinePad: numOrNull(pf.contractedBaselinePad),
    }));
    if (!ok) return;
    setPf(null);
    await reload();
    onChanged?.();
  };

  const remove = async () => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    if (await guarded(() => deleteProject(project.id))) onDeleted?.();
  };

  return (
    <div>
      <ErrorBox message={error} />
      {isAdmin(me) && (
        <div className="mb-2 flex items-center justify-end gap-2">
          {project.description && !pf && (
            <span className="mr-auto text-sm text-slate-600">{project.description}</span>
          )}
          {!pf && (
            <button className={btnSecondaryCls} onClick={() => setPf({
              name: project.name, code: project.code ?? '',
              description: project.description ?? '', isActive: project.isActive,
              contractedBaselinePad: project.contractedBaselinePad === null || project.contractedBaselinePad === undefined
                ? '' : String(project.contractedBaselinePad),
            })}>
              {t('projects.editProject')}
            </button>
          )}
          <button className={btnDangerCls} onClick={() => void remove()}>
            {t('projects.deleteProject')}
          </button>
        </div>
      )}
      {pf && (
        <div className="mb-3 rounded border border-slate-200 bg-slate-50 p-3">
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-4">
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
            <Field label={t('common.active')}>
              <span className="flex items-center gap-1.5 text-sm text-slate-700">
                <input type="checkbox" checked={pf.isActive}
                  onChange={(e) => setPf({ ...pf, isActive: e.target.checked })} />
                {pf.isActive ? t('common.active') : t('common.inactive')}
              </span>
            </Field>
            {pad && <Field label={t('projects.contractedBaselinePad')} help={t('projects.hContractedBaselinePad')}>
              <NumInput className={inputCls} value={pf.contractedBaselinePad}
                onChange={(v) => setPf({ ...pf, contractedBaselinePad: v })} />
            </Field>}
          </div>
          <div className="mt-3 flex gap-2">
            <button className={btnCls} disabled={!pf.name.trim()} onClick={() => void saveProject()}>
              {t('common.save')}
            </button>
            <button className={btnSecondaryCls} onClick={() => setPf(null)}>{t('common.cancel')}</button>
          </div>
        </div>
      )}
      {pad && canSeeCommercial(me) && (
        <div className="mb-4">
          <BaselineSchedulePanel projectId={project.id}
            contracted={project.contractedBaselinePad ?? null} canEdit={isAdmin(me)} />
        </div>
      )}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      <Panel title={`${t('workPackages.title')} — ${project.name}`} help={t('workPackages.help')}>
        <table className="mb-3 w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
              <th className={thCls}>{t('common.name')}</th>
              <th className={thCls}>{t('workPackages.info')}</th>
              {canSeeCommercial(me) && <th className={thCls}>{t('common.actions')}</th>}
            </tr>
          </thead>
          <tbody>
            {project.workPackages.map((l) => (
              <Fragment key={l.id}>
                <tr className="border-b border-slate-100">
                  <td className={tdCls}>
                    {l.name}
                    {l.pricingMode === 'outsideContract' && (
                      <span className="ml-1.5 rounded bg-amber-100 px-1.5 py-0.5 text-[10px] text-amber-800">
                        {t('workPackages.outsideTag')}
                      </span>
                    )}
                    {l.countsAsVisit && (
                      <span className="ml-1.5 rounded bg-sky-100 px-1.5 py-0.5 text-[10px] text-sky-800">
                        {t('workPackages.visitTag')}
                      </span>
                    )}
                  </td>
                  <td className={tdCls}>
                    {l.infoItems.length === 0 ? '—' : l.infoItems
                      .map((i) => `${i.type}: ${i.description}`).join(' · ')}
                  </td>
                  {canSeeCommercial(me) && (
                    <td className={tdCls}>
                      <span className="flex gap-1">
                        <button className={btnSecondaryCls}
                          onClick={() => setRatesFor(ratesFor === l.id ? null : l.id)}>
                          {t('workPackageRates.toggle')}
                        </button>
                        {canWrite && (
                          <>
                            <button className={btnSecondaryCls} onClick={() => editWorkPackage(l)}>
                              {t('common.edit')}
                            </button>
                            <button className={btnDangerCls}
                              onClick={() => void confirmed(async () => {
                                await deleteWorkPackage(l.id);
                                if (wpEditId === l.id) { setWpEditId(null); setLf(EMPTY_WP); setLfInfo([]); }
                                await reload();
                              })}>
                              {t('common.delete')}
                            </button>
                          </>
                        )}
                      </span>
                    </td>
                  )}
                </tr>
                {ratesFor === l.id && (
                  <tr className="border-b border-slate-100 bg-slate-50">
                    <td colSpan={3} className="px-3 py-2">
                      <WorkPackageRates workPackageId={l.id} projectId={project.id}
                        members={members} canWrite={canWrite} canGrant={canGrant} />
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
          <div className="space-y-2">
            <div className="flex flex-wrap items-end gap-2">
              <Field label={t('common.name')}>
                <input className={inputCls} value={lf.name}
                  onChange={(e) => setLf({ ...lf, name: e.target.value })} />
              </Field>
              <Field label={t('workPackages.notes')}>
                <input className={inputCls} value={lf.notes}
                  onChange={(e) => setLf({ ...lf, notes: e.target.value })} />
              </Field>
              <Field label={t('workPackages.pricingMode')} help={t('workPackages.hPricingMode')}>
                <select className={inputCls} value={lf.pricingMode}
                  onChange={(e) => setLf({ ...lf, pricingMode: e.target.value as WorkPackagePricing })}>
                  <option value="contract">{t('workPackages.pmContract')}</option>
                  <option value="outsideContract">{t('workPackages.pmOutside')}</option>
                </select>
              </Field>
              <Field label={t('workPackages.countsAsVisit')} help={t('workPackages.hCountsAsVisit')}>
                <span className="flex items-center gap-1.5 text-sm text-slate-700">
                  <input type="checkbox" checked={lf.countsAsVisit}
                    onChange={(e) => setLf({ ...lf, countsAsVisit: e.target.checked })} />
                  {lf.countsAsVisit ? t('common.yes') : t('common.no')}
                </span>
              </Field>
              <button className={btnCls} disabled={!lf.name.trim()}
                onClick={() => void saveWorkPackage()}>
                {wpEditId ? t('common.save') : t('workPackages.add')}
              </button>
              {wpEditId && (
                <button className={btnSecondaryCls}
                  onClick={() => { setWpEditId(null); setLf(EMPTY_WP); setLfInfo([]); }}>
                  {t('common.cancel')}
                </button>
              )}
            </div>
            <div>
              <div className="mb-1 flex items-center gap-2">
                <span className="text-xs font-medium text-slate-500">
                  {t('workPackages.infoFields')}
                </span>
                <button className={btnSecondaryCls}
                  onClick={() => setLfInfo([...lfInfo, { type: '', description: '' }])}>
                  {t('workPackages.addInfo')}
                </button>
              </div>
              {lfInfo.map((row, i) => (
                <div key={i} className="mb-1 flex items-center gap-1">
                  <input className={cn(inputCls, 'w-36')} list="wp-info-types"
                    placeholder={t('workPackages.infoType')} value={row.type}
                    onChange={(e) => setLfInfo(lfInfo.map((r, j) =>
                      j === i ? { ...r, type: e.target.value } : r))} />
                  <input className={cn(inputCls, 'flex-1')}
                    placeholder={t('workPackages.infoDescription')} value={row.description}
                    onChange={(e) => setLfInfo(lfInfo.map((r, j) =>
                      j === i ? { ...r, description: e.target.value } : r))} />
                  <button className={btnDangerCls}
                    onClick={() => setLfInfo(lfInfo.filter((_, j) => j !== i))}>
                    {t('common.delete')}
                  </button>
                </div>
              ))}
              <datalist id="wp-info-types">
                {INFO_TYPES.map((x) => (
                  <option key={x} value={t(`workPackages.infoTypes.${x}`)} />
                ))}
              </datalist>
            </div>
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
                    <span className="flex gap-1">
                      <button className={btnSecondaryCls} onClick={() => editQuantity(q)}>
                        {t('common.edit')}
                      </button>
                      <button className={btnDangerCls}
                        onClick={() => void confirmed(async () => { await deleteQuantity(q.id); await reload(); })}>
                        {t('common.delete')}
                      </button>
                    </span>
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
              {qEditId ? t('common.save') : t('quantities.add')}
            </button>
            {qEditId && (
              <button className={btnSecondaryCls}
                onClick={() => { setQEditId(null); setQf({ label: '', quantity: '', unit: '', notes: '' }); }}>
                {t('common.cancel')}
              </button>
            )}
          </div>
        )}
      </Panel>
      </div>

      {canSeeCommercial(me) && (
        <Panel title={t('roster.title')} help={t('roster.help')}>
          <table className="mb-3 w-full border-collapse">
            <tbody>
              {members.map((m) => (
                <tr key={m.resourceId} className="border-b border-slate-100">
                  <td className={tdCls}>{m.resourceName}</td>
                  <td className={cn(tdCls, 'text-xs text-slate-500')}>
                    {t(`resources.type${m.resourceType[0].toUpperCase()}${m.resourceType.slice(1)}`)}
                  </td>
                  <td className={tdCls}>
                    {m.pendingReviews > 0 && (
                      <span className="rounded bg-amber-100 px-2 py-0.5 text-xs text-amber-800">
                        {t('roster.pendingReview', { count: m.pendingReviews })}
                      </span>
                    )}
                  </td>
                  <td className={tdCls}>
                    {canGrant && (
                      <button className={btnDangerCls}
                        onClick={() => void confirmed(async () => {
                          await removeProjectResource(project.id, m.resourceId);
                          await reloadMembers();
                        })}>
                        {t('common.delete')}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
              {members.length === 0 && (
                <tr><td className={tdCls} colSpan={4}>{t('roster.empty')}</td></tr>
              )}
            </tbody>
          </table>
          {canGrant && (
            <div className="flex items-end gap-2">
              <Field label={t('roster.addLabel')} help={t('roster.addHelp')}>
                <SearchSelect
                  value={memberId}
                  onChange={setMemberId}
                  emptyLabel={t('roster.pick')}
                  options={resources
                    .filter((r) => !members.some((m) => m.resourceId === r.id))
                    .map((r) => ({ value: r.id, label: r.displayName }))}
                />
              </Field>
              <button className={btnCls} disabled={!memberId}
                onClick={() => void guarded(async () => {
                  await addProjectResource(project.id, memberId);
                  await reloadMembers();
                  setMemberId('');
                })}>
                {t('roster.add')}
              </button>
            </div>
          )}
        </Panel>
      )}

      {canSeeCommercial(me) && (
        <Panel title={t('access.title')} help={t('access.help')}>
          <p className="mb-2 text-xs text-slate-500">{t('access.note')}</p>
          <table className="mb-3 w-full border-collapse">
            <tbody>
              {access.map((a) => (
                <tr key={a.resourceId} className="border-b border-slate-100">
                  <td className={tdCls}>{a.resourceName}</td>
                  <td className={cn(tdCls, 'text-xs text-slate-500')}>
                    {t('access.since')} {new Date(a.createdUtc).toLocaleDateString()}
                  </td>
                  <td className={tdCls}>
                    {canGrant && (
                      <button className={btnDangerCls}
                        onClick={() => void confirmed(async () => {
                          await revokeProjectAccess(project.id, a.resourceId);
                          setAccess(await getProjectAccess(project.id));
                        })}>
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
                onClick={() => void guarded(async () => {
                  await grantProjectAccess(project.id, grantId);
                  setAccess(await getProjectAccess(project.id));
                  setGrantId('');
                })}>
                {t('access.grant')}
              </button>
            </div>
          )}
        </Panel>
      )}
    </div>
  );
}

/** Versioned package-rate overrides for one work package — package-wide rows
 * plus resource×package matrix cells; set fields win over contract terms,
 * empty inherit per field (resource cell > package row > contract). */
function WorkPackageRates({ workPackageId, members, canWrite, canGrant }: {
  workPackageId: string; projectId: string;
  members: ProjectResource[]; canWrite: boolean; canGrant: boolean;
}) {
  const { t } = useTranslation();
  const [rates, setRates] = useState<WorkPackageRate[]>([]);
  const [err, setErr] = useState<string | null>(null);
  const empty = {
    resourceId: '',
    effectiveFrom: localTodayIso(),
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
        resourceId: f.resourceId || null,
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
    } catch (e) { setErr(describeError(e, t)); }
  };

  const removeRate = async (rateId: string) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    try { await deleteWorkPackageRate(workPackageId, rateId); await reload(); }
    catch (e) { setErr(describeError(e, t)); }
  };

  const cell = (v: number | null) => v?.toString() ?? '—';
  return (
    <div>
      <p className="mb-1 text-xs text-slate-500">{t('workPackageRates.help')}</p>
      {rates.length > 0 && (
        <table className="mb-2 w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200 text-left">
              <th className={thCls}>{t('workPackageRates.scope')}</th>
              <th className={thCls}>{t('common.effectiveFrom')}</th>
              <th className={thCls}>{t('terms.hourlyRate')}</th>
              <th className={thCls}>{t('terms.weekendRates')}</th>
              <th className={thCls}>{t('terms.dayRate')}</th>
              <th className={thCls}>{t('terms.mobilisationFee')}</th>
              {(canWrite || canGrant) && <th className={thCls}></th>}
            </tr>
          </thead>
          <tbody>
            {rates.map((r) => (
              <tr key={r.id} className="border-b border-slate-100">
                <td className={tdCls}>
                  {r.resourceName ?? t('workPackageRates.packageDefault')}
                  {r.needsReview && (
                    <span className="ml-1 rounded bg-amber-100 px-1.5 py-0.5 text-xs text-amber-800">
                      {t('workPackageRates.needsReview')}
                    </span>
                  )}
                </td>
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
                {(canWrite || canGrant) && (
                  <td className={tdCls}>
                    <span className="flex gap-1">
                      {canGrant && r.needsReview && (
                        <button className={btnSecondaryCls}
                          onClick={() => void reviewWorkPackageRate(workPackageId, r.id)
                            .then(reload).catch((e) => setErr(describeError(e, t)))}>
                          {t('workPackageRates.markReviewed')}
                        </button>
                      )}
                      {canWrite && (
                        <button className={btnDangerCls}
                          onClick={() => void removeRate(r.id)}>
                          {t('common.delete')}
                        </button>
                      )}
                    </span>
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
      {canGrant && (
        <div className="flex flex-wrap items-end gap-2">
          <Field label={t('workPackageRates.scope')} help={t('workPackageRates.hScope')}>
            <SearchSelect
              value={f.resourceId}
              onChange={(v) => setF({ ...f, resourceId: v })}
              emptyLabel={t('workPackageRates.packageDefault')}
              options={members.map((m) => ({ value: m.resourceId, label: m.resourceName }))}
            />
          </Field>
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
            ['saturdayDayRate', 'terms.saturdayDayRate'],
            ['sundayDayRate', 'terms.sundayDayRate'],
            ['holidayDayRate', 'terms.holidayDayRate'],
            ['halfDayRate', 'terms.halfDayRate'],
            ['halfDayMaxHours', 'terms.halfDayMaxHours'],
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
