import { useTranslation } from 'react-i18next';

export function LanguageSwitcher() {
  const { i18n, t } = useTranslation();

  return (
    <label className="flex items-center gap-1 text-xs text-slate-500">
      {t('language.label')}
      <select
        className="rounded border border-slate-300 bg-white px-1.5 py-0.5 text-slate-700"
        value={i18n.language}
        onChange={(e) => void i18n.changeLanguage(e.target.value)}
      >
        <option value="en">{t('language.en')}</option>
        <option value="sv">{t('language.sv')}</option>
      </select>
    </label>
  );
}
