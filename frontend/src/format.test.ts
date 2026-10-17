import { describe, expect, it } from 'vitest';
import { eventInputToIso, formatEventDate } from './format';

describe('event date formatting', () => {
  it('converts Moscow local input to an offset-aware UTC instant', () => {
    expect(eventInputToIso('2030-11-05T18:30')).toBe('2030-11-05T15:30:00.000Z');
  });

  it('displays event times in Moscow time', () => {
    expect(formatEventDate('2030-11-05T15:30:00.000Z')).toContain('18:30');
  });
});
