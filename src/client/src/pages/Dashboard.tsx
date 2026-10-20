import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import {
  getBookings, getInvoices, getPadRequests, getProjects, getStatements, getSupplierInvoices,
  getTimeEntries, getTimesheets,
} from '@/lib/api';
import { addDays } from '@/lib/bookings';
import { dashboardTiles, UPCOMING_DAYS, type DashboardData, type DashboardTile } from '@/lib/dashboard';
import { localTodayIso } from '@/lib/dates';
import { canHandleMoney, canManage, canSeeCommercial } from '@/lib/roles';
import { useMe } from '@/lib/useMe';
import { cn } from '@/lib/utils';
import type { UserDto } from '@/lib/api';

/** Look-back for unfinished/own work on the dashboard. */
const PAST_DAYS = 30;

/** Fetches what the caller's role needs; failures leave that list empty —
 * a dashboard tile must never take the whole page down. */
async function loadDashboard(me: UserDto): Promise<DashboardData> {
  const today = localTodayIso();
  const safe = <T,>(p: Promise<T[]>) => p.catch(() => [] as T[]);
  const data: DashboardData = { today };
  const range = { from: addDays(today, -PAST_DAYS), to: addDays(today, UPCOMING_DAYS + 60) };

  if (me.role === 'Resource') {
    const [bookings, timesheets, entries] = await Promise.all([
      safe(getBookings(range)), safe(getTimesheets()),
      safe(getTimeEntries({ from: addDays(today, -PAST_DAYS), to: today })),
    ]);
    return { ...data, bookings, timesheets, draftRows: entries.filter((e) => !e.timesheetId).length };
  }

  const jobs: Promise<void>[] = [];
  if (canManage(me)) {
    jobs.push(safe(getBookings({ from: today, to: addDays(today, UPCOMING_DAYS) }))
      .then((b) => { data.bookings = b; }));
    jobs.push(safe(getTimesheets({ status: 'submitted' })).then((s) => { data.timesheets = s; }));
    jobs.push(safe(getProjects()).then((ps) => Promise.all(ps.filter((p) => p.isActive)
      .map((p) => safe(getPadRequests(p.id))))).then((rs) => { data.padRequests = rs.flat(); }));
  }
  if (canSeeCommercial(me)) jobs.push(safe(getInvoices()).then((i) => { data.invoices = i; }));
  if (canHandleMoney(me)) {
    jobs.push(safe(getStatements()).then((s) => { data.statements = s; }));
    jobs.push(safe(getSupplierInvoices()).then((s) => { data.supplierInvoices = s; }));
  }
  await Promise.all(jobs);
  return data;
}

export function Dashboard() {
  const { t } = useTranslation();
  const me = useMe();
  const [tiles, setTiles] = useState<DashboardTile[] | null>(null);

  const reload = useCallback(() => {
    if (!me) return;
    loadDashboard(me).then((d) => setTiles(dashboardTiles(me.role, d))).catch(() => setTiles([]));
  }, [me]);
  // `me` re-resolves on a dev-user switch, which re-runs this effect.
  useEffect(() => { void Promise.resolve().then(reload); }, [reload]);

  return (
    <div className="mx-auto max-w-4xl px-6 py-8">
      <h1 className="text-2xl font-semibold text-slate-800">{t('dashboard.welcome')}</h1>
      {me && (
        <p className="mt-1 text-sm text-slate-600">
          {t('dashboard.signedInAs', { name: me.displayName, role: t(`roles.${me.role}`) })}
        </p>
      )}
      {tiles === null && me && <p className="mt-6 text-sm text-slate-400">{t('common.loading')}</p>}
      {tiles && tiles.length > 0 && (
        <div className="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-3">
          {tiles.map((tile) => (
            <Link key={tile.key} to={tile.to}
              className={cn('rounded-lg border bg-white p-4 shadow-sm hover:border-blue-300 hover:shadow',
                tile.tone === 'attention' ? 'border-amber-300' : 'border-slate-200')}>
              <div className={cn('text-3xl font-semibold',
                tile.tone === 'attention' ? 'text-amber-700'
                : tile.tone === 'ok' ? 'text-slate-400' : 'text-slate-800')}>
                {tile.count}
              </div>
              <div className="mt-1 text-sm text-slate-600">{t(`dashboard.tile.${tile.key}`)}</div>
            </Link>
          ))}
        </div>
      )}
      {tiles && tiles.length === 0 && (
        <p className="mt-6 text-sm text-slate-500">{t('dashboard.nothing')}</p>
      )}
    </div>
  );
}
