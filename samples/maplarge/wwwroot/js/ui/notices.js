/**
 * The notice line above the file table: a short message after an action, with a Dismiss button,
 * so a person always sees what their last action did.
 */
import { buildElement, replaceContents } from './elements.js';
/**
 * Creates the function that writes into the notice line.
 * @param noticeBox the element the notices are drawn in
 */
export function createNotices(noticeBox) {
    return (kind, text) => {
        const notice = buildElement('div', { class: `notice ${kind}`, role: 'status' }, text, ' ', buildElement('button', { type: 'button', class: 'small', onclick: () => replaceContents(noticeBox) }, 'Dismiss'));
        replaceContents(noticeBox, notice);
    };
}
/**
 * The message to show for a caught error: the server's sentence for an ApiError,
 * the browser's message for any other Error, or the value as text otherwise.
 */
export function messageOf(error) {
    return error instanceof Error ? error.message : String(error);
}
