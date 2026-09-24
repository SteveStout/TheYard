/**
 * What the Admin tab's cards share (ADR: The Admin tab, as a product, the
 * addendum on the workbench): the read an open card makes, the window a public
 * list is over, and the small pieces every card draws with.
 */
import { type ReactNode, useEffect, useState } from 'react';
import { hourOfTraffic, type TrafficSlot } from '../../lib/machineChart';
import {
  CARD_WINDOWS,
  type CardWindow,
  cardUrl,
  cardWindowName,
  type KeptAnswer,
  type KeptCard,
  keptLine,
  type KeptMeta,
  metaOf,
} from '../../lib/keptCards';
import styles from '../AdminPanel.module.css';
import type { Machines, SqlParameterShape, Fetched } from './types';

// #region reads
/** How often an open card reads again, and the strip with it. */
export const REFRESH_MS = 30_000;

/**
 * One read for one open card: when it opens, and again on the tab's clock
 * while it stays open. A closed card is not on the page, so it asks for
 * nothing (ADR: The Admin tab, as a product, the addendum on the workbench).
 */
export function useRead<T>(url: string | null, tick: number): Fetched<T> {
  const [value, setValue] = useState<Fetched<T>>(null);
  useEffect(() => {
    if (url === null) return;
    let live = true;
    // A failed or non-200 answer marks the card failed instead of leaving it loading forever.
    void fetch(url)
      .then((r) => (r.ok ? (r.json() as Promise<T>) : Promise.reject(new Error(String(r.status)))))
      .then((v) => {
        if (live) setValue(v);
      })
      .catch(() => {
        if (live) setValue('failed');
      });
    return () => {
      live = false;
    };
  }, [url, tick]);
  return value;
}
// #endregion reads

// #region kept-window
/**
 * Now, a day, a week or a month on one of the four public lists (ADR: Logs
 * that outlive the container, the addendum on the cards): the card's own
 * window, the rows the store kept for it, and the buttons that choose it.
 */
export function useKeptWindow<T>(card: KeptCard, tick: number) {
  const [window_, setWindow] = useState<CardWindow>('now');
  const [meta, setMeta] = useState<KeptMeta | null>(null);
  const [rows, setRows] = useState<Fetched<T[]>>(null);
  useEffect(() => {
    if (window_ === 'now') return;
    let live = true;
    void fetch(cardUrl(card, window_, ''))
      .then((r) =>
        r.ok ? (r.json() as Promise<KeptAnswer<T>>) : Promise.reject(new Error(String(r.status)))
      )
      .then((answer) => {
        if (!live) return;
        setRows(answer.entries);
        setMeta(metaOf(answer, window_));
      })
      .catch(() => {
        if (live) setRows('failed');
      });
    return () => {
      live = false;
    };
  }, [card, window_, tick]);
  // The change of window is the event, so the card goes back to reading here
  // and not inside the effect, as the machines window does.
  const choose = (chosen: CardWindow) => {
    if (chosen === window_) return;
    setWindow(chosen);
    setMeta(null);
    setRows(null);
  };
  const picker = <CardWindowPicker card={card} value={window_} meta={meta} onChoose={choose} />;
  return { window: window_, rows, picker };
}
// #endregion kept-window

/** A status as a pill: good or bad, and the word inside it says which. */
export const pill = (ok: boolean) =>
  ok ? `${styles.pill} ${styles.ok}` : `${styles.pill} ${styles.bad}`;

/** What a card says when its last read failed. */
export const failed = (what: string) => (
  <p className={styles.muted} data-testid="card-failed">
    Could not read {what} on the last try; the next try is in 30 seconds.
  </p>
);

/** A card this container has nothing for: its name, and why, in words. */
export function Absent({ name, note }: { name: string; note: string | null }) {
  return (
    <article className={`${styles.wide} op-glass`} data-testid="bench-absent">
      <h2 className={styles.cardTitle}>{name}</h2>
      <p className={styles.muted}>{note ?? 'Loading…'}</p>
    </article>
  );
}

export function formatUptime(totalSeconds: number): string {
  const days = Math.floor(totalSeconds / 86_400);
  const hours = Math.floor((totalSeconds % 86_400) / 3_600);
  const minutes = Math.floor((totalSeconds % 3_600) / 60);
  if (days > 0) return `${days}d ${hours}h ${minutes}m`;
  if (hours > 0) return `${hours}h ${minutes}m`;
  return `${minutes}m ${Math.floor(totalSeconds % 60)}s`;
}

/**
 * Now, a day, a week or a month, on one of the four public lists (ADR: Logs
 * that outlive the container, the addendum on the cards), and the sentence
 * that says what the table under it is showing.
 */
export function CardWindowPicker({
  card,
  value,
  meta,
  onChoose,
}: {
  card: KeptCard;
  value: CardWindow;
  meta: KeptMeta | null;
  onChoose: (chosen: CardWindow) => void;
}) {
  return (
    <>
      <p
        className={`${styles.statusRow} op-seg op-seg-wrap`}
        role="group"
        aria-label={`Window for the ${card} card`}
      >
        {CARD_WINDOWS.map((option) => (
          <button
            key={option}
            type="button"
            className={styles.back}
            aria-pressed={option === value}
            onClick={() => onChoose(option)}
            data-testid={`kept-window-${card}-${option}`}
          >
            {cardWindowName(option)}
          </button>
        ))}
      </p>
      <p className={styles.muted} data-testid={`kept-line-${card}`}>
        {keptLine(value, meta)}
      </p>
    </>
  );
}

/** What a card is and how to read it, folded away: the numbers come first, and the paragraph is one tap off. */
export function About({ children }: { children: ReactNode }) {
  return (
    <details className={styles.about}>
      <summary className={styles.aboutSummary}>What this shows</summary>
      <p className={styles.muted}>{children}</p>
    </details>
  );
}

export function describeParameters(parameters: SqlParameterShape[]): string {
  return parameters.length === 0
    ? 'none'
    : parameters
        .map(
          (parameter) =>
            `${parameter.name} ${parameter.type}` +
            (parameter.size === null ? '' : `(${parameter.size})`)
        )
        .join(', ');
}

/** A number of milliseconds, or the word for not having one. */
export function ms(value: number | null | undefined): string {
  return value === null || value === undefined ? 'not measured' : `${value} ms`;
}

/** Milliseconds with the request charge beside them, when there is one (ADR: Backends, side by side). */
export function msAndRu(value: number | null | undefined, ru: number | null | undefined): string {
  const time = ms(value);
  return ru === null || ru === undefined ? time : `${time} · ${ru} RU`;
}

/** The hour the request ring holds, as slots; the traffic card and the tiles over the page both read this one. */
export function hourSlots(machines: Machines): TrafficSlot[] | null {
  if (machines.traffic === undefined) return null;
  const minutes = machines.traffic.minutes;
  return hourOfTraffic(
    minutes,
    new Date(machines.history?.as_of ?? minutes[minutes.length - 1]?.at ?? 0)
  );
}
