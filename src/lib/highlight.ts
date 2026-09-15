/**
 * Syntax highlighting for the code inside every served document.
 *
 * The documents render through `marked`, which hands a fenced block back as
 * plain text, so until now a C# sample on the site was the same color as the
 * sentence above it. highlight.js does the tokenizing; the colors are this
 * site's own tokens rather than a stock theme, and `src/styles/code.css`
 * holds them.
 *
 * Only the languages the documents actually use are registered, counted
 * across every document the catalog serves before this list was written: C#
 * by a distance, then TypeScript and TSX, YAML, JSON, CSS, SQL, XML, Python,
 * a Dockerfile, PowerShell, and the ini shape `.editorconfig` and a TOML file share.
 * A fence in a language nobody registered, Bicep and Mermaid being the two
 * here, renders as escaped text and not as an error.
 */
import hljs from 'highlight.js/lib/core';
import bash from 'highlight.js/lib/languages/bash';
import csharp from 'highlight.js/lib/languages/csharp';
import css from 'highlight.js/lib/languages/css';
import dockerfile from 'highlight.js/lib/languages/dockerfile';
import ini from 'highlight.js/lib/languages/ini';
import javascript from 'highlight.js/lib/languages/javascript';
import json from 'highlight.js/lib/languages/json';
import markdown from 'highlight.js/lib/languages/markdown';
import plaintext from 'highlight.js/lib/languages/plaintext';
import powershell from 'highlight.js/lib/languages/powershell';
import python from 'highlight.js/lib/languages/python';
import sql from 'highlight.js/lib/languages/sql';
import typescript from 'highlight.js/lib/languages/typescript';
import xml from 'highlight.js/lib/languages/xml';
import yaml from 'highlight.js/lib/languages/yaml';

// #region languages
/**
 * The name on a fence, to the grammar that reads it. The keys are what the
 * documents write and what the live-sample expander emits from a file
 * extension, so `ts`, `tsx` and `typescript` all have to land on the same
 * grammar rather than one of them quietly falling through to plain text.
 */
const LANGUAGES = {
  bash,
  csharp,
  css,
  dockerfile,
  ini,
  javascript,
  json,
  markdown,
  plaintext,
  powershell,
  python,
  sql,
  typescript,
  xml,
  yaml,
} as const;

const ALIASES: Record<string, keyof typeof LANGUAGES> = {
  'c#': 'csharp',
  cs: 'csharp',
  html: 'xml',
  js: 'javascript',
  mjs: 'javascript',
  md: 'markdown',
  py: 'python',
  ps1: 'powershell',
  sh: 'bash',
  shell: 'bash',
  text: 'plaintext',
  toml: 'ini',
  ts: 'typescript',
  tsx: 'typescript',
  yml: 'yaml',
};

let registered = false;

function register(): void {
  if (registered) return;
  for (const [name, grammar] of Object.entries(LANGUAGES)) {
    hljs.registerLanguage(name, grammar);
  }
  registered = true;
}
// #endregion languages

// #region highlight
/** The five characters that turn code into markup if they reach the page unescaped. */
export function escapeHtml(text: string): string {
  return text
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

/** The grammar a fence names, or null when nothing here reads that language. */
export function grammarFor(language: string | undefined): string | null {
  const name = (language ?? '').trim().toLowerCase();
  if (!name) return null;
  const resolved = ALIASES[name] ?? name;
  register();
  return hljs.getLanguage(resolved) ? resolved : null;
}

/**
 * The code as HTML: tokenized when the language is one of ours, escaped and
 * nothing else when it is not. `ignoreIllegals` matters because a record shows
 * a region of a file rather than a whole one, and a fragment that starts
 * mid-class is not a parse error worth a blank block.
 */
export function highlight(code: string, language: string | undefined): string {
  const grammar = grammarFor(language);
  if (!grammar) return escapeHtml(code);
  return hljs.highlight(code, { language: grammar, ignoreIllegals: true }).value;
}
// #endregion highlight
