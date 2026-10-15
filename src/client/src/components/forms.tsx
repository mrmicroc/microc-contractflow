import { useState, type ReactNode } from 'react';

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

export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-0.5 block text-xs font-medium text-slate-500">{label}</span>
      {children}
    </label>
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
