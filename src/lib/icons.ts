/**
 * The two sizes an inline icon is drawn at and the one stroke it is drawn with
 * (1.0.3.9), the same numbers as --icon-sm, --icon-md and --icon-stroke in
 * tokens.css. Before this the small icons were 14 and 16 px with strokes of
 * 1.6, 1.8 and 2 depending on the file. A component that draws its own SVG
 * reads these rather than writing a number; icons.test.ts holds that.
 */
export const ICON = {
  sm: 16,
  md: 20,
  stroke: 1.6,
} as const;
