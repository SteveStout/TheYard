using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TheBlock.Infrastructure;

namespace TheBlock.Tests;

/// <summary>
/// The SQL Server schema, asserted without a SQL Server
/// (ADR: The SQL Server backend).
///
/// Every test in this class builds the model from the SQL Server provider and
/// reads it, or generates the CREATE script from it, and none of them opens a
/// connection. That is deliberate and it is what makes them runnable: CI has no
/// Azure credential and is never getting one, so a schema that could only be
/// checked against the real database would be a schema nothing checks.
///
/// What this cannot prove is that Azure accepts the DDL. That is proved once,
/// by the container, when it migrates on its first boot against the real
/// database, and the health check says so.
/// </summary>
public class SqlServerModelTests
{
    private static YardDbContext SqlServer() =>
        new(new YardConnection(YardProvider.SqlServer, "Server=none;Initial Catalog=none;").Options());

    private static YardDbContext Sqlite() =>
        new(new YardConnection(YardProvider.Sqlite, "Data Source=:memory:").Options());

    // #region provider-choice
    [Theory]
    [InlineData(null, YardProvider.Sqlite)]
    [InlineData("", YardProvider.Sqlite)]
    [InlineData("   ", YardProvider.Sqlite)]
    // A deploy whose substitution step failed leaves the literal placeholder.
    // Reading it as "no SQL Server here" is what makes a broken deploy fall back
    // to the file instead of crash-looping against a string that is not a
    // connection string.
    [InlineData("__YARD_SQL_CONNECTION__", YardProvider.Sqlite)]
    [InlineData("Server=tcp:example.database.windows.net,1433;", YardProvider.SqlServer)]
    public void The_provider_is_chosen_by_whether_there_is_a_sql_server_to_talk_to(
        string? sqlServer, YardProvider expected)
    {
        var chosen = YardConnection.Choose(sqlServer, "Data Source=fallback.db");

        Assert.Equal(expected, chosen.Provider);
        Assert.Equal(
            expected == YardProvider.Sqlite ? "Data Source=fallback.db" : sqlServer,
            chosen.ConnectionString);
    }
    // #endregion provider-choice

    [Fact]
    public void What_the_process_says_about_its_database_names_the_engine_and_nothing_else()
    {
        var connection = YardConnection.Choose(
            "Server=tcp:example.database.windows.net,1433;Initial Catalog=secret-name;User Id=someone;",
            "Data Source=fallback.db");

        string described = connection.Describe();

        Assert.Equal("Azure SQL Database", described);
        Assert.DoesNotContain("example.database.windows.net", described, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-name", described, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", described, StringComparison.Ordinal);
    }

    // #region lengths
    [Fact]
    public void Every_text_column_this_application_owns_has_a_length()
    {
        using var db = SqlServer();
        Type[] mine = [typeof(VehicleRow), typeof(PhotoRow), typeof(BidRow)];

        var unbounded = mine
            .Select(type => db.Model.FindEntityType(type)!)
            .SelectMany(entity => entity.GetProperties()
                .Where(property => IsTextColumn(property) && property.GetMaxLength() is null)
                .Select(property => $"{entity.ShortName()}.{property.Name}"))
            .ToList();

        // The failure message matters more than the assertion here: this test
        // exists to catch the next column somebody adds without a length, and
        // "Assert.Empty() failure" would not tell them which one.
        Assert.True(
            unbounded.Count == 0,
            $"these columns would be nvarchar(max) on SQL Server: {string.Join(", ", unbounded)}");
    }

    /// <summary>
    /// A string in C# that is still a string in the database. AuctionStart is
    /// the reason this is a method rather than a type check: the property is a
    /// string and the column is a datetime2, so asking it for a length would be
    /// asking the wrong question.
    /// </summary>
    private static bool IsTextColumn(IProperty property) =>
        property.ClrType == typeof(string)
        && (property.GetValueConverter()?.ProviderClrType ?? typeof(string)) == typeof(string);

    [Theory]
    [InlineData(nameof(VehicleRow.Id), 64)]
    [InlineData(nameof(VehicleRow.Vin), 17)]
    [InlineData(nameof(VehicleRow.Make), 64)]
    [InlineData(nameof(VehicleRow.Drivetrain), 16)]
    [InlineData(nameof(VehicleRow.ConditionReport), 1024)]
    public void The_catalogue_columns_are_the_length_the_data_needs(string property, int expected)
    {
        using var db = SqlServer();

        Assert.Equal(expected, db.Model.FindEntityType(typeof(VehicleRow))!.FindProperty(property)!.GetMaxLength());
    }

    [Fact]
    public void A_vin_is_seventeen_characters_of_ascii_because_a_standard_says_so()
    {
        using var db = SqlServer();
        var vin = db.Model.FindEntityType(typeof(VehicleRow))!.FindProperty(nameof(VehicleRow.Vin))!;

        Assert.Equal(17, vin.GetMaxLength());
        Assert.False(vin.IsUnicode());
        // Not fixed length: nchar and char pad on read, and a padded VIN would
        // stop equalling the one in the record it came from.
        Assert.NotEqual(true, vin.IsFixedLength());
    }
    // #endregion lengths

    // #region types
    [Fact]
    public void A_condition_grade_is_a_decimal_not_a_float()
    {
        using var db = SqlServer();
        var grade = db.Model.FindEntityType(typeof(VehicleRow))!
            .FindProperty(nameof(VehicleRow.ConditionGrade))!;

        Assert.Equal(3, grade.GetPrecision());
        Assert.Equal(1, grade.GetScale());
        // The domain record still says double. The persistence layer does not
        // get to change the shape of the thing it stores.
        Assert.Equal(typeof(double), grade.ClrType);
    }

    [Fact]
    public void An_auction_start_is_an_instant_in_the_database_and_a_string_everywhere_else()
    {
        using var db = SqlServer();
        var start = db.Model.FindEntityType(typeof(VehicleRow))!
            .FindProperty(nameof(VehicleRow.AuctionStart))!;

        Assert.Equal("datetime2(0)", start.GetColumnType());
        Assert.Equal(typeof(string), start.ClrType);
        Assert.NotNull(start.GetValueConverter());
    }

    [Fact]
    public void The_auction_start_converter_round_trips_every_row_in_the_dataset()
    {
        var vehicles = new JsonFileVehicleSource(Repo.DataFile("vehicles.json")).Load();
        Assert.True(vehicles.Count > 100, "the seed dataset should have been found and read");

        var converter = YardDbContext.AuctionStartToDateTime;
        var forward = converter.ConvertToProviderExpression.Compile();
        var back = converter.ConvertFromProviderExpression.Compile();

        foreach (var vehicle in vehicles)
        {
            Assert.Equal(vehicle.AuctionStart, back(forward(vehicle.AuctionStart)));
        }
    }

    [Fact]
    public void An_auction_start_the_dataset_could_not_produce_fails_loudly()
    {
        var forward = YardDbContext.AuctionStartToDateTime.ConvertToProviderExpression.Compile();

        // A silent fallback here would write the wrong instant into every row of
        // a regenerated dataset rather than stopping the boot that produced it.
        Assert.Throws<FormatException>(() => forward("2026-04-05"));
        Assert.Throws<FormatException>(() => forward("April 5 2026"));
    }
    // #endregion types

    // #region keys
    [Fact]
    public void A_bid_belongs_to_an_account_and_the_database_enforces_it()
    {
        using var db = SqlServer();
        var foreignKey = Assert.Single(db.Model.FindEntityType(typeof(BidRow))!.GetForeignKeys());

        Assert.Equal(nameof(BidRow.UserId), Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(typeof(YardUser), foreignKey.PrincipalEntityType.ClrType);
        // Deleting an account takes its bids with it. That is the right answer,
        // and it is now the database's answer rather than something the
        // application has to remember to do.
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void There_is_no_foreign_key_from_a_bid_to_the_catalogue_and_that_is_deliberate()
    {
        using var db = SqlServer();

        // The catalogue in the Vehicles table is 200 rows, expanded in memory to
        // 100,000 by SyntheticVehicleSource, and a visitor bids on the expanded
        // set. A constraint here would reject 99.8 per cent of legitimate bids.
        // It becomes correct the day the expansion is persisted, and this test
        // is what will fail and send somebody to the record when that day comes.
        Assert.DoesNotContain(
            db.Model.FindEntityType(typeof(BidRow))!.GetForeignKeys(),
            key => key.PrincipalEntityType.ClrType == typeof(VehicleRow));
    }

    [Fact]
    public void A_bid_is_keyed_on_the_pair_and_carries_no_second_copy_of_half_of_it()
    {
        using var db = SqlServer();
        var bids = db.Model.FindEntityType(typeof(BidRow))!;

        Assert.Equal(
            [nameof(BidRow.UserId), nameof(BidRow.VehicleId)],
            bids.FindPrimaryKey()!.Properties.Select(property => property.Name));

        // The primary key's leading column already answers "what has this person
        // bid on", so an index on UserId alone would be a second copy of the
        // first half of the key: a write on every bid, and nothing earned.
        Assert.DoesNotContain(
            bids.GetIndexes(),
            index => index.Properties.Count == 1 && index.Properties[0].Name == nameof(BidRow.UserId));
    }

    // #endregion keys

    // #region token
    [Fact]
    public void A_bid_carries_a_rowversion_on_sql_server()
    {
        using var db = SqlServer();
        var version = db.Model.FindEntityType(typeof(BidRow))!.FindProperty(nameof(BidRow.RowVersion))!;

        Assert.True(version.IsConcurrencyToken);
        // Database generated, so nothing in the application can forget to move it.
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        Assert.Equal("rowversion", version.GetColumnType());
    }

    [Fact]
    public void A_bid_carries_a_token_the_store_moves_on_sqlite()
    {
        using var db = Sqlite();
        var version = db.Model.FindEntityType(typeof(BidRow))!.FindProperty(nameof(BidRow.RowVersion))!;

        // Same guarantee, different owner: SQLite has no rowversion type, so
        // EfBidStore assigns one on every save.
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, version.ValueGenerated);
    }
    // #endregion token

    // #region resume-budget
    [Fact]
    public void A_connection_with_no_timeout_gets_a_minute_to_wake_the_database()
    {
        string widened = YardConnection.WithResumeBudget(
            "Server=tcp:sql-example.database.windows.net,1433;Initial Catalog=sqldb-example;"
            + "Authentication=Active Directory Managed Identity;User Id=00000000-0000-0000-0000-000000000000;"
            + "Encrypt=True");

        // Read the values back as values rather than matching on the text. The
        // builder normalises what it is handed: "Server" comes back as "Data
        // Source", "User Id" as "User ID", and "Active Directory Managed
        // Identity" as "ActiveDirectoryManagedIdentity". Both spellings are
        // accepted by the driver, and a test that asserted the spelling would
        // be asserting the builder's formatting rather than the setting that
        // reaches the database.
        var read = new SqlConnectionStringBuilder(widened);

        Assert.Equal(YardConnection.ConnectSeconds, read.ConnectTimeout);
        // The number is only right in company. It is multiplied by the retry
        // policy in Configure, and three attempts of it has to fit inside the
        // deploy's five minutes with the container's start-up still to pay for.
        Assert.True(3 * YardConnection.ConnectSeconds < 300, "three attempts must fit the deploy window");
        // Everything else survives. A helper that quietly dropped the
        // authentication mode would be worse than the timeout it fixed.
        Assert.Contains("sql-example.database.windows.net", read.DataSource, StringComparison.Ordinal);
        Assert.Equal("sqldb-example", read.InitialCatalog);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity, read.Authentication);
        Assert.Equal("True", read.Encrypt.ToString());
    }

    [Fact]
    public void A_timeout_somebody_chose_on_purpose_is_left_alone()
    {
        string widened = YardConnection.WithResumeBudget(
            "Server=tcp:sql-example.database.windows.net,1433;Initial Catalog=sqldb-example;Connect Timeout=15");

        Assert.Equal(15, new SqlConnectionStringBuilder(widened).ConnectTimeout);
    }
    // #endregion resume-budget
}
