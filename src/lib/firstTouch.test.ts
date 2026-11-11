import { describe, expect, it } from 'vitest';
import { anyLine, measureLine, minuteCell, minutes, type PounceDay } from './firstTouch';

const day = (read: boolean): PounceDay => ({
  day: '2026-11-11',
  read,
  caught: 0,
  touched: 0,
  median_min: 0,
  worst_min: 0,
  waiting: 0,
  oldest_wait_min: 0,
  judgement: read ? 'quiet' : 'unread',
});

describe('the time to first touch card', () => {
  it('prints minutes up to 120, then hours and minutes', () => {
    expect([
      minutes(0),
      minutes(45),
      minutes(120),
      minutes(121),
      minutes(185),
      minutes(180),
    ]).toEqual(['0 min', '45 min', '120 min', '2 h 1 min', '3 h 5 min', '3 h']);
  });

  it('prints over 48 hours as days and hours', () => {
    expect([minutes(2880), minutes(2881), minutes(3060), minutes(4320)]).toEqual([
      '48 h',
      '2 d',
      '2 d 3 h',
      '3 d',
    ]);
  });

  it('reads a negative or fractional minute as whole and never below zero', () => {
    expect([minutes(-5), minutes(59.6)]).toEqual(['0 min', '60 min']);
  });

  it('leaves a minute cell blank when nothing it measures happened', () => {
    expect([minuteCell(0, 0), minuteCell(2, 41)]).toEqual(['', '41 min']);
  });

  it('says what it measures and the target', () => {
    expect(measureLine(60)).toContain('a median of 60 min or less');
  });

  it('is empty until a day has a line', () => {
    expect(anyLine({ days: [day(false), day(false)] })).toBe(false);
    expect(anyLine({ days: [day(false), day(true)] })).toBe(true);
  });
});
