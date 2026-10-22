namespace TheYard.Application;

// #region store-keys
/// <summary>
/// The two stores' short names, the keys a request header, a session claim, the store bar and every
/// registration use (ADR: The onion cut into vertical slices, rule 3). Written once here, so a third
/// store is one line in this file and its registration, never an edit in every place that asked
/// for one by name. In the Application ring because the use cases key their logs by it too.
/// </summary>
public static class StoreKeys
{
    /// <summary>The relational store: Azure SQL Database, or SQLite on a developer's machine.</summary>
    public const string Relational = "sql";

    /// <summary>The document store: Azure Cosmos DB.</summary>
    public const string Document = "cosmos";
}
// #endregion store-keys
