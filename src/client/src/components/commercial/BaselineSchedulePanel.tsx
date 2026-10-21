import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getBaselineSchedule, putBaselineSchedule } from '@/lib/api';
import {
  addYear, cellKey, changedMonths, closedKeys, grandTotal, gridFromSchedule, invalidCells, MONTHS,
  scheduleDifference, yearTotal, type ScheduleGrid,
} from '@/lib/baselineSchedule';
import { localMonth } from '@/lib/dates';
import { describeError, errorBody, errorCode } from '@/lib/errors';
import type { BaselineSchedule } from '@/lib/types';
import { cn } from '@/lib/utils';
import { btnCls, btnSecondaryCls, ErrorBox, Panel, tdCls, thCls } from '@/components/forms';

const fmt = (n: number) => String(Math.round(n * 100) / 100);

/** The project's PAD baseline per month (contract §2) as a years × Jan–Dec
 * grid, checked against the contracted total. Admin edits; others read. */
export function BaselineSchedulePanel({ projectId, contracted, canEdit }: {
  projectId: string;
  contracted: number | null;
  canEdit: boolean;
}) {
  const { t, i18n } = useTranslation();
  const [stored, setStored] = useState<BaselineSchedule | null>(null);
  const [grid, setGrid] = useState<ScheduleGrid | null>(null);
  const [closed, setClosed] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const thisYear = localMonth().year;

  const reload = useCallback(async () => {
    try {
      const s = await getBaselineSchedule(projectId);
      setStored(s);
      setGrid(gridFromSchedule(s, thisYear));
    } catch { setStored(null); setGrid(gridFromSchedule(null, thisYear)); }
  }, [projectId, thisYear]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  if (!grid) return null;
  if (!canEdit && (stored?.months.length ?? 0) === 0) return null;

  const monthName = (m: number) =>
    new Date(2000, m - 1, 1).toLocaleString(i18n.language, { month: 'short' });
  const total = grandTotal(grid);
  const diff = scheduleDifference(contracted, total);
  const bad = invalidCells(grid);
  const changes = changedMonths(grid, stored);

  const save = async () => {
    setError(null);
    setSaved(false);
    try {
      const s = await putBaselineSchedule(projectId, changes);
      setStored(s);
      setGrid(gridFromSchedule(s, thisYear));
      setSaved(true);
    } catch (e) {
      if (errorCode(e) === 'monthClosed') {
        const months = errorBody(e)?.months;
        setClosed(closedKeys(months));
        setError(t('baselineSchedule.closedMonths', {
          months: Array.isArray(months) ? months.join(', ') : '',
        }));
      } else setError(describeError(e, t));
    }
  };

  return (
    <Panel title={t('baselineSchedule.title')} help={t('baselineSchedule.help')}>
      <ErrorBox message={error} />
      {saved && <p className="mb-2 text-sm text-green-700">{t('baselineSchedule.saved')}</p>}
      <div className="overflow-x-auto">
        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
              <th className={thCls}>{t('common.year')}</th>
              {MONTHS.map((m) => <th key={m} className={thCls}>{monthName(m)}</th>)}
              <th className={thCls}>{t('common.total')}</th>
            </tr>
          </thead>
          <tbody>
            {grid.years.map((y) => (
              <tr key={y} className="border-b border-slate-100">
                <td className={cn(tdCls, 'font-medium')}>{y}</td>
                {MONTHS.map((m) => {
                  const k = cellKey(y, m);
                  const isClosed = closed.has(k);
                  return (
                    <td key={m} className="px-0.5 py-1">
                      {canEdit && !isClosed ? (
                        <input
                          className={cn('w-14 rounded border px-1 py-0.5 text-right text-sm',
                            bad.includes(k) ? 'border-red-400' : 'border-slate-300')}
                          inputMode="decimal"
                          value={grid.cells[k] ?? ''}
                          onChange={(e) => {
                            setSaved(false);
                            setGrid({ ...grid, cells: { ...grid.cells, [k]: e.target.value } });
                          }}
                        />
                      ) : (
                        <span className={cn('block w-14 text-right text-sm', isClosed && 'text-slate-400')}
                          title={isClosed ? t('baselineSchedule.closedCell') : undefined}>
                          {grid.cells[k] || '—'}
                        </span>
                      )}
                    </td>
                  );
                })}
                <td className={cn(tdCls, 'font-medium')}>{fmt(yearTotal(grid, y))}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="mt-2 text-sm text-slate-700">
        {t('baselineSchedule.totals', { total: fmt(total) })}
        {contracted !== null && ` · ${t('baselineSchedule.contracted', { contracted: fmt(contracted) })}`}
      </p>
      {diff !== null && diff !== 0 && (
        <p className="mt-1 rounded bg-amber-50 px-2 py-1 text-sm text-amber-800">
          {t('baselineSchedule.difference', { diff: fmt(diff) })}
        </p>
      )}
      {canEdit && (
        <div className="mt-2 flex flex-wrap gap-2">
          <button className={btnSecondaryCls} onClick={() => setGrid(addYear(grid, true))}>
            {t('baselineSchedule.addYearBefore')}
          </button>
          <button className={btnSecondaryCls} onClick={() => setGrid(addYear(grid))}>
            {t('baselineSchedule.addYear')}
          </button>
          <button className={btnCls} disabled={bad.length > 0 || changes.length === 0}
            onClick={() => void save()}>
            {t('baselineSchedule.save', { count: changes.length })}
          </button>
        </div>
      )}
    </Panel>
  );
}
