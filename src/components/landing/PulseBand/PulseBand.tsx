/**
 * The first screen's band: four instruments reading the site that is answering this page, from
 * /api/pulse (ADR: One reading for the first screen). How fast it answers, what it has cost this
 * month, how long it has been up since the last roll, and how many people have visited (ADR: People
 * who have visited, counted once; that instrument is PeopleInstrument.tsx beside this file).
 *
 * The band stands at its full height from the first paint and every instrument keeps its box
 * whether its figure has arrived or not, so nothing under it moves when one does. A figure the
 * server could not read says so in words, with its scale empty: the band never shows a number the
 * site cannot stand behind.
 *
 * An instrument's label names the reading it stands over, so it is the article's own name rather
 * than a heading: four headings here would put three sections in the page's outline that a reader
 * navigating by heading would land on and find are not sections at all.
 *
 * It reads again every fifteen seconds while the page is open and stops while the tab is hidden,
 * so a phone in a pocket costs nothing.
 *
 * It wears the sales pages' dark live panel laid on its side (ADR: The sales pages set the site's look): the head
 * "TheYard, right now" with the LIVE tag, then the four instruments, four across at a desk and two by two on a phone.
 * Its live dot breathes three times when the band is first seen (useFirstSeen); what it reads does not change.
 */
import {
  buildWords,
  LATENCY_CEILING_MS,
  forecastWords,
  moneyWords,
  share,
  ticks,
  UNREAD,
  uptimeWords,
} from '../../../lib/pulse';
import { PeopleInstrument } from './PeopleInstrument';
import styles from './PulseBand.module.css';
import { useFirstSeen } from '../../../hooks/useFirstSeen';
import { usePulse } from '../../../hooks/usePulse';

/** The documents an instrument opens: how it performs, how it rolls, and how a visitor is counted. */
export type PulseDoc = 'performance' | 'adrBlueGreenMeasuredAndHeld' | 'adrPeopleWhoHaveVisited';

/** The scale's graduations, drawn once: the ceiling never changes while the page is open. */
const MARKS = ticks(LATENCY_CEILING_MS);

export function PulseBand({ onOpenDoc }: { onOpenDoc: (key: PulseDoc) => void }) {
  // The one read of /api/pulse (usePulse.ts): the drawn page's reading first, then every 15 seconds.
  const { pulse } = usePulse();
  // The holder of the LIVE dot's three breaths.
  const [holder] = useFirstSeen<HTMLElement>();

  const latency = pulse?.usual_p95_ms_24h ?? null;
  const spend = pulse?.month_to_date ?? null;
  const forecast = pulse?.forecast_month ?? null;
  const currency = pulse?.currency ?? 'USD';
  // The fill runs to the month's spend as a share of what the month is forecast to cost; with no
  // forecast there is nothing to be a share of, so the track stays empty.
  const billed =
    spend !== null && forecast !== null && forecast > 0 ? Math.min(1, spend / forecast) : 0;
  const latencyShare = share(latency, LATENCY_CEILING_MS);

  return (
    <section
      ref={holder}
      className={`${styles.band} op-live-panel`}
      aria-label="What this site is doing right now"
      data-mock="live-panel"
      data-testid="pulse-band"
    >
      <p className={`${styles.head} op-live-head`}>
        <span>TheYard, right now</span>
        <span className="op-live-tag">
          <i className="op-live-dot" aria-hidden="true" />
          Live
        </span>
      </p>
      <article
        className={styles.instrument}
        aria-labelledby="pulse-answering"
        data-testid="pulse-latency"
      >
        <p className={styles.label} id="pulse-answering">
          Answering in
        </p>
        <p className={styles.reading}>
          {latency === null ? (
            <span className={styles.unread}>{UNREAD}</span>
          ) : (
            <>
              <span
                className={styles.figure}
                data-mock="live-value"
                data-testid="pulse-latency-figure"
              >
                {latency.toLocaleString('en-US')}
              </span>
              <span className={styles.unit}>ms</span>
            </>
          )}
        </p>
        <svg
          className={styles.scale}
          viewBox="0 0 320 34"
          role="img"
          aria-label={
            latency === null
              ? 'The latency scale, nothing read'
              : `${latency} milliseconds on a scale to ${LATENCY_CEILING_MS.toLocaleString('en-US')}`
          }
        >
          <line className={styles.axis} x1="2" y1="12" x2="318" y2="12" />
          {MARKS.map((mark) => (
            <line
              key={mark.at}
              className={mark.major ? styles.major : styles.minor}
              x1={2 + (316 * mark.at) / LATENCY_CEILING_MS}
              y1="12"
              x2={2 + (316 * mark.at) / LATENCY_CEILING_MS}
              y2={mark.major ? 20 : 17}
            />
          ))}
          {MARKS.filter((mark) => mark.major).map((mark) => (
            <text
              key={mark.at}
              className={styles.number}
              x={2 + (316 * mark.at) / LATENCY_CEILING_MS}
              y="32"
              textAnchor={
                mark.at === 0 ? 'start' : mark.at === LATENCY_CEILING_MS ? 'end' : 'middle'
              }
            >
              {mark.at.toLocaleString('en-US')}
            </text>
          ))}
          {latency !== null && (
            <>
              <line className={styles.fill} x1="2" y1="12" x2={2 + 316 * latencyShare} y2="12" />
              <circle className={styles.marker} cx={2 + 316 * latencyShare} cy="12" r="4" />
            </>
          )}
        </svg>
        <p className={styles.detail}>
          server time at p95, the usual five minutes of the last day
          {pulse?.requests_24h
            ? ` over ${pulse.requests_24h.toLocaleString('en-US')} requests`
            : ''}
          {'. '}
          <button type="button" className={styles.link} onClick={() => onOpenDoc('performance')}>
            How it performs
          </button>
        </p>
      </article>

      <article className={styles.instrument} aria-labelledby="pulse-azure" data-testid="pulse-cost">
        <p className={styles.label} id="pulse-azure">
          Azure this month
        </p>
        <p className={styles.reading}>
          {spend === null ? (
            <span className={styles.unread}>{UNREAD}</span>
          ) : (
            <span className={styles.figure} data-mock="live-value" data-testid="pulse-cost-figure">
              {moneyWords(spend, currency)}
            </span>
          )}
        </p>
        <p className={styles.gauge}>
          <span className={styles.track} aria-hidden="true">
            <span className={styles.spent} style={{ width: `${(billed * 100).toFixed(1)}%` }} />
            {billed > 0 && (
              <span className={styles.pip} style={{ left: `${(billed * 100).toFixed(1)}%` }} />
            )}
          </span>
          <span className={styles.forecast}>{forecastWords(spend, forecast, currency)}</span>
        </p>
        <p className={styles.detail}>
          the bill, by resource, read from Azure. The Admin tab shows every line.
        </p>
      </article>

      <article
        className={styles.instrument}
        aria-labelledby="pulse-roll-label"
        data-testid="pulse-roll"
      >
        <p className={styles.label} id="pulse-roll-label">
          Since the last roll
        </p>
        <p className={styles.reading}>
          <span className={styles.live} aria-hidden="true" />
          <span className={styles.figure} data-mock="live-value" data-testid="pulse-roll-figure">
            {pulse === null ? UNREAD : uptimeWords(pulse.uptime_seconds)}
          </span>
        </p>
        <p className={styles.version}>
          {pulse === null ? ' ' : buildWords(pulse.version, pulse.commit)}
        </p>
        <p className={styles.detail}>
          rolled in place by the pipeline, behind one gate.{' '}
          <button
            type="button"
            className={styles.link}
            onClick={() => onOpenDoc('adrBlueGreenMeasuredAndHeld')}
          >
            How it rolls
          </button>
        </p>
      </article>

      <PeopleInstrument pulse={pulse} onOpenDoc={onOpenDoc} />
    </section>
  );
}
