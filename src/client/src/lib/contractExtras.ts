// Contract-level extras — one-off scheduled charges, authorised
// representatives, the visit counter. Pure form ⇄ body mapping so the rules
// (comma decimals, blank rows dropped, frozen once invoiced) are unit-tested.
import { numOrNull } from './versioned';
import type {
  AuthorisedRepresentative, ContractVisits, ProjectSummary, ScheduledCharge,
  ScheduledChargeRequestBody,
} from './types';

export type ChargeForm = { date: string; amount: string; description: string; projectId: string };

export const emptyCharge: ChargeForm = { date: '', amount: '', description: '', projectId: '' };

export const chargeToForm = (c: ScheduledCharge): ChargeForm => ({
  date: c.date, amount: String(c.amount), description: c.description, projectId: c.projectId ?? '',
});

/** Body for create/update, or null while the form can't be saved (missing
 * date/text, or an amount that isn't a positive number). */
export function chargeBody(f: ChargeForm): ScheduledChargeRequestBody | null {
  const amount = numOrNull(f.amount);
  if (!f.date || !f.description.trim() || amount === null || amount <= 0) return null;
  return { date: f.date, amount, description: f.description.trim(), projectId: f.projectId || null };
}

/** Name of the project whose specification bills the charge — the chosen
 * one, else the server's default (first active project). */
export function billedProjectName(c: ScheduledCharge, projects: ProjectSummary[]): string | null {
  if (c.projectName) return c.projectName;
  const id = c.billedProjectId ?? c.projectId;
  return projects.find((p) => p.id === id)?.name ?? null;
}

/** A charge on a specification beyond draft is frozen. */
export const canChangeCharge = (c: ScheduledCharge) => !c.invoiced;

export type RepresentativeRow = { name: string; email: string; phone: string; authority: string };

export const repToRow = (r: AuthorisedRepresentative): RepresentativeRow => ({
  name: r.name, email: r.email ?? '', phone: r.phone ?? '', authority: r.authority ?? '',
});

/** Replace-all list for the contract update — rows without a name drop out. */
export function representativesBody(rows: RepresentativeRow[]): AuthorisedRepresentative[] {
  return rows
    .filter((r) => r.name.trim())
    .map((r) => ({
      name: r.name.trim(),
      email: r.email.trim() || null,
      phone: r.phone.trim() || null,
      authority: r.authority.trim() || null,
    }));
}

/** Display names for the "ordered by" picker. */
export const representativeNames = (reps: AuthorisedRepresentative[] | undefined) =>
  (reps ?? []).map((r) => r.name);

/** i18n key + params for the visit counter line. */
export function visitsSummary(v: ContractVisits): { key: string; params: Record<string, number> } {
  return v.included === null
    ? { key: 'visits.usedUnlimited', params: { used: v.used } }
    : {
      key: 'visits.used',
      params: { used: v.used, included: v.included, remaining: Math.max(0, v.remaining ?? v.included - v.used) },
    };
}
