import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getUsers, type UserDto } from '@/lib/api';
import { getDevUserId, setDevUserId } from '@/lib/currentUser';

/** Fired on window when the dev user selection changes. */
export const DEV_USER_CHANGED = 'dev-user-changed';

export function DevUserBar() {
  const { t } = useTranslation();
  const [users, setUsers] = useState<UserDto[]>([]);
  const [selected, setSelected] = useState<string>(getDevUserId() ?? '');

  useEffect(() => {
    getUsers()
      .then((list) => {
        setUsers(list);
        if (!getDevUserId() && list.length > 0) {
          const admin = list.find((u) => u.role === 'Administrator') ?? list[0];
          setDevUserId(admin.id);
          setSelected(admin.id);
          window.dispatchEvent(new Event(DEV_USER_CHANGED));
        }
      })
      .catch(() => setUsers([]));
  }, []);

  return (
    <div className="flex items-center gap-2 text-xs text-slate-500">
      <span className="rounded bg-amber-100 px-1.5 py-0.5 font-medium text-amber-800">
        {t('devBar.devMode')}
      </span>
      <label className="flex items-center gap-1">
        {t('devBar.actingAs')}
        <select
          className="rounded border border-slate-300 bg-white px-1.5 py-0.5 text-slate-700"
          value={selected}
          onChange={(e) => {
            const id = e.target.value || null;
            setDevUserId(id);
            setSelected(e.target.value);
            window.dispatchEvent(new Event(DEV_USER_CHANGED));
          }}
        >
          {users.map((u) => (
            <option key={u.id} value={u.id}>
              {u.displayName} · {t(`roles.${u.role}`)}
            </option>
          ))}
        </select>
      </label>
    </div>
  );
}
