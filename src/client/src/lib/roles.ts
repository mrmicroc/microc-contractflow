// Role helpers — pure mirrors of the server's RequireRole guards so the UI
// only offers what the API will allow. The server stays the authority.

export type AppRole =
  | 'Administrator' | 'ProjectManager' | 'Resource' | 'Finance' | 'CustomerReviewer';

interface HasRole { role: string }

const roleIn = (me: HasRole | null | undefined, roles: AppRole[]) =>
  !!me && (roles as string[]).includes(me.role);

export const isAdmin = (me: HasRole | null | undefined) => roleIn(me, ['Administrator']);
/** Admin/PM — plan, confirm, review timesheets, manage bookings. */
export const canManage = (me: HasRole | null | undefined) =>
  roleIn(me, ['Administrator', 'ProjectManager']);
export const canSeeCommercial = (me: HasRole | null | undefined) =>
  roleIn(me, ['Administrator', 'ProjectManager', 'Finance']);
export const canSeeAgreements = (me: HasRole | null | undefined) =>
  roleIn(me, ['Administrator', 'Finance']);
/** Admin/Finance — generate and move invoice specs / statements. */
export const canHandleMoney = (me: HasRole | null | undefined) =>
  roleIn(me, ['Administrator', 'Finance']);

export interface NavItem { to: string; key: string; roles: AppRole[] | 'all' }

export const NAV_ITEMS: NavItem[] = [
  { to: '/', key: 'nav.dashboard', roles: 'all' },
  { to: '/plan', key: 'nav.plan', roles: ['Administrator', 'ProjectManager', 'Finance'] },
  { to: '/projects', key: 'nav.projects', roles: ['Administrator', 'ProjectManager', 'Finance'] },
  { to: '/resources', key: 'nav.resources', roles: ['Administrator', 'ProjectManager', 'Finance'] },
  { to: '/my', key: 'nav.myWork', roles: ['Administrator', 'ProjectManager', 'Resource'] },
  { to: '/approvals', key: 'nav.approvals', roles: ['Administrator', 'ProjectManager'] },
  { to: '/month-end', key: 'nav.monthEnd', roles: ['Administrator', 'ProjectManager', 'Finance'] },
  { to: '/economy', key: 'nav.economy', roles: ['Administrator', 'ProjectManager', 'Finance', 'Resource'] },
  { to: '/reports', key: 'nav.reports', roles: ['Administrator', 'ProjectManager', 'Finance'] },
  { to: '/settings', key: 'nav.settings', roles: ['Administrator'] },
];

/** Menu items for a role. Unknown/unresolved user → dashboard only (no
 * flash of menus the caller can't use while /api/me loads). */
export function visibleNav(role: string | null | undefined): NavItem[] {
  return NAV_ITEMS.filter((i) =>
    i.roles === 'all' || (!!role && (i.roles as string[]).includes(role)));
}
