import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  bookingAction, describeError, getBookings, getFeedUrl, getResources, rotateFeedUrl,
} from '@/lib/api';
import { addDays, availableActions, isTerminal, statusClasses } from '@/lib/bookings';
import { confirmAction } from '@/lib/confirm';
import { localTodayIso } from '@/lib/dates';
import { useDevUserReload, useMe } from '@/lib/useMe';
import type { Booking, ResourceSummary } from '@/lib/types';
import { cn } from '@/lib/utils';
import { btnSecondaryCls, ErrorBox, Panel, tdCls, thCls } from '@/components/forms';
import { WeekReport } from '@/components/reporting/WeekReport';

/** How far back unfinished assignments still show (yesterday's in-progress
 * day must stay reachable) and how far ahead upcoming ones are listed. */
const PAST_DAYS = 60;
const AHEAD_DAYS = 60;

/** The caller's own assignments — acknowledge, start (design §2 My Work). */
export function MyWork() {
  const { t } = useTranslation();
  const me = useMe();
  const [bookings, setBookings] = useState<Booking[]>([]);
  const [allResources, setAllResources] = useState<ResourceSummary[] | null>(null);
  const [feedUrl, setFeedUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(() => {
    const today = localTodayIso();
    getBookings({ from: addDays(today, -PAST_DAYS), to: addDays(today, AHEAD_DAYS) })
      .then(setBookings)
      .catch((e) => { setBookings([]); setError(describeError(e, t)); });
    getFeedUrl().then((r) => setFeedUrl(r.url)).catch(() => setFeedUrl(null));
  }, [t]);
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);
  useDevUserReload(reload);

  // Admin/PM see every booking from the API — "my work" is only the
  // resources linked to the signed-in user. Resource callers are already
  // scoped server-side.
  const isResource = me?.role === 'Resource';
  useEffect(() => {
    if (!me || isResource) return;
    getResources().then(setAllResources).catch(() => setAllResources([]));
  }, [me, isResource]);
  const ownResourceIds = useMemo(() => (isResource || !me
    ? null
    : new Set((allResources ?? []).filter((r) => r.userId === me.id).map((r) => r.id))),
  [isResource, me, allResources]);

  const run = async (b: Booking, action: 'acknowledge' | 'start') => {
    setError(null);
    try { await bookingAction(b.id, action); reload(); }
    catch (e) { setError(describeError(e, t)); }
  };

  const rotate = async () => {
    if (!confirmAction(t('myWork.confirmRegenerateFeed'))) return;
    try { setFeedUrl((await rotateFeedUrl()).url); }
    catch (e) { setError(describeError(e, t)); }
  };

  const mine = bookings.filter((b) => !isTerminal(b.status)
    && (ownResourceIds === null || ownResourceIds.has(b.resourceId)));
  const today = localTodayIso();

  return (
    <div className="mx-auto max-w-4xl space-y-4 px-4 py-6">
      <Panel title={t('myWork.title')} help={t('myWork.help')}>
        <ErrorBox message={error} />
        {feedUrl && (
          <p className="mb-3 flex items-center gap-2 text-xs text-slate-600">
            {t('myWork.calendarFeed')}
            <code className="max-w-96 truncate rounded bg-slate-100 px-1.5 py-0.5">
              {window.location.origin}{feedUrl}
            </code>
            <button className={btnSecondaryCls} onClick={() => void rotate()}>
              {t('myWork.regenerateFeed')}
            </button>
          </p>
        )}
        {ownResourceIds !== null && ownResourceIds.size === 0 && (
          <p className="mb-2 text-xs text-slate-500">{t('myWork.noLinkedResource')}</p>
        )}
        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
              <th className={thCls}>{t('booking.date')}</th>
              <th className={thCls}>{t('booking.project')}</th>
              <th className={thCls}>{t('booking.customer')}</th>
              <th className={thCls}>{t('booking.workPackage')}</th>
              <th className={thCls}>{t('booking.plannedHours')}</th>
              <th className={thCls}>{t('booking.statusLabel')}</th>
              <th className={thCls}>{t('common.actions')}</th>
            </tr>
          </thead>
          <tbody>
            {mine.map((b) => {
              const actions = availableActions(b.status, false, true)
                .filter((a): a is 'acknowledge' | 'start' => a === 'acknowledge' || a === 'start');
              return (
                <tr key={b.id} className={cn('border-b border-slate-100', b.date < today && 'bg-amber-50/40')}>
                  <td className={cn(tdCls, 'font-medium')}>
                    {b.date}
                    {b.date < today && (
                      <span className="block text-[11px] font-normal text-amber-700">{t('myWork.past')}</span>
                    )}
                  </td>
                  <td className={tdCls}>{b.projectName}</td>
                  <td className={tdCls}>{b.customerName}</td>
                  <td className={tdCls}>{b.workPackageName ?? '—'}</td>
                  <td className={tdCls}>{t('common.hoursValue', { value: b.plannedHours })}</td>
                  <td className={tdCls}>
                    <span className={cn('rounded px-1.5 py-0.5 text-xs', statusClasses(b.status))}>
                      {t(`booking.status.${b.status}`)}
                    </span>
                  </td>
                  <td className={tdCls}>
                    <span className="flex gap-1">
                      {actions.map((a) => (
                        <button key={a} className={btnSecondaryCls} onClick={() => void run(b, a)}>
                          {t(`booking.action.${a}`)}
                        </button>
                      ))}
                    </span>
                  </td>
                </tr>
              );
            })}
            {mine.length === 0 && (
              <tr><td className={tdCls} colSpan={7}>{t('myWork.noBookings')}</td></tr>
            )}
          </tbody>
        </table>
      </Panel>

      {me?.role === 'Resource' && <WeekReport bookings={mine} />}
    </div>
  );
}
