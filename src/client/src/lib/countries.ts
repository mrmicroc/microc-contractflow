// Localized display names for ISO codes via Intl.DisplayNames — no hand-
// maintained translations. The underlying lists come from /api/meta/*, which
// enumerates every ISO code the runtime knows.

/** Localized country name for an ISO 3166-1 alpha-2 code — falls back to the
 * raw code. */
export function countryName(code: string | null | undefined, lang: string): string {
  if (!code) return '—';
  try {
    return new Intl.DisplayNames([lang], { type: 'region' }).of(code) ?? code;
  } catch {
    return code;
  }
}

/** Localized currency name for an ISO 4217 code — falls back to the code. */
export function currencyName(code: string | null | undefined, lang: string): string {
  if (!code) return '—';
  try {
    return new Intl.DisplayNames([lang], { type: 'currency' }).of(code) ?? code;
  } catch {
    return code;
  }
}

/**
 * Display label for a holiday-calendar code. Plain two-letter codes get the
 * localized country name; regional variants (GB-SCT…) localize the country
 * part and keep the backend's region suffix; NONE gets its own label.
 */
export function calendarLabel(
  code: string,
  backendName: string,
  lang: string,
  noneLabel: string,
): string {
  if (code === 'NONE') return noneLabel;
  if (code.includes('-')) {
    const sep = backendName.indexOf('—');
    const localized = countryName(code.split('-')[0], lang);
    return sep >= 0
      ? `${localized} — ${backendName.slice(sep + 1).trim()}`
      : `${localized} (${code})`;
  }
  return `${countryName(code, lang)} (${code})`;
}
