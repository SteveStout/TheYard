/**
 * A reading that eases up from nothing to its value on its first paint (the
 * operator's look): the arithmetic, React-free. A figure is a whole number,
 * written with thousands separated, and whatever follows it ("326 s"); text
 * that does not start with one is not counted and is shown as it is.
 */
export type Countable = { value: number; suffix: string };

/** The number a figure starts with and what follows it, or null when it does not start with one. */
export function countable(text: string): Countable | null {
  const match = /^(\d[\d,]*)(\D.*)?$/.exec(text);
  if (!match) return null;
  return { value: Number(match[1].replace(/,/g, '')), suffix: match[2] ?? '' };
}

/** Ease out, so the number settles rather than stops: the share of the way at a share of the time. */
export function easeOut(t: number): number {
  const clamped = Math.max(0, Math.min(1, t));
  return 1 - Math.pow(1 - clamped, 3);
}

/** The figure at a share of the time, written the way the finished one is. */
export function figureAt(figure: Countable, t: number): string {
  const value = Math.round(figure.value * easeOut(t));
  return `${value.toLocaleString('en-US')}${figure.suffix}`;
}
