import { describe, expect, it } from 'vitest';
import { filterOptions, type SearchOption } from './options';

const opts: SearchOption[] = [
  { value: 'SE', label: 'Sweden (SE)' },
  { value: 'GB-SCT', label: 'United Kingdom — Scotland (GB-SCT)' },
  { value: 'SEK', label: 'Swedish Krona (SEK)' },
  { value: 'US', label: 'United States (US)' },
];

describe('filterOptions', () => {
  it('returns everything on an empty/blank query', () => {
    expect(filterOptions(opts, '')).toHaveLength(4);
    expect(filterOptions(opts, '   ')).toHaveLength(4);
  });

  it('matches on the label, case-insensitively', () => {
    expect(filterOptions(opts, 'swed').map((o) => o.value)).toEqual(['SE', 'SEK']);
    expect(filterOptions(opts, 'UNITED')).toHaveLength(2);
  });

  it('matches on the raw code too', () => {
    expect(filterOptions(opts, 'gb-sct')).toHaveLength(1);
    expect(filterOptions(opts, 'sek').map((o) => o.value)).toEqual(['SEK']);
  });

  it('gives an empty list when nothing matches', () => {
    expect(filterOptions(opts, 'zzz')).toEqual([]);
  });
});
