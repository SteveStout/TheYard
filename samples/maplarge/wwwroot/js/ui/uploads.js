/**
 * Uploads files into the folder on screen, one at a time, each with its own progress bar. When a
 * file with the same name is already there, the batch pauses and asks: Overwrite sends that file
 * again with overwrite on, Skip leaves it, and either way the rest of the batch carries on.
 */
import * as api from '../lib/api.js';
import { bytes } from '../lib/format.js';
import { ApiError } from '../lib/types.js';
import { buildElement, replaceContents } from './elements.js';
import { messageOf } from './notices.js';
/**
 * Creates the function that uploads a batch of files.
 * @param parts what an upload needs from the browser around it
 */
export function createUploader(parts) {
    /** Sends one file and draws its progress bar while it goes. */
    function uploadOne(file, overwrite) {
        const bar = buildElement('div', {});
        replaceContents(parts.noticeBox, buildElement('div', { class: 'notice' }, `Uploading ${file.name} (${bytes(file.size)})`, buildElement('div', { class: 'progress' }, bar)));
        return api.upload(parts.folder(), file, overwrite, (fraction) => { bar.style.width = `${Math.round(fraction * 100)}%`; });
    }
    /**
     * Shows the "name already exists" question with Overwrite and Skip buttons, and returns a
     * promise that resolves to true for Overwrite or false for Skip, so the loop can wait for it.
     */
    function askOverwrite(message) {
        return new Promise((resolve) => {
            replaceContents(parts.noticeBox, buildElement('div', { class: 'notice' }, `${message} `, buildElement('button', { type: 'button', class: 'small', onclick: () => resolve(true) }, 'Overwrite'), ' ', buildElement('button', { type: 'button', class: 'small', onclick: () => resolve(false) }, 'Skip')));
        });
    }
    // A 409 (the name is taken) asks the person; any other error stops the batch and shows why.
    return async function uploadFiles(files) {
        if (files.length === 0) {
            return;
        }
        for (const file of files) {
            try {
                await uploadOne(file, false);
            }
            catch (error) {
                if (!(error instanceof ApiError) || error.status !== 409) {
                    parts.say('error', messageOf(error));
                    return;
                }
                if (await askOverwrite(error.message)) {
                    try {
                        await uploadOne(file, true);
                    }
                    catch (again) {
                        parts.say('error', messageOf(again));
                        return;
                    }
                }
            }
        }
        parts.fileInput.value = '';
        parts.say('ok', files.length === 1 ? `Uploaded ${files[0]?.name ?? ''}.` : `Uploaded ${files.length} files.`);
        await parts.refresh();
    };
}
