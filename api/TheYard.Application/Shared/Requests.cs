// The two shapes every request in this codebase takes (ADR-115: A request is a query or a command). A query
// reads and changes nothing; a command changes something and may answer what it did. Each request is a
// record named for what it asks, with exactly one handler, and the slice's Module maps its route to that
// handler. There is no bus, no mediator and no pipeline: the two interfaces are the whole of it, and
// CqrsTests holds every handler to one of them.
namespace TheYard.Application.Shared;

// #region requests
/// <summary>Answers one query: reads, and writes nothing. Its constructor takes reader ports only (CqrsTests).</summary>
/// <typeparam name="TQuery">The query, a record named <c>&lt;Verb&gt;&lt;Noun&gt;Query</c>.</typeparam>
/// <typeparam name="TResult">What the query answers.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
{
    /// <summary>The answer to one query.</summary>
    /// <param name="query">What is asked.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellation);
}

/// <summary>Carries out one command: changes something, and answers what it did. It may take reader and writer ports.</summary>
/// <typeparam name="TCommand">The command, a record named <c>&lt;Verb&gt;&lt;Noun&gt;Command</c>.</typeparam>
/// <typeparam name="TResult">What the command answers: the outcome, or whether it did anything.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
{
    /// <summary>Carries out one command.</summary>
    /// <param name="command">What is to be done.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellation);
}
// #endregion requests
