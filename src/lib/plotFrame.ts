/**
 * The frame drawn round a chart's plot, shared by every chart that has one
 * (ADR-083): tick marks up the side at each quarter (longer at zero, half
 * and the top), the ticks along the bottom that the chart asks for, and two
 * small gold brackets at the corners. Plain functions, no React: a chart
 * passes its box in and draws the lines that come back.
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
 * A chart's box, laid out at the width it is actually given (ADR-083).
 * A chart drawn for a desk and then shrunk onto a phone shrinks its words
 * too. Laid out at the phone's own width instead, its words keep their size
 * and only the plot gets narrower. Given more room than its own width, the
 * box stays as it is and the browser scales it up as usual.
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
