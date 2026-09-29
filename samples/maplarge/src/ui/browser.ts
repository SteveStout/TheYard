// The file browser inside the dialog: the breadcrumb, the search, the totals,
// the table and the writes (ADR-006). It renders from the state it is given and
// the reply it fetched, and every change of view goes through navigate(), so
// the URL is always the state (ADR-005). One listener on the table handles every
// row; a row is found again by its data-path.

import * as api from '../lib/api.js';
import { bytes, plural, when } from '../lib/format.js';
import { ApiError, type FileEntry, type FolderEntry, type Listing, type SearchResult } from '../lib/types.js';
import { crumbs, type Navigate, type Sort, type State } from '../lib/urlState.js';
import { h, replace } from './dom.js';

const SEARCH_DEBOUNCE_MS = 250;

type Reply = Listing | SearchResult;
type Entry = FolderEntry | FileEntry;

function isSearch(reply: Reply): reply is SearchResult {
  return 'query' in reply;
}

/** The sentence a failure carries: the server's, or the browser's. */
function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export interface View {
  render(state: State): Promise<void>;
}

/**
 * @param root the element the browser owns
 * @param navigate pushes a state change, or replaces the current one
 */
export function createBrowser(root: HTMLElement, navigate: Navigate): View {
  let state: State = { open: true, view: 'browse', path: '', q: '', sort: 'name', dir: 'asc', doc: '' };
  let reply: Reply | null = null;
  let debounce = 0;

  const toolbar = h('div', { class: 'toolbar' });
  const readout = h('div', { class: 'readout', role: 'status', 'aria-live': 'polite' });
  const noticeBox = h('div');
  const grid = h('div', { class: 'grid' });
  const actions = h('div', { class: 'actions-row' });
  const fileInput = h('input', { type: 'file', multiple: true, class: 'visually-hidden', 'aria-label': 'Choose files to upload', onchange: () => void uploadFiles([...(fileInput.files ?? [])]) });
  replace(root, toolbar, readout, noticeBox, grid, actions, fileInput);

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
    // A dropped folder arrives as an empty entry: dataTransfer.files holds files only.
    const folders = [...transfer.items].filter((item) => item.webkitGetAsEntry()?.isDirectory);
    if (folders.length > 0) {
      say('error', 'Drop files, not folders: make the folder here first, then drop its files into it.');
    }
    void uploadFiles([...transfer.files].filter((file) => !folders.some((item) => item.getAsFile()?.name === file.name)));
  });

  // #region render
  /** Renders the state: a browse or a search, then the table from the reply. */
  async function render(next: State): Promise<void> {
    state = next;
    renderToolbar();
    replace(grid, h('div', { class: 'empty' }, 'Loading'));
    try {
      reply = state.q ? await api.search(state.path, state.q) : await api.browse(state.path);
    } catch (error) {
      reply = null;
      replace(readout);
      replace(grid, h('div', { class: 'empty' }, messageOf(error)));
      renderActions();
      return;
    }
    if (next !== state) {
      return; // A newer render started while this one was fetching; it will draw.
    }
    renderReadout(reply);
    renderTable(reply);
    renderActions();
  }

  // The search box is made once and kept, so re-rendering while a person types
  // never takes the focus or the caret away from them.
  const searchInput = h('input', { type: 'search', name: 'q', placeholder: 'Search this folder and below: name, *.md, report?', 'aria-label': 'Search' });
  searchInput.addEventListener('input', () => {
    clearTimeout(debounce);
    // The first keystroke of a search pushes, so Back returns to the folder without it; every
    // keystroke after that replaces, so Back never steps through a search letter by letter.
    debounce = window.setTimeout(() => navigate({ q: searchInput.value.trim() }, state.q !== ''), SEARCH_DEBOUNCE_MS);
  });
  const clearButton = h('button', { type: 'button', class: 'small', onclick: () => navigate({ q: '' }) }, 'Clear');
  const searchForm = h('form', {
    onsubmit: (event: Event) => {
      event.preventDefault();
      clearTimeout(debounce);
      navigate({ q: searchInput.value.trim() });
    },
  }, searchInput, h('button', { type: 'submit', class: 'small' }, 'Search'), clearButton);
  const trail = h('nav', { class: 'crumbs', 'aria-label': 'Folder path' });
  replace(toolbar, trail, searchForm);

  function renderToolbar(): void {
    const parts = crumbs(state.path);
    const nodes: Node[] = [];
    parts.forEach((crumb, index) => {
      const last = index === parts.length - 1;
      if (index > 0) {
        nodes.push(h('span', { class: 'sep', 'aria-hidden': 'true' }, '/'));
      }
      nodes.push(last && !state.q
        ? h('span', { class: 'here', 'aria-current': 'location' }, crumb.name)
        : h('button', { type: 'button', onclick: () => navigate({ path: crumb.path, q: '' }) }, crumb.name));
    });
    replace(trail, ...nodes);
    if (document.activeElement !== searchInput) {
      searchInput.value = state.q;
    }
    clearButton.hidden = !state.q;
  }

  function renderReadout(current: Reply): void {
    const totals = current.totals;
    const items = [
      h('span', {}, h('strong', {}, totals.folder_count.toLocaleString()), plural(totals.folder_count, 'folder').replace(/^\S+ /, '')),
      h('span', {}, h('strong', {}, totals.file_count.toLocaleString()), plural(totals.file_count, 'file').replace(/^\S+ /, '')),
      h('span', {}, h('strong', {}, bytes(totals.total_bytes)), 'in files'),
    ];
    if (isSearch(current)) {
      items.unshift(h('span', {}, h('strong', {}, 'search'), `for ${current.query}`));
      if (current.truncated) {
        items.push(h('span', { class: 'warn' }, 'stopped at the limit; narrow the search'));
      }
    }
    replace(readout, ...items);
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
    return h('th', { scope: 'col', 'aria-sort': current ? (state.dir === 'desc' ? 'descending' : 'ascending') : false },
      h('button', { type: 'button', onclick: () => navigate({ sort, dir: current && state.dir === 'asc' ? 'desc' : 'asc' }) }, label));
  }

  function renderTable(current: Reply): void {
    const folders = sorted(current.folders);
    const files = sorted(current.files);
    if (folders.length + files.length === 0) {
      replace(grid, h('div', { class: 'empty' }, state.q ? 'Nothing matches.' : 'This folder is empty. Upload something, or make a folder.'));
      return;
    }
    const rows: Node[] = [];
    for (const folder of folders) {
      rows.push(row(folder, true));
    }
    for (const file of files) {
      rows.push(row(file, false));
    }
    const table = h('table', {},
      h('thead', {}, h('tr', {}, header('Name', 'name'), header('Size', 'size'), header('Modified', 'modified'), h('th', { scope: 'col' }, h('span', { class: 'visually-hidden' }, 'Actions')))),
      h('tbody', {}, rows));
    replace(grid, table);
  }

  function row(entry: Entry, isFolder: boolean): HTMLTableRowElement {
    const label = state.q ? entry.path : entry.name;
    const name = isFolder
      ? h('button', { type: 'button', 'data-action': 'open' }, h('span', { class: 'kind', 'aria-hidden': 'true' }, '▸'), label)
      : h('a', { href: api.downloadUrl(entry.path), download: entry.name }, h('span', { class: 'kind', 'aria-hidden': 'true' }, '•'), label);
    return h('tr', { 'data-path': entry.path, 'data-kind': isFolder ? 'folder' : 'file' },
      h('td', { class: 'name' }, name),
      h('td', { class: 'num' }, 'size_bytes' in entry ? bytes(entry.size_bytes) : ''),
      h('td', { class: 'num' }, when(entry.modified_ms)),
      h('td', { class: 'actions' },
        isFolder ? null : h('button', { type: 'button', 'data-action': 'download', 'aria-label': `Download ${entry.name}` }, 'Download'),
        h('button', { type: 'button', 'data-action': 'copy', 'aria-label': `Copy ${entry.name}` }, 'Copy'),
        h('button', { type: 'button', 'data-action': 'move', 'aria-label': `Move ${entry.name}` }, 'Move'),
        h('button', { type: 'button', 'data-action': 'delete', class: 'danger', 'aria-label': `Delete ${entry.name}` }, 'Delete')));
  }

  function renderActions(): void {
    const took = reply ? h('span', { class: 'took' }, `served in ${reply.took_ms} ms`) : null;
    replace(actions,
      state.q ? null : h('button', { type: 'button', class: 'primary', onclick: () => fileInput.click() }, 'Upload files'),
      state.q ? null : h('button', { type: 'button', onclick: newFolder }, 'New folder'),
      took);
  }
  // #endregion render

  // #region writes
  /** One listener for every row: the button's data-action says what, the row's data-path says which. */
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

  /** Swaps a row's action cell for a question with one confirming button. */
  function confirmInRow(tr: HTMLTableRowElement, question: string, verb: string, act: () => Promise<void>): void {
    const cell = tr.querySelector('td.actions');
    if (!cell) {
      return;
    }
    const previous = [...cell.childNodes];
    const restore = (): void => replace(cell, ...previous);
    const confirm = h('button', { type: 'button', class: 'danger', onclick: () => void run(act, restore) }, verb);
    replace(cell, h('span', {}, question, ' '), confirm, h('button', { type: 'button', onclick: restore }, 'Cancel'));
    confirm.focus();
  }

  /** Swaps a row's action cell for an input and a confirming button. */
  function askInRow(tr: HTMLTableRowElement, label: string, value: string, verb: string, act: (to: string) => Promise<void>): void {
    const cell = tr.querySelector('td.actions');
    if (!cell) {
      return;
    }
    const previous = [...cell.childNodes];
    const restore = (): void => replace(cell, ...previous);
    const input = h('input', { type: 'text', value, 'aria-label': label, size: '32' });
    const form = h('form', { onsubmit: (event: Event) => { event.preventDefault(); void run(() => act(input.value.trim()), restore); } },
      input, ' ', h('button', { type: 'submit' }, verb), ' ', h('button', { type: 'button', onclick: restore }, 'Cancel'));
    replace(cell, form);
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
    const input = h('input', { type: 'text', placeholder: 'Folder name', 'aria-label': 'New folder name' });
    const form = h('form', {
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
    }, input, ' ', h('button', { type: 'submit', class: 'small' }, 'Create'), ' ', h('button', { type: 'button', class: 'small', onclick: () => renderActions() }, 'Cancel'));
    replace(actions, form);
    input.focus();
  }

  /**
   * Uploads files one at a time with a bar each. A 409 (the name exists) pauses the
   * batch and asks in place: Overwrite sends that one file again with overwrite on,
   * Skip leaves it; either way the rest of the batch goes on.
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
    const bar = h('div', {});
    replace(noticeBox, h('div', { class: 'notice' }, `Uploading ${file.name} (${bytes(file.size)})`, h('div', { class: 'progress' }, bar)));
    return api.upload(state.path, file, overwrite, (fraction) => { bar.style.width = `${Math.round(fraction * 100)}%`; });
  }

  /** The 409 question, as a promise the batch can wait on. */
  function askOverwrite(message: string): Promise<boolean> {
    return new Promise((resolve) => {
      replace(noticeBox, h('div', { class: 'notice' }, `${message} `,
        h('button', { type: 'button', class: 'small', onclick: () => resolve(true) }, 'Overwrite'), ' ',
        h('button', { type: 'button', class: 'small', onclick: () => resolve(false) }, 'Skip')));
    });
  }

  function say(kind: 'ok' | 'error', text: string): void {
    const notice = h('div', { class: `notice ${kind}`, role: 'status' }, text, ' ', h('button', { type: 'button', class: 'small', onclick: () => replace(noticeBox) }, 'Dismiss'));
    replace(noticeBox, notice);
  }
  // #endregion writes

  return { render };
}
