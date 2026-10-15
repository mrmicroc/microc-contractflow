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

/** Parses a nullable decimal form field: empty/blank → null, else the number. */
export function numOrNull(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed === '') return null;
  const n = Number(trimmed);
  return Number.isFinite(n) ? n : null;
}
