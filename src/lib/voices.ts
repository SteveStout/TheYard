/**
 * Does:      Names whose words stand beside each claim (ADR: The review route gives a reason before it asks, the
 *            addendum on named voices): one list for the review route's three cards, its pair, its close and the home
 *            page's three, and the picker that finds a name's entry in the /api/recommendations answer.
 * Does not:  Hold anybody's words (the API answers them whole, and a test holds that no source file types them),
 *            read anything or draw anything.
 * Used by:   Voice.tsx, ReviewPath.tsx, ReviewPage.tsx, ReviewClose.tsx, voices.test.ts.
 */
import type { Recommendation } from './recommendations';

/** Who speaks where, by name, in the order each place draws them. */
export const VOICES = {
  quality: ['Henry Crans'],
  gate: ['Sarah Sebestyen'],
  room: ['Meagan Colley'],
  pair: ['Sean Whitesell', 'Khushbu Shah'],
  close: ['Anthony Fortner'],
  home: ['Cliff True', 'Sanders Crosby', 'Micah Johnson'],
} as const;

/** A place a voice is drawn. */
export type VoicePlace = keyof typeof VOICES;

/** "Read it in full", the voice's last words, at the right of its signature. */
export const READ_IN_FULL = 'Read it in full';

/** A name's entry in the answer, or null before the answer or when the answer does not hold the name. */
export function voiceOf(
  entries: readonly Recommendation[] | null,
  name: string
): Recommendation | null {
  return entries?.find((entry) => entry.name === name) ?? null;
}
