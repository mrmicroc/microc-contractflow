// API error parsing. The api helpers throw `Error(responseText)`; the server
// answers with `{ error, message }` bodies where `error` is either a machine
// code ("monthClosed") or, for plain validation failures, a human sentence.

interface ErrorBody {
  error?: unknown;
  message?: unknown;
  title?: unknown;
  [key: string]: unknown;
}

function rawOf(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

/** The parsed JSON error body, or null when the response wasn't JSON. */
export function errorBody(e: unknown): ErrorBody | null {
  try {
    const parsed: unknown = JSON.parse(rawOf(e));
    return parsed && typeof parsed === 'object' ? (parsed as ErrorBody) : null;
  } catch {
    return null;
  }
}

/** Machine codes look like identifiers ("insufficientPad"), never sentences. */
const CODE_RE = /^[a-z][A-Za-z0-9]*$/;

/** The machine error code of an API failure (e.g. "monthClosed"), or null. */
export function errorCode(e: unknown): string | null {
  const body = errorBody(e);
  return typeof body?.error === 'string' && CODE_RE.test(body.error) ? body.error : null;
}

/** A readable message from an API error body ({error, message}); falls back
 * to the raw text for non-JSON responses. */
export function errorMessage(e: unknown): string {
  const body = errorBody(e);
  if (!body) return rawOf(e);
  if (typeof body.message === 'string') return body.message;
  if (typeof body.error === 'string') return body.error;
  if (typeof body.title === 'string') return body.title; // ASP.NET ProblemDetails
  return rawOf(e);
}

/** Error codes the client translates — anything else shows the server text. */
export const KNOWN_ERROR_CODES = [
  'insufficientPad', 'monthClosed', 'timesheetLocked', 'weekLocked', 'dayOverflow',
  'duplicate', 'possibleDuplicate', 'invoiced', 'overlap', 'alreadySubmitted', 'futureBooking',
  'closeBlocked', 'concurrentUpdate',
  'noteRequired', 'billedHistory', 'workPackageInUse', 'nothingToBill', 'invoiceFrozen',
  'concurrentGenerate', 'currencyMismatch', 'statementAlreadyMatched', 'statementFinal',
  'sharesDifferentContracts', 'actualHoursUnknownProject',
] as const;

/** i18n key for a known code, or null. */
export function errorKey(e: unknown): string | null {
  const code = errorCode(e);
  return code && (KNOWN_ERROR_CODES as readonly string[]).includes(code) ? `errors.${code}` : null;
}

/** The text to show the user: translated when the code is known, else the
 * server's message. `t` is i18next's translate function. */
export function describeError(e: unknown, t: (key: string) => string): string {
  const key = errorKey(e);
  return key ? t(key) : errorMessage(e);
}
