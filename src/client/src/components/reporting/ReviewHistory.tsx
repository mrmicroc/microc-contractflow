import { useTranslation } from 'react-i18next';
import { reviewHistory } from '@/lib/timesheets';
import type { TimesheetReview } from '@/lib/types';

/** A timesheet's review trail — submitted, returned, approved, reopened… —
 * oldest first, with each reviewer comment. */
export function ReviewHistory({ reviews }: { reviews: TimesheetReview[] | undefined }) {
  const { t } = useTranslation();
  const rows = reviewHistory(reviews);
  if (rows.length === 0) return null;
  return (
    <div className="mt-2">
      <h4 className="mb-1 text-xs font-semibold text-slate-500">{t('approvals.history')}</h4>
      <ol className="space-y-0.5 text-xs text-slate-600">
        {rows.map((r, i) => (
          <li key={`${r.atUtc}-${i}`}>
            <span className="text-slate-400">{new Date(r.atUtc).toLocaleString()}</span>
            {' · '}{t(`approvals.reviewAction.${r.action}`)}
            {r.byName && <> · {r.byName}</>}
            {r.note && <span className="block pl-3 italic">“{r.note}”</span>}
          </li>
        ))}
      </ol>
    </div>
  );
}
