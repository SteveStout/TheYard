/**
 * Does:      Draws the stack page at ?view=stack (ADR: The stack has a page of its own): a hook whose one line is the
 *            site map's own line for the page, the whole Tech stack table the home page used to carry, and Where next.
 * Does not:  Hold the table (src/lib/techStack.ts does, generated from the repository) or the line (siteMapEntries.ts
 *            does, where the sidebar and the home page's tile read it too).
 * Used by:   App.tsx.
 */
import { SITE_MAP } from '../../../lib/siteMap';
import { Hook } from '../../shared/Hook';
import { WhereNext } from '../../shared/WhereNext';
import landing from '../Landing/Landing.module.css';
import { TechStack } from '../TechStack';

/** The page's one line: the stack entry's own, as the sidebar and the home page's tile word it. */
const LINE = SITE_MAP.sections.find((section) => section.menu === 'stack')?.blurb ?? '';

export function StackPage() {
  return (
    <section className={landing.landing} aria-labelledby="stack-title" data-testid="stack-page">
      <Hook page="stack" lines={[LINE]} headingId="stack-title" headingClass={landing.heading} />
      <TechStack headingClass={landing.stackHeading} />
      <WhereNext page="landing" />
    </section>
  );
}
