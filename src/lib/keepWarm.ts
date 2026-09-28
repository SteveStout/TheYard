/**
 * The health card's line for the keep-warm loop (ADR: Kept awake): when its
 * last pass ran, how many reads it sent and the slowest of them. React-free.
 */
import { formatNumber, LOCALE } from './format';

/** The loop's last pass as /api/health reports it; null where the loop is off. */
export type KeptWarm = {
  last_pass: string | null;
  reads: number;
  failed: number;
  slowest_ms: number;
  slowest: string | null;
} | null;

const clock = new Intl.DateTimeFormat(LOCALE, {
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

/** "Kept warm: last pass 12:04, 50 reads, slowest 312 ms", or what stands in for it. */
export function keptWarmLine(kept: KeptWarm | undefined): string {
  if (kept === null || kept === undefined) return 'Kept warm: off on this container';
  if (kept.last_pass === null) return 'Kept warm: the first pass is on its way';
  const when = new Date(kept.last_pass);
  const at = Number.isNaN(when.getTime()) ? '' : clock.format(when);
  const failed = kept.failed > 0 ? `, ${formatNumber(kept.failed)} did not answer` : '';
  return `Kept warm: last pass ${at}, ${formatNumber(kept.reads)} reads, slowest ${formatNumber(kept.slowest_ms)} ms${failed}`;
}
