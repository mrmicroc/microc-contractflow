import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getMe, type UserDto } from '@/lib/api';
import { DEV_USER_CHANGED } from '@/components/DevUserBar';

export function Dashboard() {
  const { t } = useTranslation();
  const [me, setMe] = useState<UserDto | null>(null);

  useEffect(() => {
    const load = () => getMe().then(setMe).catch(() => setMe(null));
    load();
    window.addEventListener(DEV_USER_CHANGED, load);
    return () => window.removeEventListener(DEV_USER_CHANGED, load);
  }, []);

  return (
    <div className="mx-auto max-w-3xl px-6 py-10">
      <h1 className="text-2xl font-semibold text-slate-800">{t('dashboard.welcome')}</h1>
      {me && (
        <p className="mt-2 text-sm text-slate-600">
          {t('dashboard.signedInAs', { name: me.displayName, role: me.role })}
        </p>
      )}
    </div>
  );
}
