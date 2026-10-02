/**
 * What Azure says about the machine this site runs on, as the Azure card draws
 * it: the state, the restarts, the image and the recent platform events. Types
 * only. Its own file because it is one endpoint with its own shape.
 */

/**
 * One kind of platform event Azure recorded, how often, when last, and its message.
 * Sent by GET /api/admin/azure, in the events.
 */
export type AzureEvent = { name: string; count: number; last_at: string; message: string };

/**
 * The Azure answer: whether it could be read, and if so the machine's state and plan.
 * Sent by GET /api/admin/azure.
 */
export type AzureState = {
  available: boolean;
  reason?: string;
  /** Which kind of machine answered: a container group, or a web app on a shared plan (ADR: One plan, two sites). */
  host?: 'container-instances' | 'app-service';
  group_state?: string;
  container_state?: string;
  restart_count?: number;
  image?: string;
  events?: AzureEvent[];
  availability?: string;
  always_on?: boolean;
  health_check_path?: string | null;
  plan_name?: string | null;
  plan_sku?: string | null;
  plan_sites?: number | null;
  region?: string | null;
  fetched_at?: string;
};
