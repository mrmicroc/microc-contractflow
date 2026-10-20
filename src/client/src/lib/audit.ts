// Audit log helpers (design §3/§9.3) — query building and display shaping
// for GET /api/audit. Pure; the Settings page renders the result.

export type AuditAction = 'created' | 'updated' | 'deleted';

export interface AuditChange { field: string; old: string | null; new: string | null }

export interface AuditEntry {
  id: string;
  occurredUtc: string;
  userId: string | null;
  userDisplayName: string | null;
  entityType: string;
  entityId: string;
  action: AuditAction;
  changes: AuditChange[];
  reason: string | null;
}

/** Entity types the server audits (its CLR type names) — the filter list. */
export const AUDITED_ENTITY_TYPES = [
  'Contract', 'ContractTerms', 'Customer', 'EndClient', 'Project', 'WorkPackage',
  'WorkPackageRate', 'Resource', 'CompensationAgreement', 'ProjectResource',
  'ProjectAccess', 'Booking', 'Timesheet', 'InvoiceSpecification',
  'SubcontractorStatement', 'SupplierInvoice', 'AdditionalPadRequest',
  'ProjectMonthClose', 'EstimatedQuantity',
] as const;

export const AUDIT_MAX_TAKE = 1000;

export interface AuditFilters {
  entityType: string;
  entityId: string;
  /** Local dates (yyyy-mm-dd); `to` is inclusive of the whole day. */
  from: string;
  to: string;
  take: number;
}

/** Query string for /api/audit. Dates go as UTC instants of the local day
 * bounds so "to 2026-10-20" includes that whole local day. */
export function auditQuery(f: AuditFilters): string {
  const p = new URLSearchParams();
  if (f.entityType) p.set('entityType', f.entityType);
  if (f.entityId.trim()) p.set('entityId', f.entityId.trim());
  if (f.from) p.set('from', new Date(`${f.from}T00:00:00`).toISOString());
  if (f.to) p.set('to', new Date(`${f.to}T23:59:59.999`).toISOString());
  const take = Math.min(Math.max(Math.trunc(f.take) || 200, 1), AUDIT_MAX_TAKE);
  p.set('take', String(take));
  return p.toString();
}

/** A stored value for display — null/empty become an em dash. */
export const auditValue = (v: string | null | undefined) =>
  v === null || v === undefined || v === '' ? '—' : v;

/** Short summary for the collapsed row: the changed field names. */
export function changedFields(e: AuditEntry, max = 4): string {
  const names = e.changes.map((c) => c.field);
  if (names.length <= max) return names.join(', ');
  return `${names.slice(0, max).join(', ')} +${names.length - max}`;
}

/** Who did it — the snapshot name, else "system" (seed/startup writes). */
export const auditActor = (e: AuditEntry, systemLabel: string) =>
  e.userDisplayName ?? systemLabel;
