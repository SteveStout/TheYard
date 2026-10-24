import { describe, expect, it } from 'vitest';
import {
  dedupe,
  PROCESS_STRIP_CELLS,
  PROCESS_WINDOWS,
  processRunsUrl,
  runsByDay,
  stripCells,
} from './cloudProcesses';

// #region cloud-processes-tests
describe('the nightly card process sections', () => {
  it('draws exactly PROCESS_STRIP_CELLS cells at none, one and many days', () => {
    expect(stripCells([])).toEqual(Array(PROCESS_STRIP_CELLS).fill('skip'));
    const one = stripCells(['fail']);
    expect(one).toHaveLength(PROCESS_STRIP_CELLS);
    expect(one[PROCESS_STRIP_CELLS - 1]).toBe('fail');
    expect(one.filter((cell) => cell !== 'skip')).toEqual(['fail']);
    const many = stripCells([...Array<string>(40).fill('ok'), 'late', 'shadow', 'nonsense']);
    expect(many).toHaveLength(PROCESS_STRIP_CELLS);
    expect(many.slice(-3)).toEqual(['late', 'shadow', 'skip']);
  });

  it('groups the runs under their Central day in the order the page answered', () => {
    const row = (day: string, when: string) =>
      ({
        at_ms: 0,
        day,
        when,
        took: '1 s',
        outcome: 'success',
        came: 'success',
        tone: 'good',
        sentence: '',
        trace_href: null,
      }) as const;
    const groups = runsByDay([
      row('Sat 24 Oct', '11:20'),
      row('Sat 24 Oct', '10:50'),
      row('Fri 23 Oct', '23:50'),
    ]);
    expect(groups.map((group) => [group.day, group.rows.length])).toEqual([
      ['Sat 24 Oct', 2],
      ['Fri 23 Oct', 1],
    ]);
    expect(runsByDay([])).toEqual([]);
  });

  it('shows a run once when a later page repeats it', () => {
    const row = (at: number, trace: string) =>
      ({
        at_ms: at,
        day: 'Sat 24 Oct',
        when: '11:20',
        took: '1 s',
        outcome: 'success',
        came: 'success',
        tone: 'good',
        sentence: '',
        trace_href: trace,
      }) as const;
    expect(
      dedupe([row(3, 'a'), row(2, 'b'), row(2, 'b'), row(1, 'c')]).map((r) => r.trace_href)
    ).toEqual(['a', 'b', 'c']);
  });

  it('asks the runs route by the process, the window and the page', () => {
    expect(processRunsUrl('AtsSweep', '7d', 2)).toBe(
      '/api/admin/cloud/nightly/runs/AtsSweep?window=7d&page=2'
    );
    expect(PROCESS_WINDOWS.map((option) => option.key)).toEqual(['24h', '7d', '30d']);
  });
});
// #endregion cloud-processes-tests
