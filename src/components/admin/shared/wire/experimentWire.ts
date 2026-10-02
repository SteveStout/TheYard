/**
 * The partition experiment the Experiment card draws: the same queries run
 * pinned to one partition and fanned out, with what each charged. Types only.
 * Its own file because it is one endpoint.
 */

/**
 * One query in the experiment: the partitions it touched, its charge, its time and how many documents came back.
 * Sent by GET /api/admin/experiment, in the rows.
 */
export type ExperimentRow = {
  query: string;
  partitions: string;
  request_charge: number;
  duration_ms: number;
  documents: number;
};

/**
 * The experiment answer: whether it could run, the container it ran on, and every row.
 * Sent by GET /api/admin/experiment.
 */
export type Experiment = {
  available: boolean;
  reason: string | null;
  container?: string;
  physical_partitions?: number;
  documents?: number;
  rows: ExperimentRow[];
  ran_at?: string;
};
