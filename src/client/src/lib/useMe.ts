import { useEffect, useState } from 'react';
import { getMe, type UserDto } from './api';
import { DEV_USER_CHANGED } from '@/components/DevUserBar';

/** The user the API currently treats requests as (dev role-switcher aware). */
export function useMe(): UserDto | null {
  const [me, setMe] = useState<UserDto | null>(null);

  useEffect(() => {
    const load = () => getMe().then(setMe).catch(() => setMe(null));
    load();
    window.addEventListener(DEV_USER_CHANGED, load);
    return () => window.removeEventListener(DEV_USER_CHANGED, load);
  }, []);

  return me;
}

// Role helpers live in lib/roles (pure, tested); re-exported for the pages.
export { canHandleMoney, canManage, canSeeAgreements, canSeeCommercial, isAdmin } from './roles';

/** Re-runs `reload` whenever the dev role-switcher changes the acting user —
 * pages must never keep showing the previous user's data. */
export function useDevUserReload(reload: () => void) {
  useEffect(() => {
    window.addEventListener(DEV_USER_CHANGED, reload);
    return () => window.removeEventListener(DEV_USER_CHANGED, reload);
  }, [reload]);
}
