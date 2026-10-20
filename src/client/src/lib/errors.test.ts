import { describe, expect, it } from 'vitest';
import { describeError, errorCode, errorKey, errorMessage, KNOWN_ERROR_CODES } from './errors';

// The api helpers throw Error(responseText) — these mirror real server bodies.
const err = (body: unknown) => new Error(typeof body === 'string' ? body : JSON.stringify(body));
const t = (k: string) => `T:${k}`;

describe('errorCode', () => {
  it('reads machine codes from {error, message} bodies', () => {
    expect(errorCode(err({ error: 'monthClosed', message: 'The project month is closed.' })))
      .toBe('monthClosed');
    expect(errorCode(err({ error: 'insufficientPad', freePad: 0.5, neededPad: 1, message: 'x' })))
      .toBe('insufficientPad');
    expect(errorCode(err({ error: 'possibleDuplicate', message: 'x', duplicates: ['a'] })))
      .toBe('possibleDuplicate');
  });

  it('ignores human-sentence errors and non-JSON bodies', () => {
    expect(errorCode(err({ error: 'Unknown or inactive project.' }))).toBeNull();
    expect(errorCode(err('Internal server error'))).toBeNull();
    expect(errorCode('plain string')).toBeNull();
  });
});

describe('errorMessage', () => {
  it('prefers message, then error, then ProblemDetails title, then raw', () => {
    expect(errorMessage(err({ error: 'weekLocked', message: 'That week is locked.' }))).toBe('That week is locked.');
    expect(errorMessage(err({ error: 'Booking does not belong to this resource.' })))
      .toBe('Booking does not belong to this resource.');
    expect(errorMessage(err({ title: 'One or more validation errors occurred.', status: 400 })))
      .toBe('One or more validation errors occurred.');
    expect(errorMessage(err('boom'))).toBe('boom');
  });
});

describe('describeError / errorKey', () => {
  it('translates every known code', () => {
    for (const code of KNOWN_ERROR_CODES) {
      expect(errorKey(err({ error: code, message: 'server text' }))).toBe(`errors.${code}`);
      expect(describeError(err({ error: code, message: 'server text' }), t)).toBe(`T:errors.${code}`);
    }
  });

  it('falls back to the server text for unknown codes', () => {
    expect(describeError(err({ error: 'somethingNew', message: 'Server says no.' }), t))
      .toBe('Server says no.');
    expect(errorKey(err({ error: 'somethingNew' }))).toBeNull();
  });
});
