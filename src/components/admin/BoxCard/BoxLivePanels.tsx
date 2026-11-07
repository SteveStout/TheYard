/**
 * The box's four live panels (ADR: The box reports itself, the addendum on the live panels): each core, the memory
 * and swap, the network and the GPUs over the last sixty seconds, read from GET /api/admin/cloud/box/live every two
 * seconds while the card is open and the tab is in view, and never otherwise. Past ten seconds without a sample the
 * panels say so in blue. The layout is src/lib/box/boxLive.ts; this draws it.
 */
import { useEffect, useState } from 'react';
import {
  type BoxLiveReport,
  isStale,
  LIVE_BOX,
  livePanels,
  readBoxLive,
  shouldPoll,
} from '../../../lib/box/boxLive';
import styles from './BoxCard.module.css';

/** How often the panels ask, in milliseconds: the box's own cadence. */
const POLL_MS = 2000;

const SERIES = [styles.series1, styles.series2, styles.series3, styles.series4];

/** The ring, read every two seconds while the card is mounted and the tab is visible. */
function useLiveRing(): BoxLiveReport | null {
  const [report, setReport] = useState<BoxLiveReport | null>(null);
  useEffect(() => {
    let timer: ReturnType<typeof setInterval> | null = null;
    let alive = true;
    // A failed read leaves the last answer, which the stale line then names.
    const read = async () => {
      const answer = await readBoxLive();
      if (answer !== null && alive) setReport(answer);
    };
    const sync = () => {
      const poll = shouldPoll(alive, document.visibilityState);
      if (poll && timer === null) {
        void read();
        timer = setInterval(() => void read(), POLL_MS);
      } else if (!poll && timer !== null) {
        clearInterval(timer);
        timer = null;
      }
    };
    sync();
    document.addEventListener('visibilitychange', sync);
    return () => {
      alive = false;
      if (timer !== null) clearInterval(timer);
      document.removeEventListener('visibilitychange', sync);
    };
  }, []);
  return report;
}

export function BoxLivePanels() {
  const report = useLiveRing();
  const stale = isStale(report);
  const panels = report === null ? [] : livePanels(report);
  return (
    <section className={styles.live} data-testid="box-live" aria-label="The box, live">
      {stale && (
        <p className={styles.stale} data-testid="box-live-stale">
          no live sample
          {report?.silent_seconds != null
            ? `: the newest is ${report.silent_seconds} s old`
            : ' yet'}
        </p>
      )}
      <div className={styles.panels}>
        {(panels.length > 0
          ? panels
          : livePanels({ as_of_ms: 0, cadence_seconds: 2, silent_seconds: null, samples: [] })
        ).map((panel) => (
          <figure
            key={panel.key}
            className={`${styles.panel} op-glass`}
            data-testid={`box-live-${panel.key}`}
          >
            <figcaption className={styles.caption}>
              <span className={styles.panelTitle}>{panel.title}</span>
              <span className={styles.figures}>{panel.figures}</span>
            </figcaption>
            <svg
              className={styles.chart}
              viewBox={`0 0 ${LIVE_BOX.width} ${LIVE_BOX.height}`}
              role="img"
              aria-label={`${panel.title}, the last 60 seconds${panel.figures === '' ? ', nothing read yet' : `: ${panel.figures}`}`}
            >
              {panel.lines.map((line, index) => (
                <path
                  key={line.name}
                  className={`${styles.line} ${SERIES[index % SERIES.length]}`}
                  d={line.path}
                />
              ))}
            </svg>
            <p className={styles.axis}>
              <span>60 s</span>
              <span>top {panel.top}</span>
              <span>now</span>
            </p>
            <p className={styles.legend}>{panel.lines.map((line) => line.name).join(' · ')}</p>
          </figure>
        ))}
      </div>
    </section>
  );
}
