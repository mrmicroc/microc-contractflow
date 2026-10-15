/**
 * Dev-mode user selection. Until real sign-in exists, the client stores the id
 * of the seeded user to act as; the api helper sends it as X-Dev-User-Id.
 */
export const DEV_USER_KEY = 'contractflow.devUserId';
export const DEV_USER_HEADER = 'X-Dev-User-Id';

interface StorageLike {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

const defaultStorage = (): StorageLike | undefined =>
  typeof localStorage === 'undefined' ? undefined : localStorage;

export function getDevUserId(storage: StorageLike | undefined = defaultStorage()): string | null {
  return storage?.getItem(DEV_USER_KEY) ?? null;
}

export function setDevUserId(id: string | null, storage: StorageLike | undefined = defaultStorage()): void {
  if (id == null) storage?.removeItem(DEV_USER_KEY);
  else storage?.setItem(DEV_USER_KEY, id);
}
