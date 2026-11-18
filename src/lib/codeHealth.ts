/**
 * CODE HEALTH on the tests card: the numbers about the code itself that the
 * gate measures beside its suites (push to live, build warnings, the bundle's
 * size, packages with a newer release) and the last thirty gates, so a slow creep in the
 * gate's seconds or the bundle reads as a line rather than a memory. React-free:
 * plain functions over the results file, tested on their own.
 */
import { RANGES, toneOf, type RangeTone } from './ranges';

/** What the gate measured about the code, as test_results.mjs writes it; absent from a file an older gate wrote. */
export type CodeNumbers = {
  warnings: number | null;
  distJsBytes: number | null;
  distCssBytes: number | null;
  outdatedNpm: number | null;
  outdatedNuget: number | null;
};

/** One earlier gate, as the results file keeps it. */
export type GateSummary = {
  version: string;
  ranAt: string;
  gateSeconds: number;
  /** The check jobs' own wall clock; absent from a gate that ran every check in one job. */
  checksSeconds?: number | null;
  passed: number;
  failed: number;
  distJsBytes: number | null;
  /** The gate run that shipped it, its push to live in seconds and the lines it changed (G5); absent before G5. */
  runId?: number | null;
  liveSeconds?: number | null;
  changedLines?: number | null;
};

/** A version's push to live: from its gate branch's push to both sites serving it. */
export type LiveReading = { version: string; seconds: number };

/** The newest kept gate whose push to live is known: a version's own is known only after the next promote reads it. */
export function newestLive(history: GateSummary[] | undefined): LiveReading | null {
  const gate = [...(history ?? [])]
    .reverse()
    .find((row) => row.liveSeconds !== null && row.liveSeconds !== undefined);
  return gate ? { version: gate.version, seconds: gate.liveSeconds! } : null;
}

/** One row of CODE HEALTH: what is measured, the reading, and its tone. */
export type CodeRow = {
  key: string;
  label: string;
  value: string;
  tone: RangeTone | 'info' | 'plain';
};

const kb = (bytes: number) =>
  `${(bytes / 1024).toLocaleString('en-US', { maximumFractionDigits: 1 })} kB`;

/**
 * The gate's seconds from the run's start to its verdict: the check jobs' own wall clock where the gate measured
 * it, and gateSeconds where it did not, which was the one-job gate, whose promote step came straight after the
 * verdict. With the checks side by side, promote also waits on CodeRabbit, so gateSeconds runs on past the verdict
 * (ADR-098, the addendum on the checks side by side).
 */
export function verdictSeconds(gate: {
  gateSeconds: number;
  checksSeconds?: number | null;
}): number {
  return gate.checksSeconds ?? gate.gateSeconds;
}

// #region code-rows
/** The rows the section draws; a number the gate did not measure says so rather than reading as zero. */
export function codeRows(
  code: CodeNumbers | undefined,
  gateSeconds: number,
  live: LiveReading | null = null
): CodeRow[] {
  const unread = 'not measured by this gate';
  const counted = (n: number | null | undefined, unit: string) =>
    n === null || n === undefined || n < 0 ? unread : `${n.toLocaleString('en-US')} ${unit}`;
  return [
    // The row a manager who runs continuous deployment reads first, so it leads once the gate has measured one (V4).
    ...(live
      ? [
          {
            key: 'live',
            label: `Push to live, ${live.version}`,
            value: `${live.seconds.toLocaleString('en-US')} s`,
            tone: liveTone(live.seconds),
          } satisfies CodeRow,
        ]
      : []),
    {
      key: 'warnings',
      label: 'Build warnings',
      value: counted(code?.warnings, code?.warnings === 1 ? 'warning' : 'warnings'),
      // Any warning is red: the gate already fails on one (ADR: The gate runs where the memory is).
      tone:
        code?.warnings === null || code?.warnings === undefined
          ? 'plain'
          : code.warnings > 0
            ? 'bad'
            : 'good',
    },
    {
      key: 'gate',
      label: 'The gate, start to verdict',
      value: `${gateSeconds.toLocaleString('en-US')} s`,
      // Five minutes stays the target and stays printed; a gate between it and ten minutes is information, not a
      // fault, by the Admin tab's rule that amber means something needs a look, and red only from ten.
      tone: gateTone(gateSeconds),
    },
    {
      key: 'bundle',
      label: 'The page, built',
      value:
        code?.distJsBytes === null || code?.distJsBytes === undefined
          ? unread
          : `${kb(code.distJsBytes)} of script, ${kb(code.distCssBytes ?? 0)} of styles`,
      tone: 'plain',
    },
    {
      key: 'outdated',
      label: 'Packages with a newer release',
      value:
        code?.outdatedNpm === null || code?.outdatedNpm === undefined
          ? unread
          : `${counted(code.outdatedNpm, 'npm')}, ${counted(code.outdatedNuget, 'NuGet')}`,
      tone: 'plain',
    },
  ];
}
// #endregion code-rows

/** The push to live row's tone, by the gate row's rule: fine under its target, information up to red, red from it. */
export function liveTone(seconds: number): 'good' | 'info' | 'bad' {
  const tone = toneOf('liveSeconds', seconds);
  return tone === 'warn' ? 'info' : tone;
}

/** The gate row's tone: fine under its target, information up to its red line, red from it. */
export function gateTone(seconds: number): 'good' | 'info' | 'bad' {
  const tone = toneOf('gateSeconds', seconds);
  return tone === 'warn' ? 'info' : tone;
}

/** The gate's seconds over the kept gates, oldest first, with the amber line for the chart's scale. */
export function gateTrend(history: GateSummary[] | undefined): {
  points: { at: string; value: number }[];
  amber: number;
} {
  return {
    points: (history ?? []).map((gate) => ({ at: gate.ranAt, value: verdictSeconds(gate) })),
    amber: RANGES.gateSeconds.amber,
  };
}
