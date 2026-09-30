/**
 * The file browser shown inside the dialog: the breadcrumb trail, the search box,
 * the totals line, the file table, and the actions that change files (upload,
 * new folder, copy, move, delete).
 *
 * It draws only from the State it is given and the listing it fetched for that
 * State. Every change of folder, search or sort goes through navigate(), so the
 * page address always describes what is on screen.
 *
 * One click listener on the table handles the buttons of every row. Each row
 * stores its path in a data-path attribute, so the listener finds which entry was
 * clicked without keeping a listener per row. (More in
 * docs/ADR-005-state-lives-in-the-url.md and docs/ADR-006-typescript-organised.md.)
 */

import * as api from '../lib/api.js';
import { bytes, plural, when } from '../lib/format.js';
import { ApiError, type FileEntry, type FolderEntry, type Listing, type SearchResult } from '../lib/types.js';
import { crumbs, type Navigate, type Sort, type State } from '../lib/urlState.js';
import { buildElement, replaceContents } from './elements.js';

const SEARCH_DEBOUNCE_MS = 250;

type Reply = Listing | SearchResult;
type Entry = FolderEntry | FileEntry;

function isSearch(reply: Reply): reply is SearchResult {
  return 'query' in reply;
}

/**
 * The message to show for a caught error: the server's sentence for an ApiError,
 * the browser's message for any other Error, or the value as text otherwise.
 */
function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export interface View {
  render(state: State): Promise<void>;
}

/**
 * Builds the file browser inside a container element and returns an object whose
 * render() draws it for a given State.
 * @param root the element the browser fills; it replaces everything inside it
 * @param navigate the function to call to change the State and the page address
 */
export function createBrowser(root: HTMLElement, navigate: Navigate): View {
  let state: State = { open: true, view: 'browse', path: '', q: '', sort: 'name', dir: 'asc', doc: '' };
  let reply: Reply | null = null;
  let debounce = 0;

  const toolbar = buildElement('div', { class: 'toolbar' });
  const readout = buildElement('div', { class: 'readout', role: 'status', 'aria-live': 'polite' });
  const noticeBox = buildElement('div');
  const grid = buildElement('div', { class: 'grid' });
  const actions = buildElement('div', { class: 'actions-row' });
  const fileInput = buildElement('input', { type: 'file', multiple: true, class: 'visually-hidden', 'aria-label': 'Choose files to upload', onchange: () => void uploadFiles([...(fileInput.files ?? [])]) });
  replaceContents(root, toolbar, readout, noticeBox, grid, actions, fileInput);

  grid.addEventListener('click', onRowAction);
  grid.addEventListener('dragover', (event) => {
    event.preventDefault();
    grid.classList.add('dropping');
  });
  grid.addEventListener('dragleave', () => grid.classList.remove('dropping'));
  grid.addEventListener('drop', (event) => {
    event.preventDefault();
    grid.classList.remove('dropping');
    const transfer = event.dataTransfer;
    if (!transfer) {
      return;
    }
    // A dropped folder shows up in dataTransfer.files as an empty entry with the folder's name,
    // so folders are detected through the item list and left out of the upload with a message.
    const folders = [...transfer.items].filter((item) => item.webkitGetAsEntry()?.isDirectory);
    if (folders.length > 0) {
      say('error', 'Drop files, not folders: make the folder here first, then drop its files into it.');
    }
    void uploadFiles([...transfer.files].filter((file) => !folders.some((item) => item.getAsFile()?.name === file.name)));
  });

  // #region render
  /**
   * Draws the browser for a State. It fetches the folder listing, or the search
   * results when the State has a search, then draws the totals, the table and
   * the action buttons from that reply. A failed fetch shows the error message
   * in place of the table.
   */
  async function render(next: State): Promise<void> {
    state = next;
    renderToolbar();
    replaceContents(grid, buildElement('div', { class: 'empty' }, 'Loading'));
    try {
      reply = state.q ? await api.search(state.path, state.q) : await api.browse(state.path);
    } catch (error) {
      reply = null;
      replaceContents(readout);
      replaceContents(grid, buildElement('div', { class: 'empty' }, messageOf(error)));
      renderActions();
      return;
    }
    if (next !== state) {
      return; // A newer render started during this fetch; let the newer one draw.
    }
    renderReadout(reply);
    renderTable(reply);
    renderActions();
  }

  // The search box is created once and reused on every render. Rebuilding it would move the
  // keyboard focus and the text cursor away from a person who is still typing.
  const searchInput = buildElement('input', { type: 'search', name: 'q', placeholder: 'Search this folder and below: name, *.md, report?', 'aria-label': 'Search' });
  searchInput.addEventListener('input', () => {
    clearTimeout(debounce);
    // Wait until typing pauses, then search. The first search adds a history entry, so Back
    // returns to the folder without a search. Later keystrokes overwrite that entry, so Back
    // does not step back through the search one letter at a time.
    debounce = window.setTimeout(() => navigate({ q: searchInput.value.trim() }, state.q !== ''), SEARCH_DEBOUNCE_MS);
  });
  const clearButton = buildElement('button', { type: 'button', class: 'small', onclick: () => navigate({ q: '' }) }, 'Clear');
  const searchForm = buildElement('form', {
    onsubmit: (event: Event) => {
      event.preventDefault();
      clearTimeout(debounce);
      navigate({ q: searchInput.value.trim() });
    },
  }, searchInput, buildElement('button', { type: 'submit', class: 'small' }, 'Search'), clearButton);
  const trail = buildElement('nav', { class: 'crumbs', 'aria-label': 'Folder path' });
  replaceContents(toolbar, trail, searchForm);

  function renderToolbar(): void {
    const parts = crumbs(state.path);
    const nodes: Node[] = [];
    parts.forEach((crumb, index) => {
      const last = index === parts.length - 1;
      if (index > 0) {
        nodes.push(buildElement('span', { class: 'sep', 'aria-hidden': 'true' }, '/'));
      }
      nodes.push(last && !state.q
        ? buildElement('span', { class: 'here', 'aria-current': 'location' }, crumb.name)
        : buildElement('button', { type: 'button', onclick: () => navigate({ path: crumb.path, q: '' }) }, crumb.name));
    });
    replaceContents(trail, ...nodes);
    if (document.activeElement !== searchInput) {
      searchInput.value = state.q;
    }
    clearButton.hidden = !state.q;
  }

  function renderReadout(current: Reply): void {
    const totals = current.totals;
    const items = [
      buildElement('span', {}, buildElement('strong', {}, totals.folder_count.toLocaleString()), plural(totals.folder_count, 'folder').replace(/^\S+ /, '')),
      buildElement('span', {}, buildElement('strong', {}, totals.file_count.toLocaleString()), plural(totals.file_count, 'file').replace(/^\S+ /, '')),
      buildElement('span', {}, buildElement('strong', {}, bytes(totals.total_bytes)), 'in files'),
    ];
    if (isSearch(current)) {
      items.unshift(buildElement('span', {}, buildElement('strong', {}, 'search'), `for ${current.query}`));
      if (current.truncated) {
        items.push(buildElement('span', { class: 'warn' }, 'stopped at the limit; narrow the search'));
      }
    }
    replaceContents(readout, ...items);
  }

  function sorted<T extends Entry>(entries: T[]): T[] {
    const dir = state.dir === 'desc' ? -1 : 1;
    const key = (entry: T): string | number => {
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

  function header(label: string, sort: Sort): HTMLTableCellElement {
    const current = state.sort === sort;
    return buildElement('th', { scope: 'col', 'aria-sort': current ? (state.dir === 'desc' ? 'descending' : 'ascending') : false },
      buildElement('button', { type: 'button', onclick: () => navigate({ sort, dir: current && state.dir === 'asc' ? 'desc' : 'asc' }) }, label));
  }

  function renderTable(current: Reply): void {
    const folders = sorted(current.folders);
    const files = sorted(current.files);
    if (folders.length + files.length === 0) {
      replaceContents(grid, buildElement('div', { class: 'empty' }, state.q ? 'Nothing matches.' : 'This folder is empty. Upload something, or make a folder.'));
      return;
    }
    const rows: Node[] = [];
    for (const folder of folders) {
      rows.push(row(folder, true));
    }
    for (const file of files) {
      rows.push(row(file, false));
    }
    const table = buildElement('table', {},
      buildElement('thead', {}, buildElement('tr', {}, header('Name', 'name'), header('Size', 'size'), header('Modified', 'modified'), buildElement('th', { scope: 'col' }, buildElement('span', { class: 'visually-hidden' }, 'Actions')))),
      buildElement('tbody', {}, rows));
    replaceContents(grid, table);
  }

  function row(entry: Entry, isFolder: boolean): HTMLTableRowElement {
    const label = state.q ? entry.path : entry.name;
    const name = isFolder
      ? buildElement('button', { type: 'button', 'data-action': 'open' }, buildElement('span', { class: 'kind', 'aria-hidden': 'true' }, '▸'), label)
      : buildElement('a', { href: api.downloadUrl(entry.path), download: entry.name }, buildElement('span', { class: 'kind', 'aria-hidden': 'true' }, '•'), label);
    return buildElement('tr', { 'data-path': entry.path, 'data-kind': isFolder ? 'folder' : 'file' },
      buildElement('td', { class: 'name' }, name),
      buildElement('td', { class: 'num' }, 'size_bytes' in entry ? bytes(entry.size_bytes) : ''),
      buildElement('td', { class: 'num' }, when(entry.modified_ms)),
      buildElement('td', { class: 'actions' },
        isFolder ? null : buildElement('button', { type: 'button', 'data-action': 'download', 'aria-label': `Download ${entry.name}` }, 'Download'),
        buildElement('button', { type: 'button', 'data-action': 'copy', 'aria-label': `Copy ${entry.name}` }, 'Copy'),
        buildElement('button', { type: 'button', 'data-action': 'move', 'aria-label': `Move ${entry.name}` }, 'Move'),
        buildElement('button', { type: 'button', 'data-action': 'delete', class: 'danger', 'aria-label': `Delete ${entry.name}` }, 'Delete')));
  }

  function renderActions(): void {
    const took = reply ? buildElement('span', { class: 'took' }, `served in ${reply.took_ms} ms`) : null;
    replaceContents(actions,
      state.q ? null : buildElement('button', { type: 'button', class: 'primary', onclick: () => fileInput.click() }, 'Upload files'),
      state.q ? null : buildElement('button', { type: 'button', onclick: newFolder }, 'New folder'),
      took);
  }
  // #endregion render

  // #region writes
  /**
   * Handles a click on any row button. The button's data-action attribute names
   * the action, and the row's data-path attribute names the file or folder.
   */
  function onRowAction(event: Event): void {
    const button = (event.target as Element | null)?.closest<HTMLButtonElement>('button[data-action]');
    const tr = button?.closest('tr');
    if (!button || !tr) {
      return;
    }
    const path = tr.dataset['path'] ?? '';
    const isFolder = tr.dataset['kind'] === 'folder';
    switch (button.dataset['action']) {
      case 'open':
        navigate({ path, q: '' });
        break;
      case 'download':
        window.location.assign(api.downloadUrl(path));
        break;
      case 'delete':
        confirmInRow(tr, `Delete ${isFolder ? 'folder' : 'file'} "${path}"${isFolder ? ' and everything in it' : ''}?`, 'Delete', async () => {
          await api.remove(path);
          say('ok', `Deleted ${path}.`);
        });
        break;
      case 'move':
        askInRow(tr, 'Move to (path with the new name)', path, 'Move', async (to) => {
          await api.move(path, to);
          say('ok', `Moved ${path} to ${to}.`);
        });
        break;
      case 'copy':
        askInRow(tr, 'Copy to (path with the new name)', suggestCopyName(path), 'Copy', async (to) => {
          await api.copy(path, to);
          say('ok', `Copied ${path} to ${to}.`);
        });
        break;
      default:
        break;
    }
  }

  function suggestCopyName(path: string): string {
    const dot = path.lastIndexOf('.');
    const slash = path.lastIndexOf('/');
    return dot > slash ? `${path.slice(0, dot)}-copy${path.slice(dot)}` : `${path}-copy`;
  }

  /**
   * Replaces a row's action buttons with a question, a confirm button and a
   * Cancel button. Cancel, or a failed action, puts the original buttons back.
   * Asking inside the row keeps the question next to the entry it is about.
   */
  function confirmInRow(tr: HTMLTableRowElement, question: string, verb: string, act: () => Promise<void>): void {
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
   * Replaces a row's action buttons with a text box, a confirm button and a
   * Cancel button, for actions that need a destination path (move and copy).
   * Cancel, or a failed action, puts the original buttons back.
   */
  function askInRow(tr: HTMLTableRowElement, label: string, value: string, verb: string, act: (to: string) => Promise<void>): void {
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

  async function run(act: () => Promise<void>, restore: () => void): Promise<void> {
    try {
      await act();
      await render(state);
    } catch (error) {
      restore();
      say('error', messageOf(error));
    }
  }

  function newFolder(): void {
    const input = buildElement('input', { type: 'text', placeholder: 'Folder name', 'aria-label': 'New folder name' });
    const form = buildElement('form', {
      onsubmit: (event: Event) => {
        event.preventDefault();
        void (async () => {
          try {
            await api.createFolder(state.path, input.value.trim());
            say('ok', `Made folder ${input.value.trim()}.`);
            await render(state);
          } catch (error) {
            say('error', messageOf(error));
          }
        })();
      },
    }, input, ' ', buildElement('button', { type: 'submit', class: 'small' }, 'Create'), ' ', buildElement('button', { type: 'button', class: 'small', onclick: () => renderActions() }, 'Cancel'));
    replaceContents(actions, form);
    input.focus();
  }

  /**
   * Uploads files one at a time, each with its own progress bar. When the server
   * answers 409 (a file with that name already exists), the batch pauses and asks
   * the person. Overwrite sends that file again with overwrite turned on; Skip
   * leaves it. Either way the remaining files continue. Any other error stops the
   * batch and shows the message.
   */
  async function uploadFiles(files: File[]): Promise<void> {
    if (files.length === 0) {
      return;
    }
    for (const file of files) {
      try {
        await uploadOne(file, false);
      } catch (error) {
        if (!(error instanceof ApiError) || error.status !== 409) {
          say('error', messageOf(error));
          return;
        }
        const overwrite = await askOverwrite(error.message);
        if (overwrite) {
          try {
            await uploadOne(file, true);
          } catch (again) {
            say('error', messageOf(again));
            return;
          }
        }
      }
    }
    fileInput.value = '';
    say('ok', files.length === 1 ? `Uploaded ${files[0]?.name ?? ''}.` : `Uploaded ${files.length} files.`);
    await render(state);
  }

  function uploadOne(file: File, overwrite: boolean): Promise<unknown> {
    const bar = buildElement('div', {});
    replaceContents(noticeBox, buildElement('div', { class: 'notice' }, `Uploading ${file.name} (${bytes(file.size)})`, buildElement('div', { class: 'progress' }, bar)));
    return api.upload(state.path, file, overwrite, (fraction) => { bar.style.width = `${Math.round(fraction * 100)}%`; });
  }

  /**
   * Shows the "name already exists" question with Overwrite and Skip buttons, and
   * returns a promise that resolves to true for Overwrite or false for Skip, so
   * the upload loop can wait for the answer.
   */
  function askOverwrite(message: string): Promise<boolean> {
    return new Promise((resolve) => {
      replaceContents(noticeBox, buildElement('div', { class: 'notice' }, `${message} `,
        buildElement('button', { type: 'button', class: 'small', onclick: () => resolve(true) }, 'Overwrite'), ' ',
        buildElement('button', { type: 'button', class: 'small', onclick: () => resolve(false) }, 'Skip')));
    });
  }

  function say(kind: 'ok' | 'error', text: string): void {
    const notice = buildElement('div', { class: `notice ${kind}`, role: 'status' }, text, ' ', buildElement('button', { type: 'button', class: 'small', onclick: () => replaceContents(noticeBox) }, 'Dismiss'));
    replaceContents(noticeBox, notice);
  }
  // #endregion writes

  return { render };
}
