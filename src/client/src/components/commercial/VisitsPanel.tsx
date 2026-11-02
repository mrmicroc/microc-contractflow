import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getContractVisits } from '@/lib/api';
import { visitsSummary } from '@/lib/contractExtras';
import type { ContractVisits } from '@/lib/types';
import { cn } from '@/lib/utils';
import { Panel, tdCls, thCls } from '@/components/forms';

/** Visit counter (§4): mobilisations to visit-counted work packages,
 * numbered across the contract; beyond the included number travel bills. */
export function VisitsPanel({ contractId }: { contractId: string }) {
  const { t } = useTranslation();
  const [data, setData] = useState<ContractVisits | null>(null);

  useEffect(() => {
    getContractVisits(contractId).then(setData).catch(() => setData(null));
  }, [contractId]);

  // Nothing tracked yet and no limit set — keep the page quiet.
  if (!data || (data.included === null && data.used === 0)) return null;
  const summary = visitsSummary(data);

  return (
    <Panel title={t('visits.title')} help={t('visits.help')}>
      <p className="mb-2 text-sm text-slate-700">{t(summary.key, summary.params)}</p>
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-b border-slate-200">
            <th className={thCls}>{t('visits.number')}</th>
            <th className={thCls}>{t('common.resource')}</th>
            <th className={thCls}>{t('charges.project')}</th>
            <th className={thCls}>{t('common.period')}</th>
            <th className={thCls}>{t('common.status')}</th>
          </tr>
        </thead>
        <tbody>
          {data.visits.map((v) => (
            <tr key={v.periodId} className="border-b border-slate-100">
              <td className={tdCls}>#{v.number}</td>
              <td className={tdCls}>{v.resourceName}</td>
              <td className={tdCls}>{v.projectName} · {v.workPackageName}</td>
              <td className={tdCls}>{v.startDate} → {v.endDate}</td>
              <td className={tdCls}>
                <span className={cn('rounded px-1.5 py-0.5 text-xs',
                  v.withinLimit ? 'bg-green-100 text-green-800' : 'bg-amber-100 text-amber-800')}>
                  {v.withinLimit ? t('visits.within') : t('visits.beyond')}
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </Panel>
  );
}
