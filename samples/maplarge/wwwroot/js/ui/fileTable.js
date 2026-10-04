/**
 * The totals line and the file table: one row per folder and file, sorted the way the address
 * says, with the buttons each row needs. This file only draws. The clicks are handled by the
 * browser, which reads the path and kind each row carries in its data-path and data-kind
 * attributes, so one listener serves every row.
 */
import * as api from '../lib/api.js';
import { bytes, plural, when } from '../lib/format.js';
import { buildElement, replaceContents } from './elements.js';
/** Draws the totals line: folders, files and bytes, plus the search and whether it stopped early. */
export function renderReadout(readout, current) {
    const totals = current.totals;
    const items = [
        buildElement('span', {}, buildElement('strong', {}, totals.folder_count.toLocaleString()), plural(totals.folder_count, 'folder').replace(/^\S+ /, '')),
        buildElement('span', {}, buildElement('strong', {}, totals.file_count.toLocaleString()), plural(totals.file_count, 'file').replace(/^\S+ /, '')),
        buildElement('span', {}, buildElement('strong', {}, bytes(totals.total_bytes)), 'in files'),
    ];
    if ('query' in current) {
        items.unshift(buildElement('span', {}, buildElement('strong', {}, 'search'), `for ${current.query}`));
        if (current.truncated) {
            items.push(buildElement('span', { class: 'warn' }, 'stopped at the limit; narrow the search'));
        }
    }
    replaceContents(readout, ...items);
}
/**
 * Draws the table for a reply: folders first, then files, each group sorted the way the State
 * says. An empty reply gets a short message instead of an empty table.
 */
export function renderTable(grid, current, state, navigate) {
    const folders = sorted(current.folders, state);
    const files = sorted(current.files, state);
    if (folders.length + files.length === 0) {
        replaceContents(grid, buildElement('div', { class: 'empty' }, state.q ? 'Nothing matches.' : 'This folder is empty. Upload something, or make a folder.'));
        return;
    }
    const rows = [];
    for (const folder of folders) {
        rows.push(row(folder, true, state));
    }
    for (const file of files) {
        rows.push(row(file, false, state));
    }
    const table = buildElement('table', {}, buildElement('thead', {}, buildElement('tr', {}, header('Name', 'name', state, navigate), header('Size', 'size', state, navigate), header('Modified', 'modified', state, navigate), buildElement('th', { scope: 'col' }, buildElement('span', { class: 'visually-hidden' }, 'Actions')))), buildElement('tbody', {}, rows));
    replaceContents(grid, table);
}
/** Sorts entries by the State's column and direction. Search results sort by their full path. */
function sorted(entries, state) {
    const dir = state.dir === 'desc' ? -1 : 1;
    const key = (entry) => {
        if (state.sort === 'size') {
            return 'size_bytes' in entry ? entry.size_bytes : 0;
        }
        if (state.sort === 'modified') {
            return entry.modified_ms;
        }
        return (state.q ? entry.path : entry.name).toLowerCase();
    };
    return [...entries].sort((a, b) => {
        const ka = key(a);
        const kb = key(b);
        return (ka < kb ? -1 : ka > kb ? 1 : 0) * dir;
    });
}
/** A column heading that sorts by its column; clicking the sorted column again flips the direction. */
function header(label, sort, state, navigate) {
    const current = state.sort === sort;
    return buildElement('th', { scope: 'col', 'aria-sort': current ? (state.dir === 'desc' ? 'descending' : 'ascending') : false }, buildElement('button', { type: 'button', onclick: () => navigate({ sort, dir: current && state.dir === 'asc' ? 'desc' : 'asc' }) }, label));
}
/** One row: the name (a button for a folder, a download link for a file), size, date and actions. */
function row(entry, isFolder, state) {
    const label = state.q ? entry.path : entry.name;
    const name = isFolder
        ? buildElement('button', { type: 'button', 'data-action': 'open' }, buildElement('span', { class: 'kind', 'aria-hidden': 'true' }, '▸'), label)
        : buildElement('a', { href: api.downloadUrl(entry.path), download: entry.name }, buildElement('span', { class: 'kind', 'aria-hidden': 'true' }, ''), label);
    return buildElement('tr', { 'data-path': entry.path, 'data-kind': isFolder ? 'folder' : 'file' }, buildElement('td', { class: 'name' }, name), buildElement('td', { class: 'num' }, 'size_bytes' in entry ? bytes(entry.size_bytes) : ''), buildElement('td', { class: 'num' }, when(entry.modified_ms)), buildElement('td', { class: 'actions' }, isFolder ? null : buildElement('button', { type: 'button', 'data-action': 'download', 'aria-label': `Download ${entry.name}` }, 'Download'), buildElement('button', { type: 'button', 'data-action': 'copy', 'aria-label': `Copy ${entry.name}` }, 'Copy'), buildElement('button', { type: 'button', 'data-action': 'move', 'aria-label': `Move ${entry.name}` }, 'Move'), buildElement('button', { type: 'button', 'data-action': 'delete', class: 'danger', 'aria-label': `Delete ${entry.name}` }, 'Delete')));
}
