/**
 * CODE HEALTH on the tests card: the numbers about the code itself that the
 * gate measures beside its suites (build warnings, the bundle's size, packages
 * with a newer release) and the last thirty gates, so a slow creep in the
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
  passed: number;
  failed: number;
  distJsBytes: number | null;
};

/** One row of CODE HEALTH: what is measured, the reading, and its tone. */
export type CodeRow = { key: string; label: string; value: string; tone: RangeTone | 'plain' };

const kb = (bytes: number) =>
  `${(bytes / 1024).toLocaleString('en-US', { maximumFractionDigits: 1 })} kB`;

// #region code-rows
/** The rows the section draws; a number the gate did not measure says so rather than reading as zero. */
export function codeRows(code: CodeNumbers | undefined, gateSeconds: number): CodeRow[] {
  const unread = 'not measured by this gate';
  const counted = (n: number | null | undefined, unit: string) =>
    n === null || n === undefined || n < 0 ? unread : `${n.toLocaleString('en-US')} ${unit}`;
  return [
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
      tone: toneOf('gateSeconds', gateSeconds),
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

/** The gate's seconds over the kept gates, oldest first, with the amber line for the chart's scale. */
export function gateTrend(history: GateSummary[] | undefined): {
  points: { at: string; value: number }[];
  amber: number;
} {
  return {
    points: (history ?? []).map((gate) => ({ at: gate.ranAt, value: gate.gateSeconds })),
    amber: RANGES.gateSeconds.amber,
  };
}
