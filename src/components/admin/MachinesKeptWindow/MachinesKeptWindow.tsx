/**
 * The machines over a day, a week or a month, drawn from the minutes each site
 * keeps in the document store: a line of words and three charts slot by slot.
 * Its own file because the kept windows come from a different source than the
 * live hour the rest of the card shows, and read their own way.
 */
import styles from '../shared/card.module.css';
import type { Machines } from '../shared/types';
import {
  coverage,
  fromFirstReading,
  type MachineWindow,
  requestUnitsAMinute,
  shareOf,
  timeline,
  windowName,
} from '../../../lib/machineChart';
import { MachineChart, youngRecord } from '../charts';
import { formatNumber } from '../../../lib/format';

// #region kept-window
/**
 * A day, a week or a month of the machines, from the minutes each site keeps in
 * the document store (ADR: What the machines are doing, the addendum on the
 * windows). Three charts in the units the hour uses, drawn over the whole
 * window slot by slot, so a stretch the store holds nothing for is a gap in
 * the line and not a line drawn across it. A window the store cannot answer
 * says why instead of drawing an empty chart.
 */
export function MachinesKeptWindow({
  machines,
  window: kept,
}: {
  machines: Machines;
  window: MachineWindow;
}) {
  const history = machines.history;
  if (kept === '1h' || history === undefined || history.window !== kept) {
    return null;
  }
  if (!history.available) {
    return (
      <p className={styles.muted} data-testid="machines-history-note">
        {windowName(kept)} is not kept here: {history.note}
      </p>
    );
  }

  const whole = timeline(history.buckets, kept, history.bucket_minutes, new Date(history.as_of));
  const held = coverage(whole);
  // Counted against the whole window, drawn from the first reading.
  const slots = fromFirstReading(whole);
  const grain =
    history.bucket_minutes >= 60
      ? `${history.bucket_minutes / 60}-hour`
      : `${history.bucket_minutes}-minute`;
  const peak = history.buckets.reduce(
    (most, bucket) => Math.max(most, bucket.working_set_max_mb),
    0
  );
  const charged = history.buckets.reduce((sum, bucket) => sum + bucket.request_units, 0);

  return (
    <div data-testid="machines-history">
      <p data-testid="machines-history-line">
        <strong>{windowName(kept)}</strong>, in {grain} buckets, from the minutes the{' '}
        {history.site === 'cosmos' ? 'Cosmos DB' : 'SQL'} site has kept: {held.held} of {held.of}{' '}
        buckets hold a reading.{' '}
        {slots.length < whole.length
          ? `${youngRecord(slots)}; a gap after the first reading is drawn as the gap it is.`
          : 'A stretch with no reading is drawn as the gap it is.'}
        {held.held > 0 &&
          ` Peak working set in the window: ${formatNumber(peak)} MB. The document store charged ${Math.round(charged * 100) / 100} request units in it.`}
        {history.note !== null && held.held === 0 && ` ${history.note}.`}
      </p>
      <MachineChart
        testId="machine-history-container"
        label={`The container over the ${windowName(kept).toLowerCase()}: memory as a share of its limit, and processor share`}
        percentage
        window={kept}
        series={[
          {
            key: 'memory',
            name: 'Memory, share of the limit',
            points: slots.map((slot) => ({
              at: slot.at,
              value:
                slot.bucket === null
                  ? null
                  : shareOf(slot.bucket.working_set_mb, slot.bucket.memory_limit_mb),
            })),
          },
          {
            key: 'cpu',
            name: 'Processor share',
            points: slots.map((slot) => ({ at: slot.at, value: slot.bucket?.cpu_percent ?? null })),
          },
        ]}
      />
      <MachineChart
        testId="machine-history-relational"
        label={`The relational store over the ${windowName(kept).toLowerCase()}, as it reported itself each minute: processor and memory as shares of what the tier allows`}
        percentage
        window={kept}
        series={[
          {
            key: 'memory',
            name: 'Relational store: memory, share of the tier',
            points: slots.map((slot) => ({
              at: slot.at,
              value: slot.bucket?.sql_memory_percent ?? null,
            })),
          },
          {
            key: 'cpu',
            name: 'Relational store: processor, share of the tier',
            points: slots.map((slot) => ({
              at: slot.at,
              value: slot.bucket?.sql_cpu_percent ?? null,
            })),
          },
        ]}
      />
      <MachineChart
        testId="machine-history-document"
        label={`What the document store charged over the ${windowName(kept).toLowerCase()}, request units a minute`}
        unit="request units a minute"
        window={kept}
        series={[
          {
            key: 'ru',
            name: 'Document store',
            points: slots.map((slot) => ({ at: slot.at, value: requestUnitsAMinute(slot.bucket) })),
          },
        ]}
      />
    </div>
  );
}
// #endregion kept-window
