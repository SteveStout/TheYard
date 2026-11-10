/**
 * One number of the quality history drawn as a line (ADR-130), in the Admin tab's chart grammar: the same frame,
 * axis, labels and series colours (PlotFrame, charts.module.css), so a chart on the Quality page reads like a chart
 * on the Admin tab. The arithmetic is src/lib/qualityTrend.ts; this file only draws. The pre-scan's readings are
 * dashed, because a different instrument measured them, and the version the record names is marked in gold.
 */
import { plotFrame } from '../../../lib/plotFrame';
import { TREND_BOX, trendGeometry, type QualityTrendData } from '../../../lib/qualityTrend';
import { PlotFrame } from '../../admin/PlotFrame';
import { useFittedBox } from '../../admin/charts/useFittedBox';
import styles from '../../admin/charts/charts.module.css';

/** The line for one number, with its frame, its two end labels and the marked version. */
export function QualityTrend({ data }: { data: QualityTrendData }) {
  const [measure, box] = useFittedBox(TREND_BOX);
  const shape = trendGeometry(data, box);
  const frame = plotFrame(box, shape.along);
  const floor = box.height - box.bottom;
  return (
    <svg
      ref={measure}
      className={styles.chart}
      viewBox={`0 0 ${box.width} ${box.height}`}
      role="img"
      aria-label={`${data.label}: ${data.points.map((p) => p.value).join(', ')}`}
      data-testid="quality-trend"
    >
      <line
        className={styles.axis}
        x1={box.left}
        y1={floor}
        x2={box.width - box.right}
        y2={floor}
      />
      <PlotFrame frame={frame} />
      {shape.yLabels.map((label) => (
        <text
          key={label.y}
          className={styles.axisLabel}
          x={box.left - 6}
          y={label.y + 4}
          textAnchor="end"
        >
          {label.text}
        </text>
      ))}
      {shape.xLabels.map((label) => (
        <text
          key={label.x}
          className={styles.axisLabel}
          x={label.x}
          y={floor + 18}
          textAnchor={label.anchor}
        >
          {label.text}
        </text>
      ))}
      {shape.preScan !== '' && (
        <path
          className={`${styles.line} ${styles.thirdLine}`}
          d={shape.preScan}
          strokeDasharray="4 4"
        />
      )}
      <path
        className={`${styles.line} ${styles.firstLine}`}
        d={shape.measured}
        data-testid="quality-trend-line"
      />
      {shape.marker && (
        <g data-testid="quality-trend-marker">
          <line
            className={styles.calloutLine}
            x1={shape.marker.x}
            y1={box.top}
            x2={shape.marker.x}
            y2={floor}
          />
          <circle className={styles.calloutDot} cx={shape.marker.x} cy={shape.marker.y} r={3.5} />
          <text className={styles.calloutText} x={shape.marker.x + 6} y={box.top + 10}>
            {shape.marker.label}
          </text>
        </g>
      )}
    </svg>
  );
}
