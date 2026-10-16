// Form-field validation helpers — pure logic kept in lib/ so Vitest covers
// the rules (AGENTS.md testing discipline). The acceptance rule: what the
// user typed must match what the field expects; a non-empty invalid value
// blocks the save instead of silently degrading to empty/default.

/**
 * Strict decimal parse for free-text numeric fields (rates, hours, amounts).
 * Empty/whitespace → ok with null (field unset). Otherwise requires plain
 * digits with optional decimal separator (both ',' and '.' accepted) —
 * rejects letters, 'e5', '0x10', 'Infinity', 'NaN' and trailing junk that
 * Number() or numOrNull would silently swallow.
 */
export function parseDecimal(raw: string): { ok: true; value: number | null } | { ok: false } {
  const s = raw.trim();
  if (s === '') return { ok: true, value: null };
  const normalized = s.replace(',', '.');
  if (!/^-?\d+(\.\d+)?$/.test(normalized)) return { ok: false };
  const n = Number(normalized);
  return Number.isFinite(n) ? { ok: true, value: n } : { ok: false };
}

/** True when the field currently holds a non-empty, non-decimal value —
 * drives the live red border on NumInput. */
export function isInvalidDecimal(raw: string): boolean {
  return !parseDecimal(raw).ok;
}

/** Whole-number fields (days counts, thresholds) — decimals rejected. */
export function isInvalidInteger(raw: string): boolean {
  const p = parseDecimal(raw);
  return !p.ok || (p.value !== null && !Number.isInteger(p.value));
}

/**
 * Collects the labels of every invalid numeric field, so the caller can
 * block the save and tell the user exactly which fields to fix.
 * `fields`: label → raw string value. `intFields`: same, for whole numbers.
 */
export function invalidNumericFields(
  fields: Record<string, string>,
  intFields: Record<string, string> = {},
): string[] {
  return [
    ...Object.entries(fields).filter(([, v]) => isInvalidDecimal(v)).map(([k]) => k),
    ...Object.entries(intFields).filter(([, v]) => isInvalidInteger(v)).map(([k]) => k),
  ];
}
