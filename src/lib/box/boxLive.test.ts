import { describe, expect, it } from 'vitest';
import {
  type BoxLiveReport,
  type BoxLiveSample,
  isStale,
  LIVE_BOX,
  livePanels,
  networkTop,
  rateWords,
  shareOf,
  shouldPoll,
} from './boxLive';

const NOW = Date.parse('2026-11-07T22:00:00Z');

const sample = (secondsAgo: number, load = 30, inBps = 17_300): BoxLiveSample => ({
  at: new Date(NOW - secondsAgo * 1000).toISOString(),
  cores: [load, load + 1, load + 2, load + 3],
  memory_used_b: 8_500_000_000,
  memory_total_b: 33_600_000_000,
  swap_used_b: 0,
  swap_total_b: 2_100_000_000,
  nets: [
    { name: 'enp8s0', in_bps: inBps, out_bps: 722_800 },
    { name: 'lo', in_bps: 9_000_000, out_bps: 9_000_000 },
  ],
  gpus: [
    { index: 0, load: 3 },
    { index: 1, load: 2 },
  ],
});

const report = (samples: BoxLiveSample[], silent: number | null = 1): BoxLiveReport => ({
  as_of_ms: NOW,
  cadence_seconds: 2,
  silent_seconds: silent,
  samples,
});

describe('the box live panels (ADR: The box reports itself)', () => {
  it('asks for the ring only while the card is open and the tab is in view', () => {
    expect(shouldPoll(true, 'visible')).toBe(true);
    expect(shouldPoll(true, 'hidden')).toBe(false);
    expect(shouldPoll(false, 'visible')).toBe(false);
  });

  it('reads blue past ten seconds of silence, and before the box has posted at all', () => {
    expect(isStale(report([sample(0)], 4))).toBe(false);
    expect(isStale(report([sample(0)], 11))).toBe(true);
    expect(isStale(report([], null))).toBe(true);
    expect(isStale(null)).toBe(true);
  });

  it('draws four panels with the newest figures in each title line', () => {
    const panels = livePanels(report([sample(4, 20), sample(2, 25), sample(0, 26.8)]));

    expect(panels.map((panel) => panel.key)).toEqual(['cores', 'memory', 'network', 'gpus']);
    expect(panels[0].figures).toBe('26.8 · 27.8 · 28.8 · 29.8 %');
    expect(panels[0].lines.map((line) => line.name)).toEqual(['CPU1', 'CPU2', 'CPU3', 'CPU4']);
    expect(panels[1].figures).toBe('25.3 % · swap 0 %');
    expect(panels[2].figures).toBe('in 16.9 KiB/s · out 705.9 KiB/s');
    expect(panels[3].figures).toBe('GPU 0 3 % · GPU 1 2 %');
    expect(panels[3].lines.map((line) => line.name)).toEqual(['GPU 0', 'GPU 1']);
  });

  it('puts now at the right edge and sixty seconds ago at the left, oldest first whatever the order sent', () => {
    const [cores] = livePanels(report([sample(0, 100), sample(60, 0)]));

    expect(cores.lines[0].path).toBe(
      `M 0.0 ${LIVE_BOX.height.toFixed(1)} L ${LIVE_BOX.width.toFixed(1)} 0.0`
    );
  });

  it('scales the network to the window peak and leaves the loopback out', () => {
    expect(networkTop([sample(0, 30, 3_000_000)])).toBe(4_194_304);
    expect(networkTop([])).toBe(1024);
    const network = livePanels(report([sample(0, 30, 3_000_000)]))[2];
    expect(network.top).toBe('4.0 MiB/s');
  });

  it('says rates and shares as System Monitor does, and draws nothing from an empty ring', () => {
    expect(rateWords(900)).toBe('900 B/s');
    expect(rateWords(17_300)).toBe('16.9 KiB/s');
    expect(shareOf(1, 3)).toBe(33.3);
    expect(shareOf(5, 0)).toBe(0);
    const empty = livePanels(report([], null));
    expect(empty.map((panel) => panel.figures)).toEqual(['', '', '', '']);
    expect(empty[0].lines).toEqual([]);
  });
});
