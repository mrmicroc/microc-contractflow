import { describe, expect, it } from 'vitest';
import { en } from './en';
import { sv } from './sv';

// Every static t('key') in the UI must resolve to a string in both languages —
// a typo'd or object-valued key renders raw text or "[object Object]".

const sources = import.meta.glob(
  ['../**/*.{ts,tsx}', '!../**/*.test.{ts,tsx}', '!../i18n/**'],
  { query: '?raw', import: 'default', eager: true },
) as Record<string, string>;

function lookup(dict: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>(
    (node, part) => (node && typeof node === 'object' ? (node as Record<string, unknown>)[part] : undefined),
    dict);
}

const usedKeys = [...new Set(Object.values(sources).flatMap((text) =>
  [...text.matchAll(/\bt\(\s*'([A-Za-z0-9_.]+)'/g)].map((m) => m[1])))];

describe('i18n keys', () => {
  it('finds the UI keys to check', () => {
    expect(usedKeys.length).toBeGreaterThan(200);
  });

  it.each([['en', en], ['sv', sv]] as const)('every static key resolves to a string in %s', (_, dict) => {
    const missing = usedKeys.filter((k) => typeof lookup(dict, k) !== 'string');
    expect(missing).toEqual([]);
  });
});
