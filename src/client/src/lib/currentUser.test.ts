import { describe, expect, it } from 'vitest';
import { getDevUserId, setDevUserId, DEV_USER_KEY } from './currentUser';

describe('dev user selection', () => {
  it('returns null when nothing is stored', () => {
    localStorage.clear();
    expect(getDevUserId()).toBeNull();
  });

  it('round-trips a stored id', () => {
    setDevUserId('abc-123');
    expect(localStorage.getItem(DEV_USER_KEY)).toBe('abc-123');
    expect(getDevUserId()).toBe('abc-123');
  });

  it('clears the stored id with null', () => {
    setDevUserId('abc-123');
    setDevUserId(null);
    expect(getDevUserId()).toBeNull();
  });

  it('returns null when storage is unavailable', () => {
    expect(getDevUserId(undefined)).toBeNull();
  });
});
