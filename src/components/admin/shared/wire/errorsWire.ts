/**
 * The error ring the Admin tab's Errors card lists: what failed, where, and the
 * frames that led there, from the server and the browser alike. Types only.
 * Its own file because the error ring has its own endpoint outside /api/admin.
 */

/**
 * One recorded error: when, on which path, the status, the message and the stack frames.
 * Sent by GET /api/errors (and by GET /api/admin/kept?card=errors for a kept window).
 */
export type ErrorEntry = {
  at: string;
  path: string;
  status: number;
  message: string;
  frames: string[];
};
