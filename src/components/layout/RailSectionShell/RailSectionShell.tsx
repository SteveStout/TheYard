/**
 * One sidebar section's frame: a native disclosure that starts closed, or on
 * the icons-only rail a plain section that keeps its rows showing. Its own
 * file because how a section opens and closes is one decision with its own
 * reasons, quoted live in the sidebar's decision record.
 */
import type { ReactNode } from 'react';
import styles from '../SideNav/SideNav.module.css';

// #region section-shell
/**
 * A sidebar section: a native `details`, closed until somebody asks for it.
 *
 * Closed by default turns the sidebar into a table of contents: the section
 * headings, and the one you want is one click away, rather than a rail that
 * arrives holding about a hundred rows with the reader's own section somewhere
 * inside them. The keyboard and screen-reader behaviour comes from the element
 * rather than from a reimplementation of it, which is why this is a `details`
 * and not a button and a piece of state.
 *
 * The icons-only rail is the exception. There are no headings on that rail:
 * the rows are icons and the words are hidden, so a closed section would be a
 * triangle with nothing to read and nothing to aim at. It keeps the rows
 * (ADR: The sidebar, the addendum on closed sections).
 */
export function RailSectionShell({
  label,
  iconsOnly,
  children,
}: {
  label: string;
  iconsOnly: boolean;
  children: ReactNode;
}) {
  if (iconsOnly) {
    return (
      <section className={styles.section}>
        <h2 className={styles.srOnly}>{label}</h2>
        {children}
      </section>
    );
  }
  return (
    <details
      className={styles.section}
      onToggle={(event) => {
        if (event.currentTarget.open) {
          event.currentTarget.scrollIntoView({ block: 'nearest' });
        }
      }}
    >
      <summary className={styles.sectionToggle}>
        {/* The label stays a heading inside the summary, which HTML allows and
            which keeps the section names in the document outline where a
            screen reader's heading list finds them; the summary is what makes
            it a disclosure. */}
        <h2 className={styles.sectionHeading}>{label}</h2>
      </summary>
      {children}
    </details>
  );
}
// #endregion section-shell
