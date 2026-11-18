/**
 * Does:      Draws the blog's filter bar (ADR: Every LinkedIn post is also a blog post, the addendum on the read model):
 *            the search box, the month and the order, Clear, the topic chips and the keyword chips each with the count
 *            the list answered, and the line that says how many posts show. Every control says what it changes; a chip
 *            that would leave no post is disabled unless it is the one pressed.
 * Does not:  Keep the filter (the address does), read the list (useBlogList.ts does) or wait on the keys (the reader does).
 * Used by:   BlogPage.tsx.
 */
import { useState } from 'react';
import { ICON } from '../../../lib/icons';
import type { BlogFacet } from '../../../lib/blog';
import { NO_FILTER, isFiltered, type BlogFilter } from '../../../lib/blogFilter';
import styles from './BlogFilters.module.css';

/** How a filter change reaches the address: a search being typed replaces the entry, every other change is a new one. */
export type FilterChange = (filter: BlogFilter, how: 'type' | 'choose') => void;

function Chips({
  label,
  facets,
  pressed,
  dashed,
  onPress,
}: {
  label: string;
  facets: BlogFacet[];
  pressed: string | null;
  dashed?: boolean;
  onPress: (key: string | null) => void;
}) {
  return (
    <div className={styles.chipRow}>
      <span className={styles.chipLabel} id={`blog-chips-${label}`}>
        {label}
      </span>
      <div className={styles.chips} role="group" aria-labelledby={`blog-chips-${label}`}>
        {facets.map((facet) => (
          <button
            key={facet.key}
            type="button"
            className={dashed ? `${styles.chip} ${styles.dashed}` : styles.chip}
            aria-pressed={pressed === facet.key}
            disabled={facet.count === 0 && pressed !== facet.key}
            onClick={() => onPress(pressed === facet.key ? null : facet.key)}
          >
            {facet.label}
            <small>{facet.count}</small>
          </button>
        ))}
      </div>
    </div>
  );
}

export function BlogFilters({
  filter,
  topics,
  keywords,
  months,
  showing,
  of,
  onChange,
}: {
  filter: BlogFilter;
  topics: BlogFacet[];
  keywords: BlogFacet[];
  months: BlogFacet[];
  showing: number | null;
  of: number | null;
  onChange: FilterChange;
}) {
  // The box keeps what is typed, so a key never waits on the address; Back, Forward and Clear set it from the filter.
  const [text, setText] = useState(filter.q);
  const [shownQ, setShownQ] = useState(filter.q);
  if (filter.q !== shownQ) {
    setShownQ(filter.q);
    if (filter.q !== text) setText(filter.q);
  }
  return (
    <section
      className={`${styles.bar} op-glass`}
      aria-label="Filter the posts"
      data-testid="blog-filters"
    >
      <div className={styles.row}>
        <label className={styles.search}>
          <span className={styles.hidden}>Search the posts</span>
          <svg
            viewBox="0 0 20 20"
            width={ICON.sm}
            height={ICON.sm}
            fill="none"
            stroke="currentColor"
            strokeWidth={ICON.stroke}
            aria-hidden="true"
          >
            <circle cx="9" cy="9" r="6" />
            <path d="m17 17-3.5-3.5" />
          </svg>
          <input
            type="search"
            value={text}
            placeholder="Search: try copilot, ms, scams, llm, dotnet"
            autoComplete="off"
            enterKeyHint="search"
            maxLength={100}
            data-testid="blog-search"
            onChange={(event) => {
              setText(event.target.value);
              onChange({ ...filter, q: event.target.value }, 'type');
            }}
          />
        </label>
        <select
          aria-label="Month"
          value={filter.month ?? ''}
          onChange={(event) => onChange({ ...filter, month: event.target.value || null }, 'choose')}
          data-testid="blog-month"
        >
          <option value="">Any month</option>
          {months.map((month) => (
            <option key={month.key} value={month.key}>
              {month.label} ({month.count})
            </option>
          ))}
        </select>
        <select
          aria-label="Order"
          value={filter.sort}
          onChange={(event) =>
            onChange({ ...filter, sort: event.target.value === 'read' ? 'read' : 'new' }, 'choose')
          }
          data-testid="blog-sort"
        >
          <option value="new">{filter.q.trim() ? 'Best match' : 'Newest first'}</option>
          <option value="read">Most read</option>
        </select>
        <button
          type="button"
          className={styles.clear}
          disabled={!isFiltered(filter)}
          onClick={() => onChange(NO_FILTER, 'choose')}
          data-testid="blog-clear"
        >
          Clear
        </button>
      </div>
      <Chips
        label="Topics"
        facets={topics}
        pressed={filter.topic}
        onPress={(topic) => onChange({ ...filter, topic }, 'choose')}
      />
      <Chips
        label="Keywords"
        facets={keywords}
        pressed={filter.keyword}
        dashed
        onPress={(keyword) => onChange({ ...filter, keyword }, 'choose')}
      />
      <p className={styles.count} aria-live="polite" data-testid="blog-count">
        {showing === null ? ' ' : `Showing ${showing} of ${of ?? showing} posts`}
      </p>
    </section>
  );
}
