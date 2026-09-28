import { useState } from 'react';
import styles from './VehicleImage.module.css';

interface VehicleImageProps {
  src: string | undefined;
  alt: string;
  /** Text shown in the fallback art; omit for a compact icon-only fallback. */
  fallbackLabel?: string;
  loading?: 'eager' | 'lazy';
  /**
   * What this will paint at, so the browser can choose between the two copies
   * before it has laid the page out. Defaults to a card in the grid, which is
   * where all but two of these are (ADR: Responsive photos).
   */
  sizes?: string;
}

// #region srcset
/**
 * The card-sized copy that `scripts/resize_photos.mjs` writes beside every
 * original: `coupe-01.jpg` has `coupe-01-480.jpg` next to it. Deriving the name
 * here rather than sending it on the wire is safe because a test holds the
 * photo manifest and the image directory to exactly that convention, so a
 * missing copy fails the build rather than a card (ADR: Responsive photos).
 *
 * The 1280 descriptor is the originals' real width, which the resize script
 * refuses to run without.
 */
function cardCopy(src: string): string | undefined {
  return src.endsWith('.jpg') ? src.replace(/\.jpg$/, '-480.jpg') : undefined;
}

/**
 * The AVIF and WebP pairs the same script writes beside the JPEG pair,
 * `coupe-01.avif` and `coupe-01-480.avif` since 1.0.3.3, `coupe-01.webp` and
 * `coupe-01-480.webp` since 1.0.0.143. They are offered in that order through
 * a `picture` element: a browser takes the first it can read, and one that
 * reads neither falls through to the `img` and the JPEGs exactly as before.
 * The same test holds every pair to the manifest, so a missing file fails the
 * build rather than a card (ADR: Responsive photos, addendum).
 */
function pairOf(
  src: string,
  format: 'avif' | 'webp'
): { small: string; large: string } | undefined {
  return src.endsWith('.jpg')
    ? {
        small: src.replace(/\.jpg$/, `-480.${format}`),
        large: src.replace(/\.jpg$/, `.${format}`),
      }
    : undefined;
}
// #endregion srcset

/**
 * An image with graceful degradation: when the source is missing or fails to
 * load, renders neutral car artwork instead of a broken image. Callers should
 * key this component by `src` so the failure state resets when it changes.
 */
export function VehicleImage({
  src,
  alt,
  fallbackLabel,
  loading = 'lazy',
  sizes = '(min-width: 1024px) 360px, 92vw',
}: VehicleImageProps) {
  const [failed, setFailed] = useState(false);

  if (!src || failed) {
    return (
      <div className={styles.fallback} role="img" aria-label={alt}>
        <svg viewBox="0 0 64 40" width="56" height="35" aria-hidden="true">
          <path
            d="M8 26h4l4-9c1-2 2-3 5-3h16c3 0 5 1 7 3l5 6 8 2c2 .5 3 2 3 4v5h-6a6 6 0 0 1-12 0H22a6 6 0 0 1-12 0H6v-6c0-1 1-2 2-2Zm14 8a3 3 0 1 0 0 .01Zm24 0a3 3 0 1 0 0 .01ZM22 17l-3 7h12v-7h-9Zm12 0v7h12l-4-5c-1-1.4-2-2-4-2h-4Z"
            fill="currentColor"
          />
        </svg>
        {fallbackLabel && <span className={styles.fallbackLabel}>{fallbackLabel}</span>}
      </div>
    );
  }

  const small = cardCopy(src);

  return (
    <picture className={styles.picture}>
      {/* Best first: a browser takes the first source it can read (1.0.3.3). */}
      {(['avif', 'webp'] as const).map((format) => {
        const pair = pairOf(src, format);
        return pair === undefined ? null : (
          <source
            key={format}
            type={`image/${format}`}
            srcSet={`${pair.small} 480w, ${pair.large} 1280w`}
            sizes={sizes}
          />
        );
      })}
      <img
        className={styles.image}
        src={src}
        srcSet={small ? `${small} 480w, ${src} 1280w` : undefined}
        sizes={small ? sizes : undefined}
        alt={alt}
        loading={loading}
        onError={() => setFailed(true)}
      />
    </picture>
  );
}
