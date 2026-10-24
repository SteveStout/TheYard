// The Admin tab's observability routes: the SQL, store and log rings, the kept rings, the timing computed from
// them, the Store bar, Azure's view of this container, the peer and the machines. Each asks its one query handler
// (ADR-115's fourth rung); what the reading holds is built by the host part that holds the data
// (OperationsReadings.cs, MachineCardReadings.cs). The routes are mapped in AdminEndpoints.cs.

using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application.Features.Admin.Operations;
using TheYard.Application.Shared;

namespace TheYard.Api.Features.Admin.Operations;

/// <summary>The observability routes of AdminEndpoints; the type and what it is for are described in AdminEndpoints.cs.</summary>
public static partial class AdminEndpoints
{
    /// <summary>
    /// A reading as the route answers it: the body as JSON in the host's own wire, a refusal as a 400 problem and
    /// nothing to read as a 404 problem, each with the handler's own words.
    /// </summary>
    internal static IResult Answer(Reading reading) => reading.Kind switch
    {
        ReadingKind.Read => Results.Json(reading.Body),
        ReadingKind.Refused => Results.Problem(detail: reading.Detail, statusCode: StatusCodes.Status400BadRequest, title: reading.Title),
        _ => Results.Problem(detail: reading.Detail, statusCode: StatusCodes.Status404NotFound, title: reading.Title),
    };

    /// <summary>Answers the SQL ring: the statements the relational store ran, newest first.</summary>
    private static async Task<IResult> Sql(ReadSqlRingHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadSqlRingQuery(), cancellation));

    /// <summary>Answers the document store's operations ring, newest first, with the store's name.</summary>
    private static async Task<IResult> Store(ReadStoreRingHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadStoreRingQuery(), cancellation));

    /// <summary>Answers how this request arrived, to the operator alone; anybody else gets a 404, as if the route were not there.</summary>
    private static async Task<IResult> ArrivalRead(HttpContext http, AdminKey adminKey, ReadArrivalHandler handler, CancellationToken cancellation) =>
        adminKey.IsPresentedBy(http.Request)
            ? Answer(await handler.HandleAsync(new ReadArrivalQuery(), cancellation))
            : TypedResults.NotFound();

    /// <summary>Answers the log ring, newest first.</summary>
    private static async Task<IResult> Logs(ReadLogRingHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadLogRingQuery(), cancellation));

    /// <summary>Answers one card's kept ring over a window.</summary>
    private static async Task<IResult> Kept(string? card, string? window, ReadKeptRingHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadKeptRingQuery(card, window), cancellation));

    /// <summary>Answers the timings of the request's store and every other.</summary>
    private static async Task<IResult> Metrics(ReadMetricsHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadMetricsQuery(), cancellation));

    /// <summary>Answers the store bar; an old cookie that named a store is expired on the way, the request's own fact.</summary>
    private static async Task<Ok<StoresView>> Stores(HttpContext http, ReadStoresHandler handler, CancellationToken cancellation)
    {
        Backends.ExpireLegacyCookie(http);
        return TypedResults.Ok((StoresView)(await handler.HandleAsync(new ReadStoresQuery(), cancellation)).Body!);
    }

    /// <summary>Answers this container as Azure has it, with the hourly readings.</summary>
    private static async Task<IResult> Azure(ReadAzureSelfHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadAzureSelfQuery(), cancellation));

    /// <summary>Answers the other container's metrics.</summary>
    private static async Task<IResult> Peer(ReadPeerHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadPeerQuery(), cancellation));

    /// <summary>Answers the machines card over the last hour or a kept window.</summary>
    private static async Task<IResult> Machines(string? window, ReadMachinesHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadMachinesQuery(window), cancellation));
}

/// <summary>How one request arrived: the forwarded headers as they reached the process, and what the middleware made of them.</summary>
/// <param name="ArrivedFor">X-Forwarded-For exactly as it arrived, before the middleware read it.</param>
/// <param name="ArrivedProto">X-Forwarded-Proto exactly as it arrived.</param>
/// <param name="Address">The caller's address as the app now sees it.</param>
/// <param name="Scheme">The scheme as the app now sees it.</param>
/// <param name="OriginalFor">The connection's own address and port before the middleware, which it keeps in X-Original-For.</param>
/// <param name="OriginalProto">The connection's own scheme before the middleware, which it keeps in X-Original-Proto.</param>
/// <param name="ForwardedFor">What is left of X-Forwarded-For after the middleware took the entries it trusts: whatever lay to their left.</param>
/// <param name="ForwardedProto">What is left of X-Forwarded-Proto after the same.</param>
/// <param name="ForwardedHost">X-Forwarded-Host as sent, which this app never reads.</param>
/// <param name="EdgeClientAddress">The edge's own header for the client, which this app no longer reads.</param>
public sealed record ArrivalView(string ArrivedFor, string ArrivedProto, string Address, string Scheme, string OriginalFor, string OriginalProto, string ForwardedFor, string ForwardedProto, string ForwardedHost, string EdgeClientAddress);
