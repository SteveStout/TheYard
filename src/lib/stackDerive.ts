/**
 * The home page's Tech stack table, derived from the repository's own files (ADR: Technology versions,
 * the addendum on the home page's table). deriveStack takes the files as text keyed by their path from
 * the root and returns the table: every version is read from a project file, package-lock.json, a
 * Dockerfile or the workflow folder, and a row whose proof is not in the repository is left out rather
 * than claimed. scripts/stack.mjs runs it over the disk and writes src/generated/stack.json;
 * techStack.test.ts runs it over the same files and fails when that file is stale. Imports only the
 * table's shape, stackLayers.json.
 */
import shape from './stackLayers.json' with { type: 'json' };

/** One row as the page draws it. */
export interface StackRow {
  tech: string;
  note: string | null;
  where: string;
  version: string;
  style: 'version' | 'service' | 'flag';
  proof: { label: string; href: string };
}

/** One layer: its label, the line under it, its colour's name and its rows. */
export interface StackLayer {
  key: string;
  label: string;
  sub: string;
  tone: string;
  rows: StackRow[];
}

/** The table: the headline chips and the layers. */
export interface Stack {
  chips: { figure: string; words: string }[];
  layers: StackLayer[];
}

/** The repository's files, as text keyed by their path from the root with forward slashes. */
export type Files = Record<string, string>;

/** Where a row's version is read from: exactly one of these is set. */
interface VersionSpec {
  framework?: string;
  words?: string;
  nuget?: [string, string];
  prefix?: string;
  npm?: string;
  npmPair?: [string, string];
  docker?: [string, string];
  workflows?: boolean;
  bicep?: boolean;
  text?: string;
}

interface RowSpec {
  tech: string;
  note?: string;
  also?: string;
  where: string;
  version: VersionSpec;
  proof: string;
  tree?: boolean;
  needs?: [string, string];
}

interface LayerSpec {
  key: string;
  label: string;
  sub: string;
  tone: string;
  rows: RowSpec[];
}

const REPO = 'https://github.com/SteveStout/TheYard';

/** The table's shape: what each row is and where it runs, as data in stackLayers.json. No version is written there. */
const LAYERS = shape as LayerSpec[];

/** The paths deriveStack reads, as globs from the repository's root. */
export const INPUTS: string[] = [
  'api/**/*.csproj',
  'functions/**/*.csproj',
  'package.json',
  'package-lock.json',
  'Dockerfile',
  'render/Dockerfile',
  'infra/*.bicep',
  'netlify.toml',
  '.github/workflows/*.yml',
];

const tag = (text: string, name: string): string | null =>
  (text.match(new RegExp(`<${name}>([^<]+)</${name}>`)) ?? [])[1] ?? null;
const twoParts = (version: string | null): string =>
  (version ?? '').split('.').slice(0, 2).join('.');

function packageVersion(files: Files, file: string, name: string): string | null {
  const text = files[file] ?? '';
  const escaped = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  return (
    (text.match(new RegExp(`<PackageReference\\s+Include="${escaped}"\\s+Version="([^"]+)"`)) ??
      [])[1] ?? null
  );
}

function npmVersion(files: Files, name: string): string | null {
  const lock = JSON.parse(files['package-lock.json'] ?? '{}') as {
    packages?: Record<string, { version?: string }>;
  };
  return lock.packages?.[`node_modules/${name}`]?.version ?? null;
}

/** One row's version: the words shown, and whether it is a version, a fact about a service, or a flag. */
function version(
  files: Files,
  spec: RowSpec,
  mainMajor: string | null
): { text: string; style: StackRow['style'] } | null {
  const v = spec.version;
  if (v.framework) {
    const framework = tag(files[v.framework] ?? '', 'TargetFramework');
    if (!framework) return null;
    const number = framework.replace(/^net/, '');
    const major = number.split('.')[0];
    const words = v.words ? `.NET ${major} ${v.words}` : number;
    return { text: words, style: mainMajor !== null && major !== mainMajor ? 'flag' : 'version' };
  }
  if (v.nuget) {
    const found = packageVersion(files, v.nuget[0], v.nuget[1]);
    return found ? { text: (v.prefix ?? '') + found, style: 'version' } : null;
  }
  if (v.npm) {
    const found = npmVersion(files, v.npm);
    return found ? { text: twoParts(found), style: 'version' } : null;
  }
  if (v.npmPair) {
    const [a, b] = v.npmPair.map((name) => npmVersion(files, name));
    return a && b ? { text: `${twoParts(a)} and ${twoParts(b)}`, style: 'version' } : null;
  }
  if (v.docker) {
    const docker = v.docker;
    const line = (files[docker[0]] ?? '')
      .split(/\r?\n/)
      .find((l) => new RegExp(`^FROM\\s+\\S*${docker[1]}:`).test(l));
    const found = line ? (line.match(new RegExp(`${docker[1]}:(\\d+)`)) ?? [])[1] : null;
    return found ? { text: found, style: 'version' } : null;
  }
  if (v.workflows) {
    const count = workflows(files).length;
    return count > 0 ? { text: `${count} workflows`, style: 'version' } : null;
  }
  if (v.bicep) {
    const count = Object.keys(files).filter((path) => /^infra\/[^/]+\.bicep$/.test(path)).length;
    return count > 0 ? { text: `${count} Bicep files`, style: 'service' } : null;
  }
  return { text: v.text ?? '', style: 'service' };
}

/** A proof's link text: the file's name, with its folder when the name alone is ambiguous (Dockerfile). */
const label = (proof: string, tree?: boolean): string => {
  const parts = proof.replace(/\/$/, '').split('/');
  const name = parts.pop();
  return (
    (name === 'Dockerfile' && parts.length > 0 ? `${parts.pop()}/${name}` : name) +
    (tree ? '/' : '')
  );
};

const workflows = (files: Files): string[] =>
  Object.keys(files)
    .filter((path) => /^\.github\/workflows\/[^/]+\.yml$/.test(path))
    .sort();

const present = (files: Files, path: string): boolean =>
  Object.keys(files).some(
    (name) => name === path || name.startsWith(path.endsWith('/') ? path : `${path}/`)
  );

/** The table, from the repository's files keyed by their path from the root with forward slashes. */
export function deriveStack(files: Files): Stack {
  const main = tag(files['api/TheYard.Api/TheYard.Api.csproj'] ?? '', 'TargetFramework');
  const mainMajor = main ? main.replace(/^net/, '').split('.')[0] : null;
  const layers = LAYERS.map((layer) => ({
    key: layer.key,
    label: layer.label,
    sub: layer.sub,
    tone: layer.tone,
    rows: layer.rows
      .filter(
        (row) =>
          !row.needs ||
          (row.needs[0] in files && (files[row.needs[0]] ?? '').includes(row.needs[1]))
      )
      .filter((row) => (row.tree ? present(files, row.proof) : row.proof in files))
      .map((row) => {
        const v = version(files, row, mainMajor);
        if (!v) return null;
        const names = row.version.workflows
          ? workflows(files).map((path) => (path.split('/').pop() ?? '').replace(/\.yml$/, ''))
          : null;
        return {
          tech: row.tech + (row.also ? ` ${row.also}` : ''),
          note: names ? names.join(', ') : (row.note ?? null),
          where: row.where,
          version: v.text,
          style: v.style,
          proof: {
            label: label(row.proof, row.tree),
            href: `${REPO}/${row.tree ? 'tree' : 'blob'}/main/${row.proof.replace(/\/$/, '')}`,
          },
        };
      })
      .filter((row): row is StackRow => row !== null),
  })).filter((layer) => layer.rows.length > 0);
  const projects = Object.keys(files).filter(
    (path) => path.endsWith('.csproj') && tag(files[path] ?? '', 'TargetFramework') === main
  ).length;
  const react = npmVersion(files, 'react');
  const ts = npmVersion(files, 'typescript');
  const chips = [
    { figure: `.NET ${mainMajor}`, words: `${projects} projects` },
    { figure: `React ${twoParts(react)}`, words: `TypeScript ${twoParts(ts)}` },
    {
      figure: 'Azure',
      words: `${Object.keys(files).filter((p) => /^infra\/[^/]+\.bicep$/.test(p)).length} Bicep files`,
    },
    { figure: String(workflows(files).length), words: 'GitHub Actions workflows' },
  ];
  return { chips, layers };
}

/** The file as written: two-space JSON and a final line break, so the test compares text with text. */
export const stackText = (files: Files): string =>
  `${JSON.stringify(deriveStack(files), null, 2)}\n`;
