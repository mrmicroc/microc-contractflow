import { parseDecimal } from './validation';

/**
 * Effective-dated versioned records (contract terms, compensation agreements).
 * Dates are ISO yyyy-mm-dd strings — lexicographic comparison is chronological.
 */
export interface Versioned {
  version: number;
  effectiveFrom: string;
}

/**
 * The version in force on `onDate`: the one with the latest effectiveFrom
 * ≤ onDate (ties broken by higher version). Null when no version is in force —
 * e.g. all versions start in the future.
 */
export function resolveVersion<T extends Versioned>(versions: T[], onDate: string): T | null {
  let best: T | null = null;
  for (const v of versions) {
    if (v.effectiveFrom > onDate) continue;
    if (
      best === null ||
      v.effectiveFrom > best.effectiveFrom ||
      (v.effectiveFrom === best.effectiveFrom && v.version > best.version)
    ) {
      best = v;
    }
  }
  return best;
}

/** Parses a nullable decimal form field: empty/blank → null, else the number.
 * Accepts the Swedish decimal comma ("8,5" → 8.5) via parseDecimal; values
 * that aren't plain decimals give null (forms block those before saving). */
export function numOrNull(value: string): number | null {
  const p = parseDecimal(value);
  return p.ok ? p.value : null;
}

/** Like numOrNull but falls back to `fallback` only when the field is empty
 * or invalid — an explicit 0 survives (unlike `Number(x) || fallback`). */
export function numOr(value: string, fallback: number): number {
  return numOrNull(value) ?? fallback;
}
