import { describe, expect, it } from 'vitest';
import { auditActor, auditQuery, auditValue, changedFields, type AuditEntry } from './audit';

const entry = (fields: string[], user: string | null = 'Admin User'): AuditEntry => ({
  id: '1', occurredUtc: '2026-10-20T08:00:00Z', userId: user ? 'u' : null,
  userDisplayName: user, entityType: 'ContractTerms', entityId: 'e', action: 'updated',
  changes: fields.map((f) => ({ field: f, old: '1', new: '2' })), reason: null,
});

describe('auditQuery', () => {
  it('omits empty filters and always caps take', () => {
    const q = new URLSearchParams(auditQuery({ entityType: '', entityId: ' ', from: '', to: '', take: 200 }));
    expect([...q.keys()]).toEqual(['take']);
    expect(q.get('take')).toBe('200');
  });

  it('clamps take to the server range', () => {
    const big = new URLSearchParams(auditQuery({ entityType: '', entityId: '', from: '', to: '', take: 5000 }));
    expect(big.get('take')).toBe('1000');
    const zero = new URLSearchParams(auditQuery({ entityType: '', entityId: '', from: '', to: '', take: 0 }));
    expect(zero.get('take')).toBe('200');
  });

  it('sends local day bounds as instants, the "to" day inclusive', () => {
    const q = new URLSearchParams(auditQuery({
      entityType: 'Contract', entityId: '', from: '2026-10-01', to: '2026-10-20', take: 50,
    }));
    expect(q.get('entityType')).toBe('Contract');
    expect(new Date(q.get('from')!).getTime()).toBe(new Date('2026-10-01T00:00:00').getTime());
    expect(new Date(q.get('to')!).getTime()).toBe(new Date('2026-10-20T23:59:59.999').getTime());
  });
});

describe('display helpers', () => {
  it('shows null and empty values as a dash', () => {
    expect(auditValue(null)).toBe('—');
    expect(auditValue('')).toBe('—');
    expect(auditValue('0')).toBe('0');
  });

  it('summarises changed fields with an overflow count', () => {
    expect(changedFields(entry(['HourlyRate']))).toBe('HourlyRate');
    expect(changedFields(entry(['A', 'B', 'C', 'D', 'E', 'F']))).toBe('A, B, C, D +2');
  });

  it('labels system writes', () => {
    expect(auditActor(entry([], null), 'system')).toBe('system');
    expect(auditActor(entry([]), 'system')).toBe('Admin User');
  });
});
