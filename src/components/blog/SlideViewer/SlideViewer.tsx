/**
 * Does:      Shows a post's slides full size in a native dialog (ADR: Every LinkedIn post is also a blog post, the addendum
 *            on the slide viewer): the slide with its alt text as the caption, Prev and Next, "3 of 11", a strip of every
 *            slide, a click to zoom, a swipe on a phone, Escape and a tap outside to close, focus kept inside while it is
 *            open and handed back to the slide that opened it, a link to the slide to copy, and the slides either side
 *            fetched ahead. The thumbnail the strip already holds is drawn at once under the full slide.
 * Does not:  Decide which slide the address names (BlogPage does) or keep any count (useBlogReport sends the events).
 * Used by:   BlogPostCard.tsx.
 */
import { useEffect, useRef, useState } from 'react';
import type { BlogImage } from '../../../lib/blog';
import styles from './SlideViewer.module.css';

/** How far a finger has to move sideways, in pixels, to turn a slide. */
const SWIPE_PX = 45;

/** How much a click on the slide zooms it. */
const ZOOM = 2.2;

export function SlideViewer({
  title,
  images,
  at,
  slideAddress,
  onMove,
  onClose,
}: {
  title: string;
  images: BlogImage[];
  at: number;
  slideAddress: (position: number) => string;
  onMove: (index: number) => void;
  onClose: () => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const opener = useRef<Element | null>(null);
  const touchX = useRef<number | null>(null);
  // Both belong to the slide they were set on, so turning the slide drops them without an effect.
  const [zoomed, setZoom] = useState<{ at: number; origin: string } | null>(null);
  const [copiedAt, setCopiedAt] = useState<number | null>(null);
  const zoom = zoomed?.at === at ? zoomed.origin : null;
  const copied = copiedAt === at;
  const image = images[at];

  useEffect(() => {
    const box = dialog.current;
    if (!box) return;
    opener.current = document.activeElement;
    if (!box.open) box.showModal();
    return () => {
      if (box.open) box.close();
      if (opener.current instanceof HTMLElement) opener.current.focus();
    };
  }, []);

  useEffect(() => {
    // The slides either side, and only those, so a turn shows its slide from the cache.
    for (const near of [images[at - 1], images[at + 1]]) {
      if (near) new Image().src = near.url;
    }
    document
      .getElementById(`viewer-film-${at}`)
      ?.scrollIntoView?.({ block: 'nearest', inline: 'center' });
  }, [at, images]);

  if (!image) return null;
  const move = (step: number) => {
    const next = at + step;
    if (next >= 0 && next < images.length) onMove(next);
  };

  return (
    <dialog
      ref={dialog}
      className={styles.viewer}
      aria-label={`The slides of ${title}`}
      data-testid="slide-viewer"
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        if (event.target === dialog.current) onClose();
      }}
      onKeyDown={(event) => {
        if (event.key === 'ArrowRight') move(1);
        else if (event.key === 'ArrowLeft') move(-1);
      }}
    >
      <div className={styles.top}>
        <p className={styles.where}>
          <b>{title}</b>
          <span data-testid="slide-count">
            Slide {at + 1} of {images.length}
          </span>
        </p>
        <button
          type="button"
          className={styles.action}
          onClick={() => {
            void navigator.clipboard
              ?.writeText(slideAddress(image.position))
              .then(() => setCopiedAt(at))
              .catch(() => {});
          }}
        >
          {copied ? 'Link copied' : 'Copy link to slide'}
        </button>
        <button
          type="button"
          className={styles.action}
          aria-pressed={zoom !== null}
          onClick={() => setZoom(zoom ? null : { at, origin: '50% 50%' })}
        >
          Zoom
        </button>
        <button
          type="button"
          className={styles.action}
          onClick={onClose}
          aria-label="Close the slides"
          data-testid="slide-close"
        >
          Close
        </button>
      </div>
      <div
        className={styles.stage}
        onClick={(event) => {
          if (event.target === event.currentTarget) onClose();
        }}
        onTouchStart={(event) => {
          touchX.current = event.touches[0]?.clientX ?? null;
        }}
        onTouchEnd={(event) => {
          const start = touchX.current;
          const end = event.changedTouches[0]?.clientX;
          touchX.current = null;
          if (start !== null && end !== undefined && Math.abs(end - start) > SWIPE_PX)
            move(end < start ? 1 : -1);
        }}
      >
        <button
          type="button"
          className={`${styles.arrow} ${styles.prev}`}
          onClick={() => move(-1)}
          disabled={at === 0}
          aria-label="Previous slide"
        >
          <span aria-hidden="true">‹</span>
        </button>
        <figure className={styles.figure}>
          <div className={styles.frame} style={{ aspectRatio: `${image.width} / ${image.height}` }}>
            <img className={styles.under} src={image.thumb_url} alt="" aria-hidden="true" />
            <img
              key={image.url}
              className={zoom ? `${styles.full} ${styles.zoomed}` : styles.full}
              src={image.url}
              alt={image.alt_text}
              width={image.width}
              height={image.height}
              style={zoom ? { transformOrigin: zoom, transform: `scale(${ZOOM})` } : undefined}
              data-testid="slide-image"
              onClick={(event) => {
                const box = event.currentTarget.getBoundingClientRect();
                setZoom(
                  zoom
                    ? null
                    : {
                        at,
                        origin: `${((event.clientX - box.left) / box.width) * 100}% ${((event.clientY - box.top) / box.height) * 100}%`,
                      }
                );
              }}
            />
          </div>
          <figcaption className={styles.caption} data-testid="slide-caption">
            {image.alt_text}
          </figcaption>
        </figure>
        <button
          type="button"
          className={`${styles.arrow} ${styles.next}`}
          onClick={() => move(1)}
          disabled={at === images.length - 1}
          aria-label="Next slide"
        >
          <span aria-hidden="true">›</span>
        </button>
      </div>
      <ol className={styles.film} aria-label="Every slide">
        {images.map((slide, index) => (
          <li key={slide.position}>
            <button
              id={`viewer-film-${index}`}
              type="button"
              aria-current={index === at}
              aria-label={`Slide ${index + 1}`}
              onClick={() => onMove(index)}
            >
              <img
                src={slide.thumb_url}
                alt=""
                width={46}
                height={Math.round((46 * slide.height) / Math.max(slide.width, 1))}
                loading="lazy"
                decoding="async"
              />
            </button>
          </li>
        ))}
      </ol>
      <p className={styles.hint}>
        Arrow keys or swipe to move, click the slide or Zoom to zoom, Escape to close
      </p>
    </dialog>
  );
}
