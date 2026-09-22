/**
 * The photographs on the Author page (ADR: The sidebar, the addendum on the
 * author's section). Each is
 * served as a responsive set, the way the inventory's photos are (ADR:
 * Responsive photos): several widths of one cut, WebP first and JPEG behind
 * it, so a phone takes a small file and a dense or large screen takes a sharp
 * one, and a picture is never drawn larger than its file. The files are cut
 * by scripts/author_photos.mjs, which strips every photograph's metadata, and
 * the originals never enter the repository.
 *
 * No React in here: what each picture is, how wide its cuts are, and the
 * markup that serves them.
 */
import manifest from './authorPhotos.json';

export type AuthorPhoto = {
  /** The file's stem under /images/author, and the name a document uses for it. */
  name: string;
  /** Every width a cut exists at, narrowest first. The largest is the file's real width. */
  widths: number[];
  /** Height over width, so the box is reserved before the file arrives and nothing jumps. */
  ratio: number;
  /** How wide the picture is drawn, for the browser to choose a cut by. */
  sizes: string;
  /**
   * A tighter cut of the same photograph for a phone, where a wide picture
   * would draw its people an inch tall. Offered below the phone line and
   * nowhere else, WebP first like the rest. `height` is the cut's height at
   * its widest, so the box is reserved for the shape a phone is actually
   * drawn. A photograph with no tighter crop is offered to a phone from its
   * own cuts up to PHONE_WIDEST.
   */
  phone?: { name: string; widths: number[]; height?: number };
  /** The credit line the page must carry while this photograph is on it. */
  credit?: string;
  /**
   * Why the largest cut is under 1920 wide, for the one kind of picture that
   * may be: a snapshot whose original has no more to give. The gate reads
   * this, so a photograph cannot be served small by forgetting.
   */
  narrowBecause?: string;
  /** Stands alone, full width, between the block it closes and the next, instead of inside it. */
  between?: boolean;
  /**
   * Sits beneath the photograph before it, in its own shape, never paired and
   * never cut: 480 px wide and centred on a desk, the column's width on a
   * phone (Steve, 2026-09-22: "make sure this image goes beneath St Charles").
   */
  beneath?: boolean;
};

// #region photos
/**
 * The list is a JSON file and not a literal here because three things read
 * it: this page, the script that cuts the files, and the gate's test that
 * holds the files to it. One list, so a width the page offers is a file the
 * script wrote and the test opened.
 */
export const AUTHOR_PHOTOS: AuthorPhoto[] = manifest.map(({ width, height, ...photo }) => ({
  ...photo,
  ratio: height / width,
}));
// #endregion photos

/** Under the address the API already serves the vehicle photos from, with the same day of caching. */
const BASE = '/api/images/author';

/**
 * The widest file a phone is ever offered: its column is under 400 pixels, so
 * 960 is sharp at more than twice its pixels, and a phone's download for the
 * whole page stays inside its budget.
 */
export const PHONE_WIDEST = 960;

/** The width a phone stops at and a wider layout begins, the same line the page's stylesheet draws. */
export const PHONE_LINE = '(max-width: 720px)';

const escape = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/** A cut's address: the stem, its width, its format. */
export function cut(name: string, width: number, format: 'webp' | 'jpg'): string {
  return `${BASE}/${name}-${width}.${format}`;
}

/** The stem a document's image address names, or null when it is not one of these photographs. */
export function photoNamed(address: string): AuthorPhoto | null {
  const stem = address.match(/\/images\/author\/([a-z0-9-]+?)-\d+\.(?:jpg|webp)$/)?.[1];
  return AUTHOR_PHOTOS.find((photo) => photo.name === stem) ?? null;
}

/**
 * One photograph as markup: a figure in the one quiet frame every picture on
 * the page wears, a picture element offering WebP before JPEG at every width,
 * the box's size set, and the caption under it when there is one. Below the
 * fold unless told otherwise, so it loads when it is scrolled to.
 */
export function photoFigure(
  photo: AuthorPhoto,
  alt: string,
  caption: string | null,
  eager = false
): string {
  const setOf = (name: string, widths: number[], format: 'webp' | 'jpg') =>
    widths.map((width) => `${cut(name, width, format)} ${width}w`).join(', ');
  const set = (format: 'webp' | 'jpg') => setOf(photo.name, photo.widths, format);
  // A phone takes its tighter cut when there is one, and otherwise this photograph's own cuts up to PHONE_WIDEST.
  const own = photo.widths.filter((width) => width <= PHONE_WIDEST);
  const tight = photo.phone ?? {
    name: photo.name,
    widths: own,
    height: Math.round(own[own.length - 1] * photo.ratio),
  };
  const phoneBox =
    tight.height === undefined
      ? ''
      : ` width="${tight.widths[tight.widths.length - 1]}" height="${tight.height}"`;
  const phone =
    tight.widths.length === 0
      ? ''
      : (['webp', 'jpg'] as const)
          .map(
            (format) =>
              `<source media="${PHONE_LINE}" type="image/${format === 'jpg' ? 'jpeg' : format}" ` +
              `srcset="${setOf(tight.name, tight.widths, format)}" sizes="92vw"${phoneBox}>`
          )
          .join('');
  const largest = photo.widths[photo.widths.length - 1];
  const middle = photo.widths[Math.min(1, photo.widths.length - 1)];
  return (
    `<figure class="author-photo" data-photo="${escape(photo.name)}">` +
    `<picture>` +
    phone +
    `<source type="image/webp" srcset="${set('webp')}" sizes="${escape(photo.sizes)}">` +
    `<img class="author-frame" src="${cut(photo.name, middle, 'jpg')}" srcset="${set('jpg')}" sizes="${escape(photo.sizes)}" ` +
    `alt="${escape(alt)}" width="${largest}" height="${Math.round(largest * photo.ratio)}" ` +
    // decoding="sync": a phone throws a decoded picture away once it scrolls off and decodes it
    // again on the way back; with "async" the frame paints empty until that decode lands, which
    // is the flash Steve saw on the lower half of the page (2026-09-22). "sync" holds the old
    // frame instead. Measured the same day at 390 px: 31 decodes for a dozen pictures in one
    // scroll down and back.
    `loading="${eager ? 'eager' : 'lazy'}" decoding="sync">` +
    `</picture>` +
    (caption === null || caption === '' ? '' : `<figcaption>${escape(caption)}</figcaption>`) +
    `</figure>`
  );
}
