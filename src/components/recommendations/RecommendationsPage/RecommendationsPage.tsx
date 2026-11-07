/**
 * The recommendations page, at ?view=recommendations (ADR: The recommendations have a page): every recommendation
 * on Steven's LinkedIn profile, word for word, grouped by where the writer stood as LinkedIn words it, each card with
 * the writer's name linking to their own page, the relationship line and the month, the full text, and two pills: the
 * recommendation on LinkedIn and the writer's profile. Three tiles above the groups give the count, the years and
 * the vantage points, and an index of every name sits beside them. Every number and group comes off the wire; this
 * file draws what /api/recommendations answers and works nothing out.
 */
import { useEffect, useState } from 'react';
import {
  RECOMMENDATIONS_SOURCE,
  RECOMMENDATIONS_URL,
  recommendationsAnswerFrom,
  type RecommendationsAnswer,
} from '../../../lib/recommendations';
import { anchorOf, indexOf, sectionsOf, tilesOf, titleOf } from '../../../lib/recommendationsPage';
import { request } from '../../../lib/request';
import { useFirstLoad } from '../../../hooks/useFirstLoad';
import styles from './RecommendationsPage.module.css';

type Reading =
  { state: 'reading' } | { state: 'ready'; answer: RecommendationsAnswer } | { state: 'failed' };

/** The writer's first name, for their profile pill. */
const firstName = (name: string) => name.split(' ')[0];

export function RecommendationsPage() {
  // A page the rendering service drew carries the answer it drew from, so the first draw is whole and matches it.
  const drawn = recommendationsAnswerFrom(useFirstLoad()?.recommendations ?? null);
  const [reading, setReading] = useState<Reading>(
    drawn ? { state: 'ready', answer: drawn } : { state: 'reading' }
  );
  useEffect(() => {
    let live = true;
    request(RECOMMENDATIONS_URL, undefined, { background: true })
      .then((response) => (response.ok ? (response.json() as Promise<unknown>) : null))
      .catch(() => null)
      .then((body) => {
        if (!live) return;
        const answer = recommendationsAnswerFrom(body);
        setReading(answer === null ? { state: 'failed' } : { state: 'ready', answer });
      });
    return () => {
      live = false;
    };
  }, []);

  const answer = reading.state === 'ready' ? reading.answer : null;
  const sections = answer ? sectionsOf(answer) : [];
  return (
    <section
      className={styles.page}
      aria-labelledby="recommendations-title"
      aria-busy={reading.state === 'reading'}
      data-testid="recommendations-page"
      data-state={reading.state}
    >
      <header className={styles.head}>
        <p className={styles.eyebrow}>Who and why · Recommendations</p>
        <h1 id="recommendations-title" className={styles.title}>
          {answer ? titleOf(answer) : 'The people who worked with Steven'}
        </h1>
        <p className={styles.lede}>
          In their own words, from LinkedIn. Each one is on the writer&rsquo;s own LinkedIn page and
          on Steven&rsquo;s, so nothing on this page has to be taken on trust: click the name to
          check it.
        </p>
      </header>
      {reading.state === 'failed' && (
        <p className={styles.failed}>The recommendations could not be read just now.</p>
      )}
      {answer && (
        <>
          <ul className={styles.tiles} aria-label="The recommendations in three numbers">
            {tilesOf(answer).map((tile) => (
              <li key={tile.label} className={`${styles.tile} op-glass op-tile`}>
                <span className={styles.tileLabel}>{tile.label}</span>
                <span className={styles.tileFigure}>{tile.figure}</span>
                <span className={styles.tileLine}>{tile.line}</span>
              </li>
            ))}
          </ul>
          <div className={styles.body}>
            <div className={styles.groups}>
              {sections.map((section) => (
                <section
                  key={section.key}
                  className={styles.group}
                  aria-labelledby={`group-${section.key}`}
                >
                  <h2 id={`group-${section.key}`} className={styles.groupTitle}>
                    {section.title}{' '}
                    <span className={styles.groupCount}>{section.entries.length}</span>
                  </h2>
                  <p className={styles.groupLead}>{section.lead}</p>
                  {section.entries.map((entry) => (
                    <article
                      key={entry.name}
                      id={anchorOf(entry.name)}
                      className={`${styles.card} op-glass`}
                      data-testid="recommendation-card"
                    >
                      <h3 className={styles.name}>
                        <a href={entry.profile} target="_blank" rel="noreferrer">
                          {entry.name}
                        </a>
                      </h3>
                      <p className={styles.writer}>{entry.title}</p>
                      <p className={styles.relationship}>
                        {entry.relationship} · {entry.when}
                      </p>
                      <blockquote className={styles.words} cite={RECOMMENDATIONS_SOURCE}>
                        {entry.paragraphs.map((paragraph) => (
                          <p key={paragraph}>{paragraph}</p>
                        ))}
                      </blockquote>
                      <p className={styles.pills}>
                        <a
                          className={styles.pill}
                          href={RECOMMENDATIONS_SOURCE}
                          target="_blank"
                          rel="noreferrer"
                        >
                          Read it on LinkedIn
                        </a>
                        <a
                          className={styles.pill}
                          href={entry.profile}
                          target="_blank"
                          rel="noreferrer"
                        >
                          {firstName(entry.name)}&rsquo;s profile
                        </a>
                      </p>
                    </article>
                  ))}
                </section>
              ))}
              <section
                className={`${styles.finish} op-glass`}
                aria-labelledby="recommendations-finish"
              >
                <h2 id="recommendations-finish" className={styles.groupTitle}>
                  That is everyone
                </h2>
                <p className={styles.groupLead}>
                  Every recommendation on the profile is on this page, whole. The review route shows
                  the work they describe.
                </p>
                <p className={styles.pills}>
                  <a className={styles.pill} href="/?view=review">
                    Check my work
                  </a>
                  <a className={styles.pill} href="/about">
                    About Steven
                  </a>
                </p>
              </section>
            </div>
            <nav className={`${styles.index} op-glass`} aria-label="On this page">
              <p className={styles.indexTitle}>On this page</p>
              <ul>
                {indexOf(sections).map((item) => (
                  <li key={item.anchor}>
                    <a href={`#${item.anchor}`}>{item.name}</a>
                  </li>
                ))}
              </ul>
            </nav>
          </div>
        </>
      )}
    </section>
  );
}
