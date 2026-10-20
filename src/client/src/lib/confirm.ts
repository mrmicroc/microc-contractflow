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

type PromptFn = (message: string) => string | null;

let promptImpl: PromptFn = (message) =>
  typeof window === 'undefined' ? null : window.prompt(message);

/** Asks for a required comment (reopen, discrepancy approval): the trimmed
 * text, or null when cancelled or left blank. */
export function promptNote(message: string): string | null {
  const v = promptImpl(message)?.trim();
  return v ? v : null;
}

/** Replaces the note prompt (tests); returns a restore function. */
export function setPromptImpl(fn: PromptFn): () => void {
  const prev = promptImpl;
  promptImpl = fn;
  return () => { promptImpl = prev; };
}

/** Runs `action` only when confirmed; resolves false when declined. */
export async function confirmed<T>(message: string, action: () => Promise<T>): Promise<T | false> {
  if (!confirmAction(message)) return false;
  return action();
}
