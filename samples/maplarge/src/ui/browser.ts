/**
 * The file browser shown inside the dialog: the breadcrumb trail, the search box,
 * the totals line, the file table, and the actions that change files (upload,
 * new folder, copy, move, delete).
 *
 * It draws only from the State it is given and the listing it fetched for that
 * State. Every change of folder, search or sort goes through navigate(), so the
 * page address always describes what is on screen.
 *
 * The parts live in files of their own: fileTable.ts draws the table, rowPrompts.ts
 * asks questions inside a row, uploads.ts sends files, and notices.ts shows what
 * happened. One click listener here handles the buttons of every row; each row
 * stores its path in a data-path attribute, so the listener finds which entry was
 * clicked without keeping a listener per row. (More in
 * docs/ADR-005-state-lives-in-the-url.md and docs/ADR-006-typescript-organised.md.)
 */

import * as api from '../lib/api.js';
import { crumbs, type Navigate, type State } from '../lib/urlState.js';
import { buildElement, replaceContents } from './elements.js';
import { renderReadout, renderTable, type Reply } from './fileTable.js';
import { createNotices, messageOf } from './notices.js';
import { askInRow, confirmInRow } from './rowPrompts.js';
import { createUploader } from './uploads.js';

const SEARCH_DEBOUNCE_MS = 250;

/** What each tab hands back: one render() that draws the tab for the State it is given. */
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

  // The browser's parts, top to bottom: the path and search box, the totals line, the notice
  // line, the table, and the row of buttons under it. The file picker stays hidden.
  const toolbar = buildElement('div', { class: 'toolbar' });
  const readout = buildElement('div', { class: 'readout', role: 'status', 'aria-live': 'polite' });
  const noticeBox = buildElement('div');
  const grid = buildElement('div', { class: 'grid' });
  const actions = buildElement('div', { class: 'actions-row' });
  const fileInput = buildElement('input', { type: 'file', multiple: true, class: 'visually-hidden', 'aria-label': 'Choose files to upload', onchange: () => void uploadFiles([...(fileInput.files ?? [])]) });
  replaceContents(root, toolbar, readout, noticeBox, grid, actions, fileInput);

  const say = createNotices(noticeBox);
  const uploadFiles = createUploader({ noticeBox, fileInput, folder: () => state.path, say, refresh: () => render(state) });

  // One listener answers every row's buttons, and dropping files on the table uploads them.
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
      if (next !== state) {
        return; // A newer render started during this fetch; an old failure must not cover it.
      }
      reply = null;
      replaceContents(readout);
      replaceContents(grid, buildElement('div', { class: 'empty' }, messageOf(error)));
      renderActions();
      return;
    }
    if (next !== state) {
      return; // A newer render started during this fetch; let the newer one draw.
    }
    renderReadout(readout, reply);
    renderTable(grid, reply, state, navigate);
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

  /** Draws the folder path as buttons, and keeps the search box in step with the address. */
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

  /** Draws the buttons under the table, and how long the server took to answer. */
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
        }, run);
        break;
      case 'move':
        askInRow(tr, 'Move to (path with the new name)', path, 'Move', async (to) => {
          await api.move(path, to);
          say('ok', `Moved ${path} to ${to}.`);
        }, run);
        break;
      case 'copy':
        askInRow(tr, 'Copy to (path with the new name)', suggestCopyName(path), 'Copy', async (to) => {
          await api.copy(path, to);
          say('ok', `Copied ${path} to ${to}.`);
        }, run);
        break;
      default:
        break;
    }
  }

  /** The name a copy starts with: report.csv becomes report-copy.csv. */
  function suggestCopyName(path: string): string {
    const dot = path.lastIndexOf('.');
    const slash = path.lastIndexOf('/');
    return dot > slash ? `${path.slice(0, dot)}-copy${path.slice(dot)}` : `${path}-copy`;
  }

  /** Runs a row action, then redraws; a failure puts the row back and shows the message. */
  async function run(act: () => Promise<void>, restore: () => void): Promise<void> {
    try {
      await act();
      await render(state);
    } catch (error) {
      restore();
      say('error', messageOf(error));
    }
  }

  /** Swaps the buttons under the table for a box that names a new folder in the folder on screen. */
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

  // #endregion writes

  return { render };
}
