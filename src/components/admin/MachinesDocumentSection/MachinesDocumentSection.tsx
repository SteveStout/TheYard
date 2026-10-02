/**
 * The document store's part of the machines card: what its operations charged
 * in request units, a gauge of the busiest minute against the free allowance,
 * a chart and the newest twenty minutes. Its own file because this store is
 * sold by request unit and has no memory or processor reading to show.
 */
import styles from '../shared/card.module.css';
import type { DocumentMinute, Machines } from '../shared/types';
import { busiestRate } from '../../../lib/machineChart';
import { BarGauge, MachineChart } from '../charts';
import { type Column, DataTable } from '../DataTable';
import { formatNumber } from '../../../lib/format';
import { millisecondsWords } from '../../../lib/statTiles';

/**
 * The document store a minute at a time: what it charged, for how many
 * operations, and the minute's average rate, its charge over sixty seconds, as
 * a share of the free request units a second.
 */
const DOCUMENT_COLUMNS: Column<DocumentMinute>[] = [
  {
    name: 'Minute',
    mono: true,
    short: true,
    cell: (minute) => new Date(minute.at).toLocaleTimeString(),
  },
  {
    name: 'Request units',
    mono: true,
    num: true,
    cell: (minute) => `${formatNumber(minute.request_units)} RU`,
  },
  { name: 'Operations', mono: true, num: true, cell: (minute) => minute.operations },
  {
    name: 'Average a second, share of free',
    mono: true,
    num: true,
    cell: (minute) => `${minute.share_of_free_percent}%`,
  },
];

/**
 * The document store as its operations cost it, or the note that says why it
 * cannot. The total is said over the stretch the server's operations ring
 * reaches, when the server says how long that is.
 */
export function MachinesDocumentSection({
  documentStore,
}: {
  documentStore: Machines['document'];
}) {
  // How many minutes back the operations ring reaches, as the server measured
  // it, so the request units below are a total over a stated stretch.
  const operationsRing: { store: string; span_minutes?: number | null } = documentStore;
  const ringSpan =
    operationsRing.span_minutes === null || operationsRing.span_minutes === undefined
      ? 'in the ring'
      : `over the last ${formatNumber(operationsRing.span_minutes)} min, all the ring holds`;

  return (
    <>
      <h3 className={styles.cardTitle}>{documentStore.store}</h3>
      {!documentStore.available ? (
        <p className={styles.muted} data-testid="machines-document-note">
          {documentStore.note}
        </p>
      ) : (
        <>
          <p data-testid="machines-document-line">
            <strong>{documentStore.request_units} request units</strong> across{' '}
            {documentStore.operations} operations {ringSpan},{' '}
            {documentStore.p50_ms === null || documentStore.p95_ms === null
              ? 'none of them timed'
              : `${millisecondsWords(documentStore.p50_ms)} at the median and ${millisecondsWords(documentStore.p95_ms)} at the ninety-fifth`}
            . The free tier allows {documentStore.free_request_units_per_second} request units a
            second, and the gauge below is the busiest minute in the ring as a rate against that
            allowance; the table&rsquo;s last column is each minute&rsquo;s average rate against it.
            There is no memory or processor reading here: the store is sold by request unit and
            reports neither.
          </p>
          <BarGauge
            testId="machines-ru-gauge"
            name="Request units, busiest minute"
            ceiling={`${documentStore.free_request_units_per_second.toLocaleString()} / s free`}
            value={busiestRate(documentStore.minutes)}
            max={documentStore.free_request_units_per_second}
            reading={`${busiestRate(documentStore.minutes).toLocaleString()} / s`}
            tone="gold"
          />
          <MachineChart
            testId="machine-chart-document"
            label="What the document store charged, request units a minute"
            unit="request units a minute"
            series={[
              {
                key: 'ru',
                name: 'Request units a minute',
                points: documentStore.minutes.map((minute) => ({
                  at: minute.at,
                  value: minute.request_units,
                })),
              },
            ]}
          />
          <DataTable
            label="What the document store charged"
            testId="machines-document-table"
            rows={[...documentStore.minutes].reverse().slice(0, 20)}
            rowKey={(minute) => minute.at}
            columns={DOCUMENT_COLUMNS}
          />
        </>
      )}
    </>
  );
}
