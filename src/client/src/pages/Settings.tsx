import { Fragment, useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { describeError, getAudit } from '@/lib/api';
import {
  AUDIT_MAX_TAKE, AUDITED_ENTITY_TYPES, auditActor, auditValue, changedFields,
  type AuditEntry, type AuditFilters,
} from '@/lib/audit';
import { canReadAudit } from '@/lib/roles';
import { useDevUserReload, useMe } from '@/lib/useMe';
import {
  btnCls, btnSecondaryCls, ErrorBox, Field, inputCls, NumInput, Panel, tdCls, thCls,
} from '@/components/forms';

const emptyFilters: AuditFilters = { entityType: '', entityId: '', from: '', to: '', take: 200 };

/** Settings (§2.2.10) — currently the audit log viewer plus a note on the
 * personal calendar feeds. Admin/Finance only (mirrors GET /api/audit). */
export function Settings() {
  const { t } = useTranslation();
  const me = useMe();

  if (me && !canReadAudit(me)) {
    return <div className="mx-auto max-w-6xl px-4 py-8"><p className="text-sm text-slate-500">{t('common.noAccess')}</p></div>;
  }

  return (
    <div className="mx-auto max-w-6xl space-y-4 px-4 py-6">
      <AuditLog />
      <Panel title={t('settings.feedTitle')}>
        <p className="text-sm text-slate-600">{t('settings.feedBody')}</p>
      </Panel>
    </div>
  );
}

function AuditLog() {
  const { t } = useTranslation();
  const [filters, setFilters] = useState<AuditFilters>(emptyFilters);
  const [take, setTake] = useState(String(emptyFilters.take));
  const [applied, setApplied] = useState<AuditFilters>(emptyFilters);
  const [rows, setRows] = useState<AuditEntry[] | null>(null);
  const [open, setOpen] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(() => {
    setError(null);
    getAudit(applied).then(setRows)
      .catch((e) => { setRows([]); setError(describeError(e, t)); });
  }, [applied, t]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);
  useDevUserReload(reload);

  const apply = () => setApplied({ ...filters, take: Number(take.replace(',', '.')) || emptyFilters.take });

  return (
    <Panel title={t('settings.auditTitle')} help={t('settings.auditHelp')}>
      <div className="mb-3 flex flex-wrap items-end gap-2">
        <Field label={t('settings.entityType')}>
          <select className={inputCls} value={filters.entityType}
            onChange={(e) => setFilters({ ...filters, entityType: e.target.value })}>
            <option value="">{t('settings.allEntities')}</option>
            {AUDITED_ENTITY_TYPES.map((type) => (
              <option key={type} value={type}>{t(`settings.entity.${type}`)}</option>
            ))}
          </select>
        </Field>
        <Field label={t('settings.entityId')}>
          <input className={inputCls} value={filters.entityId}
            onChange={(e) => setFilters({ ...filters, entityId: e.target.value })} />
        </Field>
        <Field label={t('settings.from')}>
          <input type="date" className={inputCls} value={filters.from}
            onChange={(e) => setFilters({ ...filters, from: e.target.value })} />
        </Field>
        <Field label={t('settings.to')}>
          <input type="date" className={inputCls} value={filters.to}
            onChange={(e) => setFilters({ ...filters, to: e.target.value })} />
        </Field>
        <Field label={t('settings.take', { max: AUDIT_MAX_TAKE })}>
          <NumInput className={inputCls} value={take} onChange={(v) => setTake(v)} />
        </Field>
        <button className={btnCls} onClick={apply}>{t('settings.apply')}</button>
        <button className={btnSecondaryCls} onClick={() => {
          setFilters(emptyFilters); setTake(String(emptyFilters.take)); setApplied(emptyFilters);
        }}>
          {t('settings.reset')}
        </button>
      </div>
      <ErrorBox message={error} />
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('settings.when')}</th>
            <th className={thCls}>{t('settings.who')}</th>
            <th className={thCls}>{t('settings.action')}</th>
            <th className={thCls}>{t('settings.entityType')}</th>
            <th className={thCls}>{t('settings.fields')}</th>
          </tr>
        </thead>
        <tbody>
          {(rows ?? []).map((r) => (
            <Fragment key={r.id}>
              <tr className="cursor-pointer border-b border-slate-100 hover:bg-slate-50"
                onClick={() => setOpen(open === r.id ? null : r.id)}>
                <td className={tdCls}>{new Date(r.occurredUtc).toLocaleString()}</td>
                <td className={tdCls}>{auditActor(r, t('settings.system'))}</td>
                <td className={tdCls}>{t(`settings.actions.${r.action}`)}</td>
                <td className={tdCls}>{t(`settings.entity.${r.entityType}`, { defaultValue: r.entityType })}</td>
                <td className={tdCls}>
                  <span className="mr-1 text-slate-400">{open === r.id ? '▾' : '▸'}</span>
                  {changedFields(r) || '—'}
                </td>
              </tr>
              {open === r.id && (
                <tr className="border-b border-slate-100 bg-slate-50">
                  <td className={tdCls} colSpan={5}>
                    <p className="mb-1 text-xs text-slate-500">
                      {t('settings.entityId')}: <span className="font-mono">{r.entityId}</span>
                      {r.reason && <> · {t('settings.reason')}: {r.reason}</>}
                    </p>
                    <table className="w-full border-collapse text-xs">
                      <thead>
                        <tr>
                          <th className={thCls}>{t('settings.field')}</th>
                          <th className={thCls}>{t('settings.old')}</th>
                          <th className={thCls}>{t('settings.new')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {r.changes.map((c) => (
                          <tr key={c.field}>
                            <td className={tdCls}>{c.field}</td>
                            <td className={tdCls}>{auditValue(c.old)}</td>
                            <td className={tdCls}>{auditValue(c.new)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </td>
                </tr>
              )}
            </Fragment>
          ))}
          {rows === null && <tr><td className={tdCls} colSpan={5}>{t('common.loading')}</td></tr>}
          {rows?.length === 0 && <tr><td className={tdCls} colSpan={5}>{t('common.none')}</td></tr>}
        </tbody>
      </table>
    </Panel>
  );
}
