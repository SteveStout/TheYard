/**
 * The nightly card's two added slots (ADR-102, the addendum on the description and the processes): the
 * paragraph saying what the Function is, read from its own code, and one section a process with its switch
 * chips, its summary and schedule, its four figures each with a source, its strip of thirty Central days and
 * its run history. Every word, tone and link comes from the server; a card that sends neither draws nothing.
 */
import { portalSentence } from '../../../lib/cloud';
import {
  PROCESS_STRIP_CELLS,
  stripCells,
  type CloudDescription,
  type CloudFigure,
  type CloudProcess,
} from '../../../lib/cloudProcesses';
import { ProcessHistory } from '../NightlyCard';
import styles from './CloudProcesses.module.css';

/** What the resource is, under the resource line. */
export function CloudWhat({ description }: { description: CloudDescription }) {
  return (
    <p className={styles.what} data-testid="cloud-description">
      <b>What it is.</b> {description.text}
      <span className={styles.src}>source: {description.source}</span>
    </p>
  );
}

/** Two figures, each with its source under it. */
function Figures({ figures }: { figures: CloudFigure[] }) {
  return (
    <dl className={styles.figures}>
      {figures.map((figure) => (
        <div key={figure.label}>
          <dt>{figure.label}</dt>
          <dd>
            {figure.text}
            <span className={styles.src}>{figure.source}</span>
          </dd>
        </div>
      ))}
    </dl>
  );
}

/** The thirty days, one cell a day coloured by its worst run, hollow where none ran. */
function Strip({ process }: { process: CloudProcess }) {
  const cells = stripCells(process.strip);
  const width = 300;
  const step = width / PROCESS_STRIP_CELLS;
  return (
    <div className={styles.runs}>
      <span className={styles.lbl}>
        <span>{process.strip_label}</span>
        <span>{process.strip_total}</span>
      </span>
      <svg
        viewBox={`0 0 ${width} 34`}
        preserveAspectRatio="none"
        role="img"
        aria-label={`${process.name}: ${process.strip_label}, one cell a day: ${cells.filter((c) => c === 'fail').length} days with a failed or blocked run, ${cells.filter((c) => c === 'late').length} with a late one`}
        data-testid={`process-strip-${process.key}`}
      >
        {cells.map((cell, i) => (
          <rect
            key={i}
            className={styles[cell]}
            data-cell={cell}
            x={i * step + 1}
            y={4}
            width={step - 2}
            height={26}
          />
        ))}
      </svg>
      <span className={styles.leg}>
        a cell a day, coloured by its worst run
        {process.all_href !== null && (
          <>
            {' · '}
            <a href={process.all_href} target="_blank" rel="noreferrer noopener">
              every run in App Insights
              <span className={styles.hidden}> {portalSentence(process.all_href, true)}</span>
            </a>
          </>
        )}
      </span>
    </div>
  );
}

// #region cloud-processes
export function CloudProcesses({ processes }: { processes: CloudProcess[] }) {
  if (processes.length === 0) return null;
  return (
    <section
      className={styles.slot}
      aria-labelledby="cloud-processes-title"
      data-testid="cloud-processes"
    >
      <div className={styles.head}>
        <h3 id="cloud-processes-title">
          The {processes.length === 6 ? 'six' : processes.length} processes
        </h3>
        <span>
          one section each · the strip is thirty Central days, a cell a day coloured by its worst
          run · Run history opens each process's own runs
        </span>
      </div>
      <div className={styles.procs}>
        {processes.map((process) => (
          <section
            key={process.key}
            className={`${styles.proc} op-glass`}
            data-tone={process.tone}
            data-testid={`process-${process.key}`}
            aria-labelledby={`process-title-${process.key}`}
          >
            <div>
              <div className={styles.name}>
                <span className={styles.dot} data-tone={process.tone} aria-hidden="true" />
                <h4 id={`process-title-${process.key}`}>{process.name}</h4>
                {process.switches.map((chip) => (
                  <span key={chip.name} className={styles.chip} data-tone={chip.tone}>
                    {chip.name} {chip.value}
                  </span>
                ))}
              </div>
              <p className={styles.desc}>
                {process.description}
                <span className={styles.sched}>{process.schedule}</span>
              </p>
            </div>
            <Figures figures={process.figures.slice(0, 2)} />
            <Figures figures={process.figures.slice(2)} />
            <Strip process={process} />
            <ProcessHistory process={process} />
          </section>
        ))}
      </div>
    </section>
  );
}
// #endregion cloud-processes
