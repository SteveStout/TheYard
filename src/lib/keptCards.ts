/**
 * The Admin tab's four public lists over a window (ADR: Logs that outlive the
 * container, the addendum on the cards). "Now" is the ring in the process's
 * memory, which is what each card always showed; a day, a week and a month are
 * the same entries read back from the document store, in the same shape, so a
 * card draws them with the table it already has. No React in here.
 */

export type KeptCard = 'errors' | 'logs' | 'sql' | 'store';

export type CardWindow = 'now' | '24h' | '7d' | '30d';

export const CARD_WINDOWS: CardWindow[] = ['now', '24h', '7d', '30d'];

export const KEPT_CARDS: KeptCard[] = ['errors', 'logs', 'sql', 'store'];

/** What GET /api/admin/kept answers with; the entries are whatever the card's own ring serves. */
export type KeptAnswer<T> = {
  card: string;
  window: string;
  site: string;
  store: string;
  kept: { available: boolean; note: string };
  total: number;
  shown: number;
  entries: T[];
};

/** What a card needs to say about the window it is showing, without the rows. */
export type KeptMeta = {
  window: CardWindow;
  available: boolean;
  note: string;
  total: number;
  shown: number;
};

export function cardWindowName(window: CardWindow): string {
  switch (window) {
    case 'now':
      return 'Now';
    case '24h':
      return 'Last 24 hours';
    case '7d':
      return 'Last 7 days';
    case '30d':
      return 'Last 30 days';
  }
}

export const everyCardOn = <T>(value: T): Record<KeptCard, T> => ({
  errors: value,
  logs: value,
  sql: value,
  store: value,
});

/** Where a card's rows come from: its ring for now, the kept endpoint for a window. */
export function cardUrl(card: KeptCard, window: CardWindow, ringUrl: string): string {
  return window === 'now' ? ringUrl : `/api/admin/kept?card=${card}&window=${window}`;
}

export function metaOf<T>(answer: KeptAnswer<T>, window: CardWindow): KeptMeta {
  return {
    window,
    available: answer.kept.available,
    note: answer.kept.note,
    total: answer.total,
    shown: answer.shown,
  };
}

// #region kept-line
/**
 * The sentence under the buttons. It says which of three things an empty
 * table means: nothing is kept on this machine at all, nothing happened in
 * the window, or the answer has not arrived. And when the window holds more
 * than a card shows, it says how many, because two hundred rows with no total
 * beside them read as "that is all there was".
 */
export function keptLine(window: CardWindow, meta: KeptMeta | null): string {
  if (window === 'now') {
    return 'Showing what this process remembers, which a roll empties.';
  }
  const stretch = cardWindowName(window).toLowerCase();
  if (meta === null || meta.window !== window) {
    return `Reading the ${stretch}…`;
  }
  if (!meta.available) {
    return `Not kept here: ${meta.note}.`;
  }
  if (meta.total === 0) {
    return `Nothing in the ${stretch}, ${meta.note}.`;
  }
  return meta.shown >= meta.total
    ? `The ${stretch}, ${meta.note}: all ${meta.total}.`
    : `The ${stretch}, ${meta.note}: the newest ${meta.shown} of ${meta.total.toLocaleString('en-US')}.`;
}
// #endregion kept-line

/** A time for now, and a date beside it once the rows span more than today. */
export function stampFor(window: CardWindow, at: string): string {
  const when = new Date(at);
  if (Number.isNaN(when.getTime())) return '';
  if (window === 'now') return when.toLocaleTimeString();
  const two = (value: number) => String(value).padStart(2, '0');
  return `${two(when.getMonth() + 1)}-${two(when.getDate())} ${two(when.getHours())}:${two(when.getMinutes())}:${two(when.getSeconds())}`;
}
