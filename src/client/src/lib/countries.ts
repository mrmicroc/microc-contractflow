// ISO 3166-1 alpha-2 country codes offered in dropdowns. Names render in the
// active UI language via Intl.DisplayNames — no hand-maintained translations.
export const COUNTRY_CODES = [
  'SE', 'NO', 'DK', 'FI', 'IS',
  'EE', 'LV', 'LT', 'PL', 'DE', 'NL', 'BE', 'FR', 'GB', 'IE',
  'AT', 'CH', 'IT', 'ES', 'PT', 'CZ', 'US', 'CA',
] as const;

/** Localized country name for a code — falls back to the raw code. */
export function countryName(code: string | null | undefined, lang: string): string {
  if (!code) return '—';
  try {
    return new Intl.DisplayNames([lang], { type: 'region' }).of(code) ?? code;
  } catch {
    return code;
  }
}
