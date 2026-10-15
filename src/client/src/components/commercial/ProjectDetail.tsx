import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createLocation,
  createQuantity,
  deleteLocation,
  deleteQuantity,
  getProject,
} from '@/lib/api';
import { numOrNull } from '@/lib/versioned';
import type { ProjectDetail as ProjectDetailDto } from '@/lib/types';
import { isAdmin, useMe } from '@/lib/useMe';
import {
  btnCls, btnDangerCls, Field, inputCls, Panel, tdCls, thCls,
} from '@/components/forms';

export function ProjectDetail({ projectId }: { projectId: string }) {
  const { t } = useTranslation();
  const me = useMe();
  const [project, setProject] = useState<ProjectDetailDto | null>(null);
  const [lf, setLf] = useState({ name: '', address: '', notes: '' });
  const [qf, setQf] = useState({ label: '', quantity: '', unit: '', notes: '' });

  // Parent mounts this with key={projectId}, so a new selection remounts with
  // fresh state — the effect only needs to fetch.
  const reload = useCallback(
    () => getProject(projectId).then(setProject).catch(() => setProject(null)),
    [projectId],
  );
  useEffect(() => { void reload(); }, [reload]);

  if (!project) return <p className="text-sm text-slate-400">{t('common.loading')}</p>;

  const saveLocation = async () => {
    await createLocation({
      projectId: project.id,
      name: lf.name.trim(),
      address: lf.address.trim() || null,
      notes: lf.notes.trim() || null,
    });
    setLf({ name: '', address: '', notes: '' });
    await reload();
  };

  const saveQuantity = async () => {
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

  return (
    <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
      <Panel title={`${t('locations.title')} — ${project.name}`}>
        <table className="mb-3 w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
              <th className={thCls}>{t('common.name')}</th>
              <th className={thCls}>{t('locations.address')}</th>
              {isAdmin(me) && <th className={thCls}>{t('common.actions')}</th>}
            </tr>
          </thead>
          <tbody>
            {project.locations.map((l) => (
              <tr key={l.id} className="border-b border-slate-100">
                <td className={tdCls}>{l.name}</td>
                <td className={tdCls}>{l.address ?? '—'}</td>
                {isAdmin(me) && (
                  <td className={tdCls}>
                    <button className={btnDangerCls}
                      onClick={() => void deleteLocation(l.id).then(reload)}>
                      {t('common.delete')}
                    </button>
                  </td>
                )}
              </tr>
            ))}
            {project.locations.length === 0 && (
              <tr><td className={tdCls} colSpan={3}>{t('common.none')}</td></tr>
            )}
          </tbody>
        </table>
        {isAdmin(me) && (
          <div className="flex flex-wrap items-end gap-2">
            <Field label={t('common.name')}>
              <input className={inputCls} value={lf.name}
                onChange={(e) => setLf({ ...lf, name: e.target.value })} />
            </Field>
            <Field label={t('locations.address')}>
              <input className={inputCls} value={lf.address}
                onChange={(e) => setLf({ ...lf, address: e.target.value })} />
            </Field>
            <button className={btnCls} disabled={!lf.name.trim()}
              onClick={() => void saveLocation()}>
              {t('locations.add')}
            </button>
          </div>
        )}
      </Panel>

      <Panel title={t('quantities.title')}>
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
            <Field label={t('quantities.quantity')}>
              <input className={inputCls} inputMode="decimal" value={qf.quantity}
                onChange={(e) => setQf({ ...qf, quantity: e.target.value })} />
            </Field>
            <Field label={t('quantities.unit')}>
              <input className={inputCls} value={qf.unit}
                onChange={(e) => setQf({ ...qf, unit: e.target.value })} />
            </Field>
            <button className={btnCls} disabled={!qf.label.trim() || !qf.unit.trim()}
              onClick={() => void saveQuantity()}>
              {t('quantities.add')}
            </button>
          </div>
        )}
      </Panel>
    </div>
  );
}
