import colors from '../styles/colors.css?raw';
import sizes from '../styles/sizes.css?raw';
import typography from '../styles/typography.css?raw';
import effects from '../styles/effects.css?raw';

/**
 * The four token sheets as one string, for code that reads the tokens as
 * text: the swatches on the Colour and style page and the tests that measure
 * the tokens. The sheets are split by what they control (colors, sizes,
 * typography, effects), but a reader that wants "every token" reads them
 * together, in the order main.tsx loads them. No React here.
 */
export const styleSheet = [colors, sizes, typography, effects].join('\n');
