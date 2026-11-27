/**
 * Does:      Draws the blog at ?view=blog (ADR: Every LinkedIn post is also a blog post, and its addendum on the read
 *            model): the head, the filter bar, the list for the address's filter with its first post in full and the
 *            rest as short cards, a page more at a time, or the one post an address names with the posts around it;
 *            beside them the most read this week and where readers came from. A post's address opens with its hook
 *            (ADR-132) until the post is read, and ends on the site's one closing row (AskClose, its Book 20 minutes
 *            opening the booking sheet) over its Where next. Every filter change goes through the address, so a view can be linked.
 * Does not:  Read the list or the post itself (useBlogList.ts and useBlogPost.ts do), write the address (useBlogNavigation
 *            does, through onMove and onOpen), or report a read (BlogPostCard does).
 * Used by:   App.tsx.
 */
import { startTransition, useState } from 'react';
import type { BlogList } from '../../../lib/blog';
import {
  emptyLine,
  filterFromSearch,
  filterToParams,
  isDrawnFor,
  NO_FILTER,
  slideFrom,
  type BlogFilter,
  type FilterLabels,
} from '../../../lib/blogFilter';
import type { AskWhen } from '../../../lib/blogReads';
import { useBlogList } from '../../../hooks/useBlogList';
import { useBlogPost } from '../../../hooks/useBlogPost';
import { useBlogPosts } from '../../../hooks/useBlogPosts';
import { useFirstLoad, type HookPost } from '../../../hooks/useFirstLoad';
import { BookSheet } from '../../review/BookSheet';
import { AskClose } from '../../shared/AskClose';
import { Hook } from '../../shared/Hook';
import { WhereNext } from '../../shared/WhereNext';
import { BlogFilters, type FilterChange } from '../BlogFilters';
import { BlogMini } from '../BlogMini';
import { BlogPostCard } from '../BlogPostCard';
import { BlogPostNav } from '../BlogPostNav';
import { BlogSide } from '../BlogSide';
import styles from './BlogPage.module.css';

/** The chips' labels by key, from a list's own answer. */
function labelsOf(list: BlogList | null): FilterLabels | null {
  if (!list) return null;
  const by = (facets: BlogList['topics']) =>
    Object.fromEntries(facets.map((facet) => [facet.key, facet.label]));
  return { topics: by(list.topics), keywords: by(list.keywords), months: by(list.months) };
}

export function BlogPage({
  slug,
  query,
  onOpen,
  onMove,
  onAllPosts,
}: {
  slug: string | null;
  query: string;
  onOpen: (slug: string | null, query?: string) => void;
  onMove: (query: string, how: 'push' | 'replace') => void;
  onAllPosts: () => void;
}) {
  const first = useFirstLoad();
  const shared = useBlogPosts();
  const all = shared.state === 'ready' ? shared.list : null;
  const filter: BlogFilter = slug === null ? filterFromSearch(query) : NO_FILTER;
  const [when, setWhen] = useState<AskWhen>('now');
  // Book 20 minutes at a post's foot opens the review page's booking sheet beside the email app.
  const [booking, setBooking] = useState(false);
  // The server's list stands in only for the filter it was drawn for: a post's address can carry a list filter of its own.
  const drawn = isDrawnFor(first?.search, filter) ? first?.blog?.list : null;
  const listed = useBlogList(filter, when, drawn, slug === null);
  const list = slug === null ? listed.list : null;
  const lead = list?.cards[0] ?? null;
  const full = useBlogPost(slug ?? lead?.slug ?? null, first?.blog?.post);
  const labels = labelsOf(list ?? all);
  const terms = list?.terms ?? [];
  // A slide is open on a post's own page from the address (?slide=3), and on the list from a click, for this visit only.
  const [listSlide, setListSlide] = useState<{ slug: string; position: number } | null>(null);
  const slide =
    slug !== null
      ? slideFrom(`?post=${slug}&${query}`)
      : listSlide !== null && listSlide.slug === lead?.slug
        ? listSlide.position
        : null;

  // A key typed moves the address and the page as a transition: the box keeps its own text at once, and the rest is
  // drawn in slices, so typing never waits on a page drawn whole (the budget: no task over 50 ms while typing).
  const change: FilterChange = (next, how) => {
    const move = () => {
      setWhen(how === 'type' ? 'wait' : 'now');
      onMove(filterToParams(next).toString(), how === 'type' ? 'replace' : 'push');
    };
    if (how === 'type') {
      // The filter the address will read back, so the reader knows the list this key leads to.
      listed.typed(filterFromSearch(filterToParams(next)));
      startTransition(move);
    } else move();
  };
  const byTopic = (topic: string) =>
    slug === null
      ? change({ ...filter, topic, q: '' }, 'choose')
      : onOpen(null, filterToParams({ ...NO_FILTER, topic }).toString());
  const byKeyword = (keyword: string) =>
    slug === null
      ? change({ ...filter, keyword, q: '' }, 'choose')
      : onOpen(null, filterToParams({ ...NO_FILTER, keyword }).toString());
  const openSlides = (posted: string) => onOpen(posted, 'slide=1');
  const turnSlide = (position: number | null) =>
    slug !== null
      ? onMove(position === null ? '' : `slide=${position}`, 'replace')
      : setListSlide(position === null || lead === null ? null : { slug: lead.slug, position });

  // The post's hook: from the post once read, and before then from what the server drew it with.
  const hook: HookPost | null =
    slug === null
      ? null
      : full.post
        ? {
            slug: full.post.slug,
            title: full.post.title,
            summary: full.post.summary,
            image: full.post.images[0] ?? null,
          }
        : first?.post && first.post.slug === slug
          ? first.post
          : null;
  const cardProps = { labels, terms, onTopic: byTopic, onKeyword: byKeyword };
  // While what the page shows is read, the list keeps a screen of room, so nothing after it moves when it arrives (ADR-113).
  const reading =
    slug === null
      ? list === null && !listed.unavailable && !listed.failed
      : full.post === null && !full.missing;

  return (
    <section className={styles.page} aria-labelledby="blog-title" data-testid="blog-page">
      <div className={styles.head}>
        <h1 id="blog-title" className={styles.title}>
          Blog
        </h1>
        {slug !== null && (
          <button type="button" className={styles.back} onClick={onAllPosts}>
            All posts
          </button>
        )}
      </div>
      {slug === null && (
        <>
          <p className={styles.intro}>
            Everything I post on LinkedIn, kept here in full with the code and the live pages it
            talks about. Newest first.
          </p>
          <BlogFilters
            filter={filter}
            topics={(list ?? all)?.topics ?? []}
            keywords={(list ?? all)?.keywords ?? []}
            months={(list ?? all)?.months ?? []}
            showing={list?.total ?? null}
            of={all?.total ?? null}
            onChange={change}
          />
        </>
      )}

      <div className={styles.layout}>
        <div
          className={`${styles.list} ${reading ? styles.reading : ''} ${slug === null && listed.reading && list ? styles.dim : ''}`}
          aria-busy={slug === null && listed.reading}
          data-testid="blog-list"
        >
          {slug !== null && hook && (
            <Hook
              page="post"
              title={hook.title}
              lines={[hook.summary]}
              figure={
                hook.image && (
                  <img
                    src={hook.image.thumb_url ?? hook.image.url}
                    alt={hook.image.alt_text}
                    width={hook.image.width}
                    height={hook.image.height}
                    fetchPriority="high"
                  />
                )
              }
            />
          )}
          {reading && (
            <p className={styles.note}>
              {slug === null ? 'Reading the posts...' : 'Reading the post...'}
            </p>
          )}
          {slug === null && listed.unavailable && (
            <p className={styles.note}>The blog cannot be read now. The rest of the site can.</p>
          )}
          {slug === null && listed.failed && list === null && (
            <p className={styles.note}>The posts could not be read. Try again in a moment.</p>
          )}
          {list !== null && list.cards.length === 0 && (
            <div className={styles.empty} data-testid="blog-empty">
              <p>{labels ? emptyLine(filter, labels) : 'No post matches.'}</p>
              <button
                type="button"
                className={styles.back}
                onClick={() => change(NO_FILTER, 'choose')}
              >
                Clear the filters
              </button>
            </div>
          )}
          {slug === null &&
            list?.cards.map((card, index) =>
              index === 0 && full.post?.slug === card.slug ? (
                <BlogPostCard
                  key={card.slug}
                  post={full.post}
                  matched={card.matched}
                  slide={slide}
                  onSlide={turnSlide}
                  {...cardProps}
                />
              ) : (
                <BlogMini
                  key={card.slug}
                  post={card}
                  onOpen={(posted) => onOpen(posted)}
                  onOpenSlides={openSlides}
                  {...cardProps}
                />
              )
            )}
          {slug === null && list?.next && (
            <button
              type="button"
              className={styles.more}
              onClick={listed.more}
              disabled={listed.moreReading}
              data-testid="blog-more"
            >
              {listed.moreReading ? 'Reading more posts...' : 'More posts'}
            </button>
          )}
          {slug !== null && full.missing && (
            <p className={styles.note}>
              No post has that address. Every post is listed on the blog.
            </p>
          )}
          {slug !== null && full.post && (
            <>
              <BlogPostCard
                key={full.post.slug}
                post={full.post}
                open
                slide={slide}
                onSlide={turnSlide}
                {...cardProps}
              />
              <BlogPostNav
                post={full.post}
                labels={labels}
                onOpen={(posted) => onOpen(posted)}
                onOpenSlides={openSlides}
                onTopic={byTopic}
                onKeyword={byKeyword}
              />
              <AskClose onBook={() => setBooking(true)} />
              <WhereNext page="post" />
            </>
          )}
        </div>

        <BlogSide cards={all?.cards ?? null} onOpen={(posted) => onOpen(posted)} />
      </div>
      <BookSheet open={booking} onClose={() => setBooking(false)} onAction={() => {}} />
    </section>
  );
}
