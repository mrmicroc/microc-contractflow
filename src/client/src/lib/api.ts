import { DEV_USER_HEADER, getDevUserId } from './currentUser';

function devHeaders(): HeadersInit {
  const id = getDevUserId();
  return id ? { [DEV_USER_HEADER]: id } : {};
}

export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(path, { headers: { ...devHeaders() } });
  if (!res.ok) throw new Error(await res.text());
  return (await res.json()) as T;
}

export interface UserDto {
  id: string;
  displayName: string;
  email: string | null;
  role: string;
}

export const getMe = () => apiGet<UserDto>('/api/me');
export const getUsers = () => apiGet<UserDto[]>('/api/users');
