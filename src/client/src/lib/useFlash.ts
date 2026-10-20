import { useEffect, useState } from 'react';

/** Transient success message — clears itself after `ms` so stale banners
 * never linger across later actions. */
export function useFlash(ms = 5000): [string | null, (msg: string | null) => void] {
  const [msg, setMsg] = useState<string | null>(null);
  useEffect(() => {
    if (!msg) return;
    const id = window.setTimeout(() => setMsg(null), ms);
    return () => window.clearTimeout(id);
  }, [msg, ms]);
  return [msg, setMsg];
}
