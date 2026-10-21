// Kund/slutkund — one organisation register for both roles. A contract has a
// party (signs and pays MICROC) and optionally an end client (whom the work
// is ultimately for, when an intermediary holds the contract). No end client
// = direct contract.

import type { ContractRequestBody } from './types';

/** End-client choices for a contract: every organisation except the party
 * itself (picking the party would just mean a direct contract). */
export function endClientOptions<T extends { id: string; name: string }>(
  organisations: T[], partyId: string,
): T[] {
  return organisations
    .filter((o) => o.id !== partyId)
    .sort((a, b) => a.name.localeCompare(b.name));
}

export type ContractPartiesForm = {
  customerId: string;
  /** '' = direct contract. */
  endClientId: string;
  endClientReference: string;
};

/** Party fields of the contract request: a direct contract (no end client,
 * or the party picked as its own end client) sends no end client and no
 * end-client reference — mirroring how the server stores it. */
export function contractPartiesBody(
  f: ContractPartiesForm,
): Pick<ContractRequestBody, 'customerId' | 'endClientId' | 'endClientReference'> {
  const direct = !f.endClientId || f.endClientId === f.customerId;
  return {
    customerId: f.customerId,
    endClientId: direct ? null : f.endClientId,
    endClientReference: direct ? null : f.endClientReference.trim() || null,
  };
}

/** "End client (ref)" line for specs and contract headers — null on a
 * direct contract. */
export function endClientLabel(
  endClientName: string | null | undefined, endClientReference: string | null | undefined,
): string | null {
  if (!endClientName) return null;
  return endClientReference ? `${endClientName} (${endClientReference})` : endClientName;
}

/** Distinct effective end clients in margin rows, sorted by name — the
 * filter's options. */
export function endClientsOf(
  rows: { endClientId: string; endClientName: string }[],
): { id: string; name: string }[] {
  const byId = new Map<string, string>();
  for (const r of rows) byId.set(r.endClientId, r.endClientName);
  return [...byId].map(([id, name]) => ({ id, name }))
    .sort((a, b) => a.name.localeCompare(b.name));
}
