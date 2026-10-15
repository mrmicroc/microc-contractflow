import { useTranslation } from 'react-i18next';

export function Placeholder({ titleKey }: { titleKey: string }) {
  const { t } = useTranslation();
  return (
    <div className="mx-auto max-w-3xl px-6 py-16 text-center">
      <h1 className="text-2xl font-semibold text-slate-800">{t(titleKey)}</h1>
      <p className="mt-2 text-sm text-slate-500">
        {t('placeholder.title')} — {t('placeholder.body')}
      </p>
    </div>
  );
}
