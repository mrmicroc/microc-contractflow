import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { bookingAction, getBookings, getFeedUrl, rotateFeedUrl } from '@/lib/api';
import { availableActions, statusClasses } from '@/lib/bookings';
import { useMe } from '@/lib/useMe';
import type { Booking } from '@/lib/types';
import { cn } from '@/lib/utils';
import { btnSecondaryCls, Panel, tdCls, thCls } from '@/components/forms';
import { WeekReport } from '@/components/reporting/WeekReport';

const todayIso = () => new Date().toISOString().slice(0, 10);

/** The resource's own assignments — acknowledge, start, complete (design §2 My Work). */
export function MyWork() {
  const { t } = useTranslation();
  const me = useMe();
  const [bookings, setBookings] = useState<Booking[]>([]);
  const [feedUrl, setFeedUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(() => {
    const from = todayIso();
    const to = new Date(Date.now() + 60 * 86400_000).toISOString().slice(0, 10);
    getBookings({ from, to }).then(setBookings).catch(() => setBookings([]));
  }, []);
  useEffect(() => { void reload(); }, [reload]);
  useEffect(() => {
    getFeedUrl().then((r) => setFeedUrl(r.url)).catch(() => {});
  }, []);

  const run = async (b: Booking, action: 'acknowledge' | 'start' | 'complete') => {
    setError(null);
    try { await bookingAction(b.id, action); reload(); }
    catch (e) { setError(e instanceof Error ? e.message : String(e)); }
  };

  const upcoming = bookings.filter((b) => !['completed', 'cancelled', 'rescheduled'].includes(b.status));

  return (
    <div className="mx-auto max-w-4xl space-y-4 px-4 py-6">
      <Panel title={t('myWork.title')}>
        {error && <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{error}</p>}
        {feedUrl && (
          <p className="mb-3 flex items-center gap-2 text-xs text-slate-600">
            {t('myWork.calendarFeed')}
            <code className="max-w-96 truncate rounded bg-slate-100 px-1.5 py-0.5">
              {location.origin}{feedUrl}
            </code>
            <button className={btnSecondaryCls}
              onClick={() => void rotateFeedUrl().then((r) => setFeedUrl(r.url))}>
              {t('myWork.regenerateFeed')}
            </button>
          </p>
        )}
        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b border-slate-200">
              <th className={thCls}>{t('booking.date')}</th>
              <th className={thCls}>{t('booking.project')}</th>
              <th className={thCls}>{t('booking.customer')}</th>
              <th className={thCls}>{t('booking.plannedHours')}</th>
              <th className={thCls}>{t('booking.statusLabel')}</th>
              <th className={thCls}>{t('common.actions')}</th>
            </tr>
          </thead>
          <tbody>
            {upcoming.map((b) => {
              const actions = availableActions(b.status, false, true);
              return (
                <tr key={b.id} className="border-b border-slate-100">
                  <td className={cn(tdCls, 'font-medium')}>{b.date}</td>
                  <td className={tdCls}>{b.projectName}</td>
                  <td className={tdCls}>{b.customerName}</td>
                  <td className={tdCls}>{b.plannedHours} h</td>
                  <td className={tdCls}>
                    <span className={cn('rounded px-1.5 py-0.5 text-xs', statusClasses(b.status))}>
                      {t(`booking.status.${b.status}`)}
                    </span>
                  </td>
                  <td className={tdCls}>
                    <span className="flex gap-1">
                      {actions.filter((a) => ['acknowledge', 'start', 'complete'].includes(a)).map((a) => (
                        <button key={a} className={btnSecondaryCls}
                          onClick={() => void run(b, a as 'acknowledge' | 'start' | 'complete')}>
                          {t(`booking.action.${a}`)}
                        </button>
                      ))}
                    </span>
                  </td>
                </tr>
              );
            })}
            {upcoming.length === 0 && (
              <tr><td className={tdCls} colSpan={6}>{t('myWork.noBookings')}</td></tr>
            )}
          </tbody>
        </table>
      </Panel>

      {me?.role === 'Resource' && <WeekReport bookings={bookings} />}
    </div>
  );
}
