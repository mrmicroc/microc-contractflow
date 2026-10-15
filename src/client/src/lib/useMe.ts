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

export const isAdmin = (me: UserDto | null) => me?.role === 'Administrator';
export const canSeeCommercial = (me: UserDto | null) =>
  me !== null && ['Administrator', 'ProjectManager', 'Finance'].includes(me.role);
export const canSeeAgreements = (me: UserDto | null) =>
  me !== null && ['Administrator', 'Finance'].includes(me.role);
