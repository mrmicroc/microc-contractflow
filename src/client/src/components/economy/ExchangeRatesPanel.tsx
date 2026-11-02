import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createExchangeRate, deleteExchangeRate, describeError, getExchangeRates, updateExchangeRate,
} from '@/lib/api';
import { confirmAction } from '@/lib/confirm';
import { localMonth } from '@/lib/dates';
import { rateFormToBody, type RateForm } from '@/lib/currency';
import type { ExchangeRate } from '@/lib/types';
import {
  btnCls, btnDangerCls, btnSecondaryCls, ErrorBox, inputCls, Panel, tdCls, thCls,
} from '@/components/forms';
import { cn } from '@/lib/utils';

const emptyForm = (year: number, month: number): RateForm => ({
  year: String(year), month: String(month), fromCurrency: '', toCurrency: 'SEK', rate: '', note: '',
});

/** Month rate table (Admin/Finance): one rate per currency pair and month. */
export function ExchangeRatesPanel() {
  const { t } = useTranslation();
  const start = localMonth();
  const [year, setYear] = useState(start.year);
  const [month, setMonth] = useState(start.month);
  const [rows, setRows] = useState<ExchangeRate[]>([]);
  const [form, setForm] = useState<RateForm>(emptyForm(start.year, start.month));
  const [editing, setEditing] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [version, setVersion] = useState(0);
  const reload = () => setVersion((v) => v + 1);

  useEffect(() => {
    let alive = true;
    getExchangeRates(year, month)
      .then((r) => { if (alive) setRows(r); })
      .catch((e) => { if (alive) setError(describeError(e, t)); });
    return () => { alive = false; };
  }, [year, month, version, t]);

  /** Switching month starts a fresh form for that month. */
  const pickMonth = (y: number, m: number) => {
    setYear(y);
    setMonth(m);
    setForm(emptyForm(y, m));
    setEditing(null);
  };

  const save = async () => {
    setError(null);
    const { body, error: invalid } = rateFormToBody(form);
    if (!body) { setError(t(invalid!)); return; }
    try {
      if (editing) await updateExchangeRate(editing, body);
      else await createExchangeRate(body);
      setForm(emptyForm(year, month));
      setEditing(null);
      reload();
    } catch (e) {
      setError(describeError(e, t));
    }
  };

  const remove = async (r: ExchangeRate) => {
    if (!confirmAction(t('common.confirmDelete'))) return;
    try {
      await deleteExchangeRate(r.id);
      reload();
    } catch (e) {
      setError(describeError(e, t));
    }
  };

  return (
    <div id="exchange-rates">
      <Panel title={t('fx.title')} help={t('fx.help')}>
        <div className="space-y-3 p-4">
          <div className="flex items-center gap-2 text-sm">
            <input type="number" className={cn(inputCls, 'w-24')} value={year}
              onChange={(e) => pickMonth(Number(e.target.value), month)} aria-label={t('economy.month')} />
            <select className={cn(inputCls, 'w-24')} value={month}
              onChange={(e) => pickMonth(year, Number(e.target.value))} aria-label={t('economy.month')}>
              {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                <option key={m} value={m}>{String(m).padStart(2, '0')}</option>
              ))}
            </select>
          </div>
          {error && <ErrorBox message={error} />}
          <table className="w-full border-collapse">
            <thead>
              <tr className="border-b border-slate-200">
                <th className={thCls}>{t('fx.from')}</th>
                <th className={thCls}>{t('fx.to')}</th>
                <th className={cn(thCls, 'text-right')}>{t('fx.rate')}</th>
                <th className={thCls}>{t('fx.note')}</th>
                <th className={thCls} />
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id} className="border-b border-slate-100">
                  <td className={tdCls}>{r.fromCurrency}</td>
                  <td className={tdCls}>{r.toCurrency}</td>
                  <td className={cn(tdCls, 'text-right')}>{r.rate}</td>
                  <td className={cn(tdCls, 'text-xs text-slate-500')}>{r.note ?? ''}</td>
                  <td className={cn(tdCls, 'text-right')}>
                    {r.inUse ? (
                      <span className="text-xs text-slate-400">{t('fx.inUse')}</span>
                    ) : (
                      <span className="flex justify-end gap-1">
                        <button className={btnSecondaryCls} onClick={() => {
                          setEditing(r.id);
                          setForm({
                            year: String(r.year), month: String(r.month), fromCurrency: r.fromCurrency,
                            toCurrency: r.toCurrency, rate: String(r.rate), note: r.note ?? '',
                          });
                        }}>{t('common.edit')}</button>
                        <button className={btnDangerCls} onClick={() => remove(r)}>{t('common.delete')}</button>
                      </span>
                    )}
                  </td>
                </tr>
              ))}
              {rows.length === 0 && (
                <tr><td className={cn(tdCls, 'text-slate-400')} colSpan={5}>{t('fx.none')}</td></tr>
              )}
            </tbody>
          </table>
          <div className="flex flex-wrap items-end gap-2 text-sm">
            <input className={cn(inputCls, 'w-16 uppercase')} maxLength={3} placeholder={t('fx.from')}
              value={form.fromCurrency} onChange={(e) => setForm({ ...form, fromCurrency: e.target.value })} />
            <span className="text-slate-400">→</span>
            <input className={cn(inputCls, 'w-16 uppercase')} maxLength={3} placeholder={t('fx.to')}
              value={form.toCurrency} onChange={(e) => setForm({ ...form, toCurrency: e.target.value })} />
            <input className={cn(inputCls, 'w-28')} inputMode="decimal" placeholder={t('fx.rate')}
              title={t('fx.rateHelp')}
              value={form.rate} onChange={(e) => setForm({ ...form, rate: e.target.value })} />
            <input className={cn(inputCls, 'w-48')} placeholder={t('fx.note')}
              value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} />
            <button className={btnCls} onClick={save}>{editing ? t('common.save') : t('fx.add')}</button>
            {editing && (
              <button className={btnSecondaryCls} onClick={() => { setEditing(null); setForm(emptyForm(year, month)); }}>
                {t('common.cancel')}
              </button>
            )}
          </div>
          <p className="text-xs text-slate-500">{t('fx.rateHelp')}</p>
        </div>
      </Panel>
    </div>
  );
}
