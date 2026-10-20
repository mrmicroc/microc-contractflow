import { describe, expect, it } from 'vitest';
import {
  canHandleMoney, canManage, canSeeAgreements, canSeeCommercial, isAdmin, visibleNav,
} from './roles';

const as = (role: string) => ({ role });
const paths = (role: string | null) => visibleNav(role).map((i) => i.to);

describe('visibleNav', () => {
  it('administrator sees every section', () => {
    expect(paths('Administrator')).toEqual([
      '/', '/plan', '/projects', '/resources', '/my', '/approvals', '/month-end',
      '/economy', '/reports', '/settings',
    ]);
  });

  it('project manager: planning + approvals, no settings', () => {
    const p = paths('ProjectManager');
    expect(p).toContain('/approvals');
    expect(p).toContain('/plan');
    expect(p).not.toContain('/settings');
  });

  it('resource: dashboard, own work and own economy only', () => {
    expect(paths('Resource')).toEqual(['/', '/my', '/economy']);
  });

  it('finance: commercial read + economy, no approvals or own work', () => {
    const p = paths('Finance');
    expect(p).toContain('/economy');
    expect(p).toContain('/month-end');
    expect(p).not.toContain('/approvals');
    expect(p).not.toContain('/my');
    expect(p).not.toContain('/settings');
  });

  it('unknown or unresolved users only get the dashboard', () => {
    expect(paths(null)).toEqual(['/']);
    expect(paths('CustomerReviewer')).toEqual(['/']);
  });
});

describe('role predicates (allowed + forbidden)', () => {
  it('canManage: admin/PM yes; resource/finance/null no', () => {
    expect(canManage(as('Administrator'))).toBe(true);
    expect(canManage(as('ProjectManager'))).toBe(true);
    expect(canManage(as('Resource'))).toBe(false);
    expect(canManage(as('Finance'))).toBe(false);
    expect(canManage(null)).toBe(false);
  });

  it('canHandleMoney / canSeeAgreements: admin + finance only — never PM or resource', () => {
    for (const f of [canHandleMoney, canSeeAgreements]) {
      expect(f(as('Administrator'))).toBe(true);
      expect(f(as('Finance'))).toBe(true);
      expect(f(as('ProjectManager'))).toBe(false);
      expect(f(as('Resource'))).toBe(false);
    }
  });

  it('canSeeCommercial excludes resources; isAdmin only admins', () => {
    expect(canSeeCommercial(as('ProjectManager'))).toBe(true);
    expect(canSeeCommercial(as('Resource'))).toBe(false);
    expect(isAdmin(as('Administrator'))).toBe(true);
    expect(isAdmin(as('ProjectManager'))).toBe(false);
    expect(isAdmin(undefined)).toBe(false);
  });
});
