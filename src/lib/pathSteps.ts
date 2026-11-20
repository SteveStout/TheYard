/**
 * Does:      Holds the review route's numbered path (ADR: The sales pages set the site's look): its six steps, each a
 *            number, a name and the anchor of its section, in the page's order, and the rules the bar and the phone's
 *            row read: which step the reader is in (the last whose section's top has passed 45 per cent of the
 *            window, or at the page's foot the last in view), which steps are done, and the phone row's line, "02 of 06, Three questions".
 * Does not:  Read the page or draw anything (PathBar.tsx does); no React in here.
 * Used by:   PathBar.tsx, pathSteps.test.ts.
 */

/** One step of the path: its number, its name and its section's anchor. */
export type PathStep = { number: string; name: string; id: string };

/** The six steps, in the page's order. */
export const PATH_STEPS: readonly PathStep[] = [
  { number: '01', name: 'The work', id: 'the-work' },
  { number: '02', name: 'Three questions', id: 'questions' },
  { number: '03', name: 'Who worked with him', id: 'voices' },
  { number: '04', name: 'From the blog', id: 'blog' },
  { number: '05', name: 'The tour', id: 'review-tour' },
  { number: '06', name: 'Talk', id: 'talk' },
];

/** The path's name for a screen reader. */
export const PATH_LABEL = 'The path down this page';

/** How far down the window a section's top must pass for its step to be the current one. */
export const CURRENT_AT = 0.45;

/**
 * The current step: the last whose section's top (in the window, null when the page has not drawn it) has passed
 * CURRENT_AT of the window's height; -1 while the reader is above the first. At the page's foot, where the last
 * sections can never scroll that high, it is the last step whose section has come into the window at all.
 */
export function currentStep(
  tops: readonly (number | null)[],
  windowHeight: number,
  atFoot = false
): number {
  const line = atFoot ? windowHeight : windowHeight * CURRENT_AT;
  let current = -1;
  tops.forEach((top, index) => {
    if (top !== null && top < line) current = index;
  });
  return current;
}

/** How a step is drawn: done before the current one, now at it, nothing after it. */
export function stepState(index: number, current: number): 'done' | 'now' | '' {
  if (current < 0) return '';
  return index < current ? 'done' : index === current ? 'now' : '';
}

/** The phone row's line: "02 of 06, Three questions", nothing above the first step. */
export function dockLine(current: number, steps: readonly PathStep[] = PATH_STEPS): string {
  const step = steps[current];
  if (!step) return '';
  return `${step.number} of ${String(steps.length).padStart(2, '0')}, ${step.name}`;
}
