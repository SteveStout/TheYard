/**
 * Blog analytics (ADR: Every LinkedIn post is also a blog post): each post's reads, readers, depth,
 * time on the page and clicks out, and where the month's reads came from, every number from the
 * blog's SQL views and none typed by hand. Bots are flagged when a read is kept and left out of
 * every view, and no reader is named: a read is the day's keyed hash, never an address.
 */
import {
  picturesLine,
  postDate,
  sourceShares,
  type BlogStat,
  type BlogStats,
} from '../../../lib/blog';
import styles from '../shared/card.module.css';
import { About, failed, useRead } from '../shared/common';
import { CardState } from '../shared/CardState';
import { type Column, DataTable } from '../DataTable';

/** A share, or a dash when nobody has read the post yet. */
const share = (part: number, whole: number) =>
  whole === 0 ? '–' : `${Math.round((part * 100) / whole)}%`;

/** A post's row: the title and date, the reads, the readers, depth, time, and how the reads went on. */
const columns: Column<BlogStat>[] = [
  { name: 'Post', rowHeader: true, cell: (row) => row.title },
  { name: 'First on LinkedIn', short: true, cell: (row) => postDate(row.published_at) },
  { name: 'Reads', num: true, cell: (row) => row.reads },
  { name: 'Readers', num: true, cell: (row) => row.readers },
  { name: 'This week', num: true, cell: (row) => row.reads7d },
  {
    name: 'Depth',
    num: true,
    cell: (row) => (row.average_depth === null ? '–' : `${Math.round(row.average_depth)}%`),
  },
  {
    name: 'Time',
    num: true,
    cell: (row) => (row.average_seconds === null ? '–' : `${Math.round(row.average_seconds)} s`),
  },
  { name: 'Past half', num: true, cell: (row) => share(row.past_half, row.readers) },
  { name: 'Finished', num: true, cell: (row) => share(row.finished, row.readers) },
  { name: 'To LinkedIn', num: true, cell: (row) => row.to_linked_in },
  { name: 'To the code', num: true, cell: (row) => row.to_code },
  { name: 'Tagged links', num: true, cell: (row) => row.from_tagged_links },
];

export default function BlogCard({ tick }: { tick: number }) {
  const report = useRead<BlogStats>('/api/admin/blog', tick);
  const sources = report === null || report === 'failed' ? [] : sourceShares(report.sources);
  return (
    <article className={`${styles.wide} op-glass`} data-testid="blog-card">
      <h2 className={styles.cardTitle}>Blog analytics</h2>
      <About>
        Every LinkedIn post is also a post on this site. Each read is one row, kept under the day's
        keyed hash with where it came from and the device class; each thing a reader does (how far
        they scrolled, how long they stayed, a slide, a click out, a copied link) is one more. The
        numbers below are the blog's SQL views over those rows, bots left out.
      </About>
      {report === null ? (
        <CardState state="loading" />
      ) : report === 'failed' ? (
        failed('the blog numbers')
      ) : !report.available ? (
        <p className={styles.muted} data-testid="blog-absent">
          {report.kept}
        </p>
      ) : report.posts.length === 0 ? (
        <CardState state="empty" what="posts">
          No post is published yet.
        </CardState>
      ) : (
        <>
          <DataTable
            label="Each post's reads"
            testId="blog-table"
            rows={report.posts}
            rowKey={(row) => String(row.post_id)}
            columns={columns}
          />
          <p className={styles.muted} data-testid="blog-sources-line">
            Reads in the last 30 days by source:{' '}
            {sources.length === 0
              ? 'none yet.'
              : sources.map((s) => `${s.label} ${s.share}%`).join(' · ') + '.'}
          </p>
        </>
      )}
      {report !== null && report !== 'failed' && report.pictures !== undefined && (
        <p className={styles.muted} data-testid="blog-pictures-line">
          {picturesLine(report.pictures)}{' '}
          <span className={styles.muted}>Source: {report.pictures.source}.</span>
        </p>
      )}
    </article>
  );
}
