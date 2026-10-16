/**
 * TypeScript types for the JSON the API sends. Each interface matches a C# record in
 * Data/ApiResponses.cs field for field, using the same snake_case names the JSON uses, so the
 * two files can be compared side by side. This module only declares shapes and computes
 * nothing: every value, such as totals and timings, comes from the server.
 * (More in docs/ADR-004-the-wire.md.)
 */
/**
 * An error returned by the server. The message is the human-readable sentence from the
 * server's JSON error body, and status is the HTTP status code (0 when the request never
 * reached the server). Code can check status, for example 409 for a name already taken.
 */
export class ApiError extends Error {
    status;
    constructor(message, status) {
        super(message);
        this.status = status;
        this.name = 'ApiError';
    }
}
