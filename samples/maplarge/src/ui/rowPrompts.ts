/**
 * Questions asked inside a table row: "Delete this?" with a confirm button, or "Move to where?"
 * with a text box. Asking in the row keeps the question next to the entry it is about, and the
 * browser never opens a pop-up. Cancel, or a failed action, puts the row's buttons back.
 */

import { buildElement, replaceContents } from './elements.js';

/** Runs an action, then refreshes; on failure it calls restore and shows the error. */
export type Run = (act: () => Promise<void>, restore: () => void) => Promise<void>;

/**
 * Replaces a row's action buttons with a question, a confirm button and a Cancel button.
 * @param tr the row the question is about
 * @param question what to ask
 * @param verb the confirm button's label
 * @param act what to do on confirm
 * @param run runs the action and handles its result
 */
export function confirmInRow(tr: HTMLTableRowElement, question: string, verb: string, act: () => Promise<void>, run: Run): void {
  const cell = tr.querySelector('td.actions');
  if (!cell) {
    return;
  }
  const previous = [...cell.childNodes];
  const restore = (): void => replaceContents(cell, ...previous);
  const confirm = buildElement('button', { type: 'button', class: 'danger', onclick: () => void run(act, restore) }, verb);
  replaceContents(cell, buildElement('span', {}, question, ' '), confirm, buildElement('button', { type: 'button', onclick: restore }, 'Cancel'));
  confirm.focus();
}

/**
 * Replaces a row's action buttons with a text box, a confirm button and a Cancel button, for
 * actions that need a destination path (move and copy).
 * @param tr the row the question is about
 * @param label what the text box is for, read out by a screen reader
 * @param value the text box's starting value
 * @param verb the confirm button's label
 * @param act what to do with the text typed
 * @param run runs the action and handles its result
 */
export function askInRow(tr: HTMLTableRowElement, label: string, value: string, verb: string, act: (to: string) => Promise<void>, run: Run): void {
  const cell = tr.querySelector('td.actions');
  if (!cell) {
    return;
  }
  const previous = [...cell.childNodes];
  const restore = (): void => replaceContents(cell, ...previous);
  const input = buildElement('input', { type: 'text', value, 'aria-label': label, size: '32' });
  const form = buildElement('form', { onsubmit: (event: Event) => { event.preventDefault(); void run(() => act(input.value.trim()), restore); } },
    input, ' ', buildElement('button', { type: 'submit' }, verb), ' ', buildElement('button', { type: 'button', onclick: restore }, 'Cancel'));
  replaceContents(cell, form);
  input.focus();
  input.select();
}
