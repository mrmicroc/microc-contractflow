import { afterEach, describe, expect, it, vi } from 'vitest';
import { confirmAction, confirmed, promptNote, setConfirmImpl, setPromptImpl } from './confirm';

describe('confirm helper', () => {
  let restore: (() => void) | null = null;
  afterEach(() => { restore?.(); restore = null; });

  it('asks with the given (translated) message', () => {
    const ask = vi.fn().mockReturnValue(true);
    restore = setConfirmImpl(ask);
    expect(confirmAction('Delete — are you sure?')).toBe(true);
    expect(ask).toHaveBeenCalledWith('Delete — are you sure?');
  });

  it('runs the action only when confirmed', async () => {
    const action = vi.fn().mockResolvedValue('done');
    restore = setConfirmImpl(() => false);
    await expect(confirmed('x', action)).resolves.toBe(false);
    expect(action).not.toHaveBeenCalled();
    restore();
    restore = setConfirmImpl(() => true);
    await expect(confirmed('x', action)).resolves.toBe('done');
    expect(action).toHaveBeenCalledTimes(1);
  });
});

describe('promptNote', () => {
  let restore: (() => void) | null = null;
  afterEach(() => { restore?.(); restore = null; });

  it('returns the trimmed comment', () => {
    restore = setPromptImpl(() => '  Wrong project  ');
    expect(promptNote('Reason?')).toBe('Wrong project');
  });

  it('cancel or blank yields null', () => {
    restore = setPromptImpl(() => null);
    expect(promptNote('Reason?')).toBeNull();
    restore();
    restore = setPromptImpl(() => '   ');
    expect(promptNote('Reason?')).toBeNull();
  });
});
