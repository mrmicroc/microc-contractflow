import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { getCountries } from '@/lib/api';
import { countryName } from '@/lib/countries';
import { filterOptions, type SearchOption } from '@/lib/options';
import { cn } from '@/lib/utils';
import type { MetaEntry } from '@/lib/types';

export const inputCls =
  'w-full rounded border border-slate-300 bg-white px-2 py-1 text-sm text-slate-800';
export const btnCls =
  'rounded bg-blue-600 px-3 py-1 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50';
export const btnSecondaryCls =
  'rounded border border-slate-300 px-3 py-1 text-sm text-slate-600 hover:bg-slate-100';
export const btnDangerCls =
  'rounded border border-red-200 px-2 py-0.5 text-xs text-red-600 hover:bg-red-50';
export const thCls = 'px-3 py-1.5 text-left text-xs font-semibold text-slate-500';
export const tdCls = 'px-3 py-1.5 text-sm text-slate-700';

export function Field({ label, help, children }: {
  label: string;
  help?: string;
  children: ReactNode;
}) {
  return (
    <label className="block">
      <span className="mb-0.5 block text-xs font-medium text-slate-500">
        {label}
        {help && (
          <span
            title={help}
            className="ml-1 inline-flex h-3.5 w-3.5 cursor-help items-center justify-center rounded-full border border-slate-300 align-middle text-[9px] font-semibold text-slate-400"
          >?</span>
        )}
      </span>
      {children}
    </label>
  );
}

/**
 * Searchable combobox — type to filter by label or code, arrows + Enter pick,
 * Esc/outside-click closes. Used for long lists: countries, currencies,
 * holiday calendars. `emptyLabel` adds an explicit "no value" row on top.
 */
export function SearchSelect({ value, options, onChange, emptyLabel }: {
  value: string;
  options: SearchOption[];
  onChange: (value: string) => void;
  emptyLabel?: string;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [hi, setHi] = useState(0);
  const ref = useRef<HTMLDivElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const selected = options.find((o) => o.value === value);
  const filtered = filterOptions(options, query);

  const pick = (v: string) => {
    onChange(v);
    setOpen(false);
    setQuery('');
  };

  const move = (delta: number) => {
    setHi((h) => {
      const next = Math.max(0, Math.min(h + delta, filtered.length - 1));
      listRef.current?.children[emptyLabel !== undefined ? next + 1 : next]
        ?.scrollIntoView({ block: 'nearest' });
      return next;
    });
  };

  return (
    <div
      ref={ref}
      className="relative"
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node)) setOpen(false);
      }}
    >
      <input
        className={cn(inputCls, 'pr-6')}
        value={open ? query : (selected?.label ?? value)}
        placeholder={selected?.label || t('common.search')}
        onFocus={() => { setOpen(true); setQuery(''); setHi(0); }}
        onChange={(e) => { setQuery(e.target.value); setOpen(true); setHi(0); }}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown') { e.preventDefault(); setOpen(true); move(1); }
          else if (e.key === 'ArrowUp') { e.preventDefault(); move(-1); }
          else if (e.key === 'Enter' && open) {
            e.preventDefault();
            const o = filtered[hi];
            if (o) pick(o.value);
          } else if (e.key === 'Escape') { setOpen(false); setQuery(''); }
        }}
      />
      <span className="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 text-[10px] text-slate-400">▾</span>
      {open && (
        <ul ref={listRef}
          className="absolute z-20 mt-0.5 max-h-56 w-full overflow-auto rounded border border-slate-300 bg-white py-0.5 shadow-lg">
          {emptyLabel !== undefined && (
            <li>
              <button type="button"
                onMouseDown={(e) => { e.preventDefault(); pick(''); }}
                className="block w-full px-2 py-1 text-left text-sm italic text-slate-400 hover:bg-blue-50">
                {emptyLabel}
              </button>
            </li>
          )}
          {filtered.map((o, i) => (
            <li key={o.value}>
              <button type="button"
                onMouseDown={(e) => { e.preventDefault(); pick(o.value); }}
                onMouseEnter={() => setHi(i)}
                className={cn(
                  'block w-full px-2 py-1 text-left text-sm',
                  i === hi ? 'bg-blue-50 text-slate-900' : 'text-slate-700',
                  o.value === value && 'font-medium',
                )}>
                {o.label}
              </button>
            </li>
          ))}
          {filtered.length === 0 && (
            <li className="px-2 py-1 text-xs italic text-slate-400">{t('common.noMatches')}</li>
          )}
        </ul>
      )}
    </div>
  );
}

/** Country picker — every ISO 3166 region from /api/meta/countries, localized
 * names via Intl.DisplayNames, searchable. Empty row clears the value. */
export function CountrySelect({ value, onChange }: {
  value: string;
  onChange: (code: string) => void;
}) {
  const { i18n, t } = useTranslation();
  const [countries, setCountries] = useState<MetaEntry[]>([]);
  useEffect(() => {
    let alive = true;
    getCountries().then((c) => { if (alive) setCountries(c); }).catch(() => {});
    return () => { alive = false; };
  }, []);
  const options = countries.map((c) => ({
    value: c.code,
    label: `${countryName(c.code, i18n.language)} (${c.code})`,
  }));
  return (
    <SearchSelect value={value} options={options} onChange={onChange}
      emptyLabel={t('common.none')} />
  );
}

export function Panel({ title, help, children, actions }: {
  title: string;
  help?: string;
  children: ReactNode;
  actions?: ReactNode;
}) {
  const [helpOpen, setHelpOpen] = useState(false);
  return (
    <section className="rounded-lg border border-slate-200 bg-white">
      <header className="flex items-center justify-between border-b border-slate-100 px-4 py-2">
        <h2 className="text-sm font-semibold text-slate-700">
          {title}
          {help && (
            <button
              type="button"
              title={help}
              aria-label="Info"
              onClick={() => setHelpOpen((v) => !v)}
              className="ml-2 inline-flex h-4 w-4 items-center justify-center rounded-full border border-slate-300 align-middle text-[10px] font-semibold text-slate-400 hover:border-blue-400 hover:text-blue-600"
            >?</button>
          )}
        </h2>
        {actions}
      </header>
      {helpOpen && help && (
        <p className="border-b border-slate-100 bg-blue-50/40 px-4 py-2 text-xs text-slate-600">{help}</p>
      )}
      <div className="p-4">{children}</div>
    </section>
  );
}
