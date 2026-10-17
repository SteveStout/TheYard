/**
 * Freya and Vincent, under the quotations on the first screen: the two rabbits, stacked, from the
 * same photographs About Steven serves and in the same frame (ADR: The sidebar, the addendum on
 * the author's section).
 *
 * The photographs are the page's own, by name, from src/lib/authorPhotos.ts: a picture reaches
 * this card the way it reaches the Author page, so a file that is not one of them cannot be put
 * on the page by typing an address. Each box is reserved from the cut's width and the manifest's
 * own ratio, so the column does not move as they load.
 */
import {
  AUTHOR_PHOTOS,
  cut,
  mediaType,
  OFFERED,
  SOURCES,
  type AuthorPhoto,
} from '../../../lib/authorPhotos';
import styles from './Pets.module.css';

/**
 * What the browser is told each photograph is drawn at. The column stands beside the rest of the
 * page from a desk up, where it measures about 480 at the widest; under a desk the page is one
 * column and a photograph is nearly all of it. Both widths are steps of src/lib/breakpoints.ts.
 */
const SIZES = '(min-width: 1024px) 480px, 92vw';

/** The three the card shows, in order, by the names the manifest knows them under. */
const SHOWN = [
  {
    name: 'rabbits-both-lying-on-runner',
    alt: 'Two white rabbits asleep side by side on a grey rug',
  },
  {
    name: 'rabbits-lop-on-blue-rug',
    alt: 'A white lop-eared rabbit sitting on a blue rug, a white rabbit with upright ears behind',
  },
  {
    name: 'rabbits-under-hay-rack',
    alt: 'Two white rabbits side by side under a hay rack',
  },
] as const;

/** One photograph, best format first, at the width this column actually draws it. */
function Picture({ photo, alt }: { photo: AuthorPhoto; alt: string }) {
  const widths = photo.widths.filter((width) => width <= 960);
  const set = (format: (typeof OFFERED)[number]) =>
    widths.map((width) => `${cut(photo.name, width, format)} ${width}w`).join(', ');
  const largest = widths[widths.length - 1] ?? photo.widths[0];
  return (
    <picture>
      {SOURCES.map((format) => (
        <source key={format} type={mediaType(format)} srcSet={set(format)} sizes={SIZES} />
      ))}
      <img
        className={styles.photo}
        src={cut(photo.name, largest, 'jpg')}
        srcSet={set('jpg')}
        sizes={SIZES}
        width={largest}
        height={Math.round(largest * photo.ratio)}
        alt={alt}
        loading="lazy"
        decoding="async"
      />
    </picture>
  );
}

export function Pets() {
  const photos = SHOWN.map((shown) => ({
    ...shown,
    photo: AUTHOR_PHOTOS.find((photo) => photo.name === shown.name),
  })).filter((shown): shown is typeof shown & { photo: AuthorPhoto } => shown.photo !== undefined);
  if (photos.length === 0) return null;
  return (
    <section
      className={`${styles.pets} op-glass op-tile`}
      aria-labelledby="landing-pets"
      data-testid="landing-pets"
    >
      <h2 id="landing-pets" className={styles.heading}>
        Freya and Vincent
      </h2>
      <p className={styles.words}>
        Katie and I have two rabbits, Freya and Vincent, our little white snowballs.
      </p>
      <div className={styles.stack}>
        {photos.map((shown) => (
          <Picture key={shown.name} photo={shown.photo} alt={shown.alt} />
        ))}
      </div>
    </section>
  );
}
