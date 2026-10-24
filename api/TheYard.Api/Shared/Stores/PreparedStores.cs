
namespace TheYard.Api.Shared.Stores;

// #region prepared-stores
/// <summary>
/// What bringing the stores up made, handed back by StoreRegistration so Program.cs can pass each
/// later step the parts it needs and nothing else. Immutable: it is filled once, before anything
/// else is registered, and no request reads it; what a request needs is registered on its own.
/// </summary>
/// <param name="Backends">Every store this process runs, and which one a request gets.</param>
/// <param name="Relational">The relational store's context factories, held once it is attached.</param>
/// <param name="Document">The document store, or null where no account is configured.</param>
/// <param name="Scratch">The scratch file to delete when no database was configured.</param>
/// <param name="SqlLog">The relational store's statements, for the Admin tab.</param>
/// <param name="StoreLog">The document store's operations, for the Admin tab.</param>
/// <param name="Accessor">The accessor both stores and the log providers read the current request through.</param>
/// <param name="CurrentRequest">The request describer the store logs file each operation under.</param>
/// <param name="Loggers">The host's loggers, once the host exists, for a context factory made later.</param>
public sealed record PreparedStores(
    Backends Backends,
    RelationalContexts Relational,
    CosmosStore? Document,
    ScratchDatabase Scratch,
    SqlRingBuffer SqlLog,
    StoreRingBuffer StoreLog,
    HttpContextAccessor Accessor,
    HttpCurrentRequest CurrentRequest,
    StoreLoggers Loggers);
// #endregion prepared-stores

/// <summary>The scratch SQLite file this process deletes on the way out, when no database was configured.</summary>
/// <param name="ScratchPath">The scratch file, or null when a database was configured.</param>
public sealed record ScratchDatabase(string? ScratchPath);

/// <summary>The document store, registered as a value because there may be none: null where no account is configured.</summary>
/// <param name="Store">The store, or null.</param>
public sealed record DocumentStore(CosmosStore? Store);
