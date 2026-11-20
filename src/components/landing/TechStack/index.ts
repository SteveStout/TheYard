/**
 * Does:      The folder entry for TechStack: re-exports TechStack and its summary, so an import names the folder,
 *            components/landing/TechStack.
 * Does not:  Hold code of its own: everything it gives is the folder's.
 * Used by:   Landing.tsx, StackPage.tsx.
 */
export { TechStack, TechStackSummary } from './TechStack';
