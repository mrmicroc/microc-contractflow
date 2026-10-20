// One confirmation helper for every destructive or irreversible action —
// callers pass an already-translated message. The dialog function is
// swappable so tests (and a future styled modal) don't depend on
// window.confirm.

type ConfirmFn = (message: string) => boolean;

let impl: ConfirmFn = (message) =>
  typeof window === 'undefined' ? true : window.confirm(message);

/** True when the user confirms `message`. */
export function confirmAction(message: string): boolean {
  return impl(message);
}

/** Replaces the dialog (tests); returns a restore function. */
export function setConfirmImpl(fn: ConfirmFn): () => void {
  const prev = impl;
  impl = fn;
  return () => { impl = prev; };
}

/** Runs `action` only when confirmed; resolves false when declined. */
export async function confirmed<T>(message: string, action: () => Promise<T>): Promise<T | false> {
  if (!confirmAction(message)) return false;
  return action();
}
