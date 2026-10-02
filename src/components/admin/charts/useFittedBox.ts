/**
 * The hook that sizes a chart's drawing box to the width its svg actually gets,
 * measured before the first paint and again on every resize. The machine
 * charts and the activity chart both use it, so it sits beside the charts'
 * short list as its own file rather than inside any one chart.
 */
import { useLayoutEffect, useState } from 'react';
import { fitBox, type PlotBox } from '../../../lib/plotFrame';

// #region fitted-box
/**
 * Sizes a chart's drawing box to the width its svg actually gets. On a desk
 * that is the box's own width; on a phone it is the phone's width, so the
 * chart's text keeps its size instead of shrinking (see fitBox in
 * src/lib/plotFrame.ts). It measures before the first paint and again on every
 * resize, so the chart is drawn once at the right width and never jumps.
 *
 * Returns a ref callback to put on the svg, and the fitted box.
 */
export function useFittedBox<Box extends PlotBox>(
  box: Box
): [(node: SVGSVGElement | null) => void, Box] {
  const [node, setNode] = useState<SVGSVGElement | null>(null);
  const [given, setGiven] = useState(0);
  // useLayoutEffect runs after the DOM is built but before the browser paints,
  // so the first frame the user sees is already at the measured width.
  useLayoutEffect(() => {
    if (node === null) return;
    const measure = () => setGiven(node.getBoundingClientRect().width);
    measure();
    const watcher = new ResizeObserver(measure);
    watcher.observe(node);
    return () => watcher.disconnect();
  }, [node]);
  return [setNode, fitBox(box, given)];
}
// #endregion fitted-box
