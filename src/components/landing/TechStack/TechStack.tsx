/**
 * The Tech stack panel: one table of what TheYard runs on, where it runs, the version as
 * built and the file that proves it, drawn only from the generated table in src/lib/techStack.ts.
 * A dark band with the gold bar and four headline chips, then Layer, What we use, Where it runs,
 * Version and Proof where the panel has room; narrower, each layer is a bar over its rows; on a phone each
 * row stacks under its layer. The stack page draws it whole; the home page draws its summary, the band and
 * its chips with a link to the stack page, and no element of the table (ADR: The stack has a page of its own).
 */
import { MENUS } from '../../../library/sections';
import { STACK, rowSpan } from '../../../lib/techStack';
import summary from './TechStackSummary.module.css';
import styles from './TechStack.module.css';

/** The band: what the panel says above its table, and the one line the summary closes on. */
function Band({ more = false }: { more?: boolean }) {
  const link = MENUS.stack.links?.[0];
  return (
    <div className={styles.band}>
      <p className={styles.eyebrow}>Read from the build, never typed by hand</p>
      <h3 className={styles.title}>What TheYard runs on</h3>
      <p className={styles.lede}>
        What we use, where it runs, the version as built, and the file that proves it.
      </p>
      <ul className={styles.chips} aria-label="The headline facts">
        {STACK.chips.map((chip) => (
          <li key={chip.figure} className={styles.chip}>
            <strong>{chip.figure}</strong>
            <span>{chip.words}</span>
          </li>
        ))}
      </ul>
      {more && link && (
        <a className={summary.more} href={link.href} data-testid="landing-stack-link">
          {link.label} <span aria-hidden="true">→</span>
        </a>
      )}
    </div>
  );
}

/** The home page's summary: the band and its chips, then the way to the whole table on its own page. */
export function TechStackSummary() {
  return (
    <div className={`${summary.summary} op-glass op-tile`} data-testid="landing-stack-summary">
      <Band more />
    </div>
  );
}

export function TechStack({ headingClass }: { headingClass: string }) {
  return (
    <section className={styles.stack} aria-labelledby="landing-stack" data-testid="landing-stack">
      <h2 id="landing-stack" className={headingClass}>
        Tech stack
      </h2>
      <div className="op-glass op-tile">
        <Band />
        <table className={styles.table} data-testid="landing-stack-table">
          <thead>
            <tr>
              <th scope="col" className={styles.layerHead}>
                Layer
              </th>
              <th scope="col">What we use</th>
              <th scope="col">Where it runs</th>
              <th scope="col">Version</th>
              <th scope="col">Proof</th>
            </tr>
          </thead>
          {STACK.layers.map((layer) => (
            <tbody
              key={layer.key}
              className={styles[layer.tone]}
              data-testid={`stack-${layer.key}`}
            >
              {/* The layer as a bar over its rows, where the panel is too narrow for its own column. */}
              <tr className={styles.layerBar}>
                <th scope="rowgroup" colSpan={5}>
                  {layer.label}
                  {layer.sub !== '' && <small>{layer.sub}</small>}
                </th>
              </tr>
              {layer.rows.map((row, index) => (
                <tr key={row.tech} className={index % 2 === 1 ? styles.alt : undefined}>
                  {index === 0 && (
                    <th scope="rowgroup" rowSpan={rowSpan(layer)} className={styles.layer}>
                      {layer.label}
                      {layer.sub !== '' && <small>{layer.sub}</small>}
                    </th>
                  )}
                  <td className={styles.tech}>
                    {row.tech}
                    {row.note !== null && <em> ({row.note})</em>}
                  </td>
                  <td className={styles.where}>{row.where}</td>
                  <td>
                    <span className={styles[row.style]} data-testid="stack-version">
                      {row.version}
                    </span>
                  </td>
                  <td className={`${styles.proof} ${styles.mono}`}>
                    <a href={row.proof.href} target="_blank" rel="noreferrer">
                      {row.proof.label}
                    </a>
                  </td>
                </tr>
              ))}
            </tbody>
          ))}
        </table>
        <p className={styles.foot}>
          <span className={styles.dot} aria-hidden="true" />
          <span>
            <b>Generated, not typed.</b> A build step reads the project files, package.json, the
            Dockerfiles, the Bicep and the workflows; a test fails CI when this table drifts from
            the repository.
          </span>
        </p>
      </div>
    </section>
  );
}
