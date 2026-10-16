// Pure helpers behind the searchable SearchSelect combobox — kept in lib/ so
// Vitest covers the filtering rules (AGENTS.md testing discipline).

export interface SearchOption {
  value: string;
  label: string;
}

/**
 * Filters options by a case-insensitive substring match on the label AND the
 * raw code — so typing "swe", "krona" or "sek" all reach "Swedish Krona (SEK)".
 */
export function filterOptions(options: SearchOption[], query: string): SearchOption[] {
  const q = query.trim().toLowerCase();
  if (!q) return options;
  return options.filter(
    (o) => o.label.toLowerCase().includes(q) || o.value.toLowerCase().includes(q),
  );
}
