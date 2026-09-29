// The wire, as TypeScript sees it: the same shapes Data/Entries.cs declares, in the same
// snake_case, so a reader can hold the two files side by side (ADR-004). Nothing here is
// computed; the server owns every derived fact.
/** A refusal from the server, carrying the problem document's status and its sentence. */
export class ApiError extends Error {
    status;
    constructor(message, status) {
        super(message);
        this.status = status;
        this.name = 'ApiError';
    }
}
