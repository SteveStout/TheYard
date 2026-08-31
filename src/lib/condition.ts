/**
 * Condition grade bands. Rule: 4.0 and up → Excellent, 3.0–3.9 → Average,
 * below 3.0 → Rough. The UI shows both the numeric grade and the label.
 */
export type ConditionBand = 'excellent' | 'average' | 'rough';

export function conditionBand(grade: number): ConditionBand {
  if (grade >= 4) return 'excellent';
  if (grade >= 3) return 'average';
  return 'rough';
}

export const CONDITION_BAND_LABELS: Record<ConditionBand, string> = {
  excellent: 'Excellent',
  average: 'Average',
  rough: 'Rough',
};
