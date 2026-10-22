/**
 * The home page's Tech stack table as data (ADR: Technology versions, the addendum on the home page's
 * table): src/generated/stack.json, which scripts/stack.mjs writes from the repository's own project
 * files, package-lock.json, Dockerfiles, Bicep and workflows. Nothing here types a version, and
 * techStack.test.ts fails when the committed file is stale against the repository.
 */
import type { Stack, StackLayer, StackRow } from './stackDerive';
import generated from '../generated/stack.json';

export type { Stack, StackLayer, StackRow };

/** The table the build step wrote. */
export const STACK = generated as Stack;

/** How many rows a layer spans, so its label cell can stand beside all of them. */
export function rowSpan(layer: StackLayer): number {
  return layer.rows.length;
}
