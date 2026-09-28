import { Ribbons } from './Ribbons';
import { Watermark } from './Watermark';

/**
 * The page's ground: the ribbons, then the watermark over them, in that order. A fragment, so
 * the page's markup is what it was when App rendered the two itself (ADR: One folder per
 * component).
 */
export function Background() {
  return (
    <>
      <Ribbons />
      <Watermark />
    </>
  );
}
