import { useTranslation } from 'react-i18next';
import { Panel } from '@/components/forms';

const PLANNED = [
  'padUtilisation', 'baselineVsActual', 'bookedDays', 'resourceHours',
  'billingBasis', 'paymentStatements', 'projectMargin', 'quantities', 'forecast',
] as const;

/** Reports (§8) — not built yet; lists what is planned instead of showing
 * invented numbers. */
export function Reports() {
  const { t } = useTranslation();
  return (
    <div className="mx-auto max-w-3xl px-4 py-6">
      <Panel title={t('nav.reports')}>
        <p className="text-sm text-slate-600">{t('reports.comingSoon')}</p>
        <ul className="mt-2 list-disc pl-5 text-sm text-slate-600">
          {PLANNED.map((k) => <li key={k}>{t(`reports.planned.${k}`)}</li>)}
        </ul>
        <p className="mt-3 text-xs text-slate-500">{t('reports.meanwhile')}</p>
      </Panel>
    </div>
  );
}
