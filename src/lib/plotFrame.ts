/**
 * The Mark VII frame round a plot (the tweaks pass, B2), one drawing for every
 * chart that has one: graduations outside the plot up the side at the quarters
 * (major at nothing, half and the ceiling), the ticks along the bottom the
 * chart asks for, and two gold bracket ticks at the corners. The machine charts
 * and the activity chart drew this twice until the self-review of 25 September.
 */
export type PlotBox = {
  width: number;
  height: number;
  top: number;
  right: number;
  bottom: number;
  left: number;
};

/**
 * A drawing's box at the width it is given, never wider than its own (1.0.3.30).
 * A chart laid out for a desk and scaled down to a phone drew its ten-pixel
 * words at four pixels; laid out at the phone's own width, its words keep their
 * size and only the plot narrows. Wider than its own, the box is the desk's and
 * scales up as it always has.
 */
export function fitBox<Box extends PlotBox>(box: Box, given: number): Box {
  return given > 0 && Math.round(given) < box.width ? { ...box, width: Math.round(given) } : box;
}

export type FrameTick = {
  key: string;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  major: boolean;
};

/** Tick lengths and the bracket's arm, in viewBox units. */
export const FRAME = { major: 6, minor: 3, arm: 8 } as const;

const tenth = (value: number) => Math.round(value * 10) / 10;

export function plotFrame(
  box: PlotBox,
  along: { key: string; x: number; major: boolean }[]
): { ticks: FrameTick[]; brackets: [string, string] } {
  const floor = box.height - box.bottom;
  const tall = box.height - box.top - box.bottom;
  const up = [0, 0.25, 0.5, 0.75, 1].map((share) => {
    const major = share === 0 || share === 0.5 || share === 1;
    const y = tenth(floor - tall * share);
    return {
      key: `y${share}`,
      x1: box.left - (major ? FRAME.major : FRAME.minor),
      y1: y,
      x2: box.left,
      y2: y,
      major,
    };
  });
  // A minor tick under a major one is drawn once, as the major.
  const majors = along.filter((tick) => tick.major).map((tick) => tenth(tick.x));
  const bottom = along
    .filter((tick) => tick.major || !majors.some((x) => Math.abs(x - tenth(tick.x)) < 0.5))
    .map((tick) => {
      const x = tenth(tick.x);
      return {
        key: tick.key,
        x1: x,
        y1: floor,
        x2: x,
        y2: floor + (tick.major ? FRAME.major : FRAME.minor),
        major: tick.major,
      };
    });
  const right = box.width - box.right;
  return {
    ticks: [...up, ...bottom],
    brackets: [
      `M${box.left + 1} ${box.top + 1 + FRAME.arm}V${box.top + 1}H${box.left + 1 + FRAME.arm}`,
      `M${right - 1 - FRAME.arm} ${floor - 1}H${right - 1}V${floor - 1 - FRAME.arm}`,
    ],
  };
}
