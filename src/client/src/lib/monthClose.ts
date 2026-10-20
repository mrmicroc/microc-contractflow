// Month-end close preview → view model (design §6.4). The server's dry run
// (GET …/close/preview, also attached to a 409 closeBlocked) is the
// authority; this only decides what the page offers.

import { errorBody } from './errors';
import type { CloseMonthResult } from './types';

export interface CloseCheckItem { code: string; count: number; blocking: boolean }
export interface CloseStep { code: string; amount: number; explanation: string }

export interface ClosePreview {
  year: number;
  month: number;
  canClose: boolean;
  orderingErrors: string[];
  checklist: CloseCheckItem[];
  steps: CloseStep[];
  result: CloseMonthResult;
}

export type CloseMode =
  /** Nothing in the way — a plain close. */
  | 'close'
  /** Only blocking checklist items remain — an admin may force with a reason. */
  | 'force'
  /** Ordering errors (or not an admin with blockers) — no close offered. */
  | 'none';

export interface CloseView {
  orderingErrors: string[];
  blocking: CloseCheckItem[];
  warnings: CloseCheckItem[];
  steps: CloseStep[];
  mode: CloseMode;
  /** True when the month is already closed (ordering code alreadyClosed). */
  alreadyClosed: boolean;
}

/** Splits the preview into what the page renders and which close it offers.
 * Ordering errors can never be forced; blocking items only by an admin. */
export function closeView(p: ClosePreview, isAdmin: boolean): CloseView {
  const blocking = p.checklist.filter((c) => c.blocking);
  const warnings = p.checklist.filter((c) => !c.blocking);
  const ordering = p.orderingErrors;
  let mode: CloseMode = 'none';
  if (isAdmin && ordering.length === 0) mode = blocking.length === 0 ? 'close' : 'force';
  return {
    orderingErrors: ordering,
    blocking,
    warnings,
    steps: p.steps,
    mode,
    alreadyClosed: ordering.includes('alreadyClosed'),
  };
}

/** A forced close needs a non-blank reason (the server answers 400 otherwise). */
export const canSubmitForce = (reason: string) => reason.trim().length > 0;

/** The preview attached to a 409 closeBlocked/monthClosed response, if any. */
export function blockedPreview(e: unknown): ClosePreview | null {
  const body = errorBody(e);
  const p = body?.preview;
  return p && typeof p === 'object' && Array.isArray((p as ClosePreview).checklist)
    ? (p as ClosePreview)
    : null;
}
