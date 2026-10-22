import { describe, expect, it } from 'vitest';
import { deriveStack, stackText } from './stackDerive';
import committed from '../generated/stack.json?raw';
import { STACK } from './techStack';

// The repository's own files, read as text by the bundler the way icons.test.ts reads the components
// (this project carries no Node types): the project files, the lock file, the Dockerfiles, the Bicep
// and the workflows that scripts/stack.mjs reads. Keys become paths from the root.
const raw = import.meta.glob(
  [
    '../../api/**/*.csproj',
    '../../functions/**/*.csproj',
    '../../package.json',
    '../../package-lock.json',
    '../../Dockerfile',
    '../../render/Dockerfile',
    '../../infra/*.bicep',
    '../../netlify.toml',
    '../../.github/workflows/*.yml',
  ],
  { query: '?raw', import: 'default', eager: true }
) as Record<string, string>;
const files = Object.fromEntries(
  Object.entries(raw).map(([path, text]) => [path.replace(/^\.\.\/\.\.\//, ''), text])
);
const derived = deriveStack(files);

describe('the Tech stack table', () => {
  it('reads every input it needs from the repository', () => {
    expect(Object.keys(files)).toContain('api/TheYard.Api/TheYard.Api.csproj');
    expect(Object.keys(files)).toContain('package-lock.json');
    expect(Object.keys(files).filter((path) => path.startsWith('.github/workflows/'))).not.toEqual(
      []
    );
  });

  it('is the table the repository gives: run node scripts/stack.mjs when this fails', () => {
    expect(committed.replace(/\r\n/g, '\n')).toBe(stackText(files));
  });

  it.each(derived.layers.map((layer) => [layer.label, layer] as const))(
    'the %s rows match the repository',
    (_label, layer) => {
      expect(STACK.layers.find((kept) => kept.key === layer.key)).toEqual(layer);
    }
  );

  it('the headline chips match the repository', () => {
    expect(STACK.chips).toEqual(derived.chips);
  });

  it('links every row to a file or folder in this repository', () => {
    for (const row of STACK.layers.flatMap((layer) => layer.rows)) {
      const path = row.proof.href.replace(
        /^https:\/\/github\.com\/SteveStout\/TheYard\/(blob|tree)\/main\//,
        ''
      );
      expect(Object.keys(files).some((name) => name === path || name.startsWith(`${path}/`))).toBe(
        true
      );
    }
  });

  it('flags a piece on an older .NET than the site, and only that', () => {
    const site = (/<TargetFramework>net(\d+)/.exec(files['api/TheYard.Api/TheYard.Api.csproj']) ??
      [])[1];
    const flagged = STACK.layers
      .flatMap((layer) => layer.rows)
      .filter((row) => row.style === 'flag');
    for (const row of flagged) {
      expect(row.version).not.toContain(`.NET ${site} `);
    }
    expect(STACK.chips[0].figure).toBe(`.NET ${site}`);
  });

  it('says what the site runs as the project file says it, not as anybody remembers it', () => {
    const backend = STACK.layers.find((layer) => layer.key === 'backend');
    const framework = (/<TargetFramework>net([\d.]+)</.exec(
      files['api/TheYard.Api/TheYard.Api.csproj']
    ) ?? [])[1];
    expect(backend?.rows[0].version).toBe(framework);
    const lock = JSON.parse(files['package-lock.json']) as {
      packages: Record<string, { version: string }>;
    };
    const react = STACK.layers.flatMap((layer) => layer.rows).find((row) => row.tech === 'React');
    expect(lock.packages['node_modules/react'].version.startsWith(`${react?.version}.`)).toBe(true);
  });
});
