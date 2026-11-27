/**
 * Does:      Draws the blog at ?view=blog in the review route's look (ADR: Every LinkedIn post is also a blog post, and
 *            its addendum on the read model; ADR-142, the addendum on the landing views): the shared page head, its side
 *            the author card over the most read this week and where readers came from; on the list the filter bar, "01
 *            Latest post" in full and "02 Every post" as short cards, a page more at a time, then the close as 03; on a
 *            post its own title as the h1, its summary and cover, All posts, "01 The full post", the posts around it,
 *            the close as 02 (its Book 20 minutes opening the booking sheet) and its Where next. Every filter change
 *            goes through the address, so a view can be linked.
 * Does not:  Read the list or the post itself (useBlogList.ts and useBlogPost.ts do), write the address (useBlogNavigation
 *            does, through onMove and onOpen), or report a read (BlogPostCard does).
 * Used by:   App.tsx.
 */
import { startTransition, useState } from 'react';
import {
  emptyLine,
  filterFromSearch,
  filterToParams,
  isDrawnFor,
  labelsOf,
  NO_FILTER,
  slideFrom,
  type BlogFilter,
} from '../../../lib/blogFilter';
import type { AskWhen } from '../../../lib/blogReads';
import { listHead, postHead } from '../../../lib/blogHead';
import { useBlogList } from '../../../hooks/useBlogList';
import { useBlogPost } from '../../../hooks/useBlogPost';
import { useBlogPosts } from '../../../hooks/useBlogPosts';
import { useFirstLoad, type HookPost } from '../../../hooks/useFirstLoad';
import { BookSheet } from '../../review/BookSheet';
import { AskClose } from '../../shared/AskClose';
import { AuthorCard } from '../../shared/AuthorCard';
import { PageHead } from '../../shared/PageHead';
import { SectionHead } from '../../shared/SectionHead';
import { WhereNext } from '../../shared/WhereNext';
import { BlogFilters, type FilterChange } from '../BlogFilters';
import { BlogMini } from '../BlogMini';
import { BlogPostCard } from '../BlogPostCard';
import { BlogPostNav } from '../BlogPostNav';
import { BlogSide } from '../BlogSide';
import styles from './BlogPage.module.css';

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

  // The post's head: from the post once read, and before then from the hook the server drew it with.
  const drawnHook: HookPost | null = first?.post && first.post.slug === slug ? first.post : null;
  // A post with no head to draw yet: the title and one reading line only, so nothing on screen moves after (ADR-113).
  const bare = slug !== null && full.post === null && drawnHook === null;
  const head =
    slug === null
      ? listHead((all ?? list)?.total ?? null)
      : postHead(full.post ? { ...full.post, image: full.post.images[0] ?? null } : drawnHook);
  const cardProps = { labels, terms, onTopic: byTopic, onKeyword: byKeyword };
  // While what the page shows is read, the list keeps a screen of room, so nothing after it moves when it arrives (ADR-113).
  const reading =
    slug === null
      ? list === null && !listed.unavailable && !listed.failed
      : full.post === null && !full.missing;
  const [leadCard, ...rest] = list?.cards ?? [];
  // One line at most says why there is nothing yet: still reading, the blog unreadable, or no such post.
  const note = reading
    ? slug === null
      ? 'Reading the posts...'
      : 'Reading the post...'
    : slug === null
      ? listed.unavailable
        ? 'The blog cannot be read now. The rest of the site can.'
        : listed.failed && list === null
          ? 'The posts could not be read. Try again in a moment.'
          : null
      : full.missing
        ? 'No post has that address. Every post is listed on the blog.'
        : null;

  return (
    <section className={styles.page} aria-labelledby="blog-title" data-testid="blog-page">
      <PageHead
        eyebrow={head.eyebrow}
        title={head.title}
        titleId="blog-title"
        lede={head.lede ?? undefined}
        bodyClass={styles.body}
        side={
          <>
            <AuthorCard />
            <BlogSide cards={all?.cards ?? null} onOpen={(posted) => onOpen(posted)} />
          </>
        }
      >
        {slug === null && (
          <BlogFilters
            filter={filter}
            topics={(list ?? all)?.topics ?? []}
            keywords={(list ?? all)?.keywords ?? []}
            months={(list ?? all)?.months ?? []}
            showing={list?.total ?? null}
            of={all?.total ?? null}
            onChange={change}
          />
        )}
        {slug !== null && head.cover && (
          <figure className={`${styles.cover} op-glass`} data-mock="cover">
            <img
              src={head.cover.thumb_url ?? head.cover.url}
              alt={head.cover.alt_text}
              width={head.cover.width}
              height={head.cover.height}
              fetchPriority="high"
            />
          </figure>
        )}
        {slug !== null && !bare && (
          <p className={styles.ctas}>
            <button
              type="button"
              className={`op-pill-secondary ${styles.back}`}
              onClick={onAllPosts}
              data-mock="all-posts"
            >
              All posts
            </button>
          </p>
        )}
        {bare ? (
          <p className={`${styles.note} ${styles.reading}`}>{note}</p>
        ) : (
          <div
            className={`${styles.list} ${reading ? styles.reading : ''} ${slug === null && listed.reading && list ? styles.dim : ''}`}
            aria-busy={slug === null && listed.reading}
            data-testid="blog-list"
          >
            {note && <p className={styles.note}>{note}</p>}
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
            {slug === null && leadCard && (
              <section aria-labelledby="blog-latest-title" data-mock="section">
                <SectionHead id="blog-latest-title" number="01" title="Latest post" />
                <div className={styles.cards}>
                  {full.post?.slug === leadCard.slug ? (
                    <BlogPostCard
                      key={leadCard.slug}
                      post={full.post}
                      matched={leadCard.matched}
                      slide={slide}
                      onSlide={turnSlide}
                      {...cardProps}
                    />
                  ) : (
                    <BlogMini
                      key={leadCard.slug}
                      post={leadCard}
                      onOpen={(posted) => onOpen(posted)}
                      onOpenSlides={openSlides}
                      {...cardProps}
                    />
                  )}
                </div>
              </section>
            )}
            {(slug === null ? rest.length > 0 : full.post !== null) && (
              <section
                aria-labelledby={slug === null ? 'blog-every-title' : 'blog-full-title'}
                data-mock="section"
              >
                {slug === null ? (
                  <SectionHead id="blog-every-title" number="02" title="Every post" />
                ) : (
                  <SectionHead id="blog-full-title" number="01" title="The full post" />
                )}
                <div className={styles.cards}>
                  {rest.map((card) => (
                    <BlogMini
                      key={card.slug}
                      post={card}
                      onOpen={(posted) => onOpen(posted)}
                      onOpenSlides={openSlides}
                      {...cardProps}
                    />
                  ))}
                  {slug !== null && full.post && (
                    <BlogPostCard
                      key={full.post.slug}
                      post={full.post}
                      open
                      slide={slide}
                      onSlide={turnSlide}
                      {...cardProps}
                    />
                  )}
                </div>
              </section>
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
          </div>
        )}
      </PageHead>
      {slug !== null && full.post && (
        <BlogPostNav
          post={full.post}
          labels={labels}
          onOpen={(posted) => onOpen(posted)}
          onOpenSlides={openSlides}
          onTopic={byTopic}
          onKeyword={byKeyword}
        />
      )}
      {(slug === null ? list !== null : full.post !== null) && (
        <AskClose
          number={slug === null ? '03' : '02'}
          spaced={false}
          onBook={() => setBooking(true)}
        />
      )}
      {slug !== null && full.post && <WhereNext page="post" />}
      <BookSheet open={booking} onClose={() => setBooking(false)} onAction={() => {}} />
    </section>
  );
}
