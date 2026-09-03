using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TheBlock.Infrastructure;

/// <summary>
/// The relational store (ADR: The relational store, ADR: The SQL Server
/// backend). Three tables of its own, plus Identity's: the seed catalogue, the
/// photo manifest, and the bids, which are the only rows in here that change
/// after startup.
///
/// The context lives in Infrastructure because it is an adapter. Nothing above
/// this layer references EF Core except the composition root, which is the one
/// place that is supposed to: wiring an adapter means naming it. Application
/// and Domain do not, and the three ports they read through are the same three
/// whether Azure SQL Database is underneath them, a SQLite file is, or a pair
/// of JSON files is.
///
/// One model, two providers. The differences are real and they are all in
/// <see cref="OnModelCreating"/>, guarded by <c>IsSqlServer()</c>: a
/// <c>rowversion</c> column, a clustered index chosen for the query the
/// catalogue actually runs, and two column types SQLite does not have. That is
/// also why migrations are generated per provider into their own assemblies:
/// the two models are not the same model, so they cannot share one snapshot.
/// </summary>
public sealed class YardDbContext(DbContextOptions<YardDbContext> options)
    : IdentityDbContext<YardUser>(options)
{
    /// <summary>The 200-record seed catalogue. Read whole, once, at startup.</summary>
    public DbSet<VehicleRow> Vehicles => Set<VehicleRow>();

    /// <summary>The vendored stock-photo manifest. Read whole, once, at startup.</summary>
    public DbSet<PhotoRow> Photos => Set<PhotoRow>();

    /// <summary>One buyer's standing on one vehicle. The only table that changes after startup.</summary>
    public DbSet<BidRow> Bids => Set<BidRow>();

    // #region model
    protected override void OnModelCreating(ModelBuilder model)
    {
        // Identity's own tables first. Skipping this call is the classic way to
        // get a context that compiles, migrates, and has nowhere to put a user.
        base.OnModelCreating(model);

        // The two providers differ in what they can express, not in what the
        // application means. Everything below that is inside this flag is a
        // SQL Server feature SQLite has no equivalent for.
        bool sqlServer = Database.IsSqlServer();

        // Identity's key columns, narrowed from its default of 450. A clustered
        // index key is capped at 900 bytes on SQL Server and nvarchar(450) is
        // 900 bytes on its own, so Identity's own composite keys are over the
        // cap out of the box: the first publish of this schema warned that
        // PK_AspNetUserTokens was 2,700 bytes and PK_Bids was 1,028, each of
        // which fails an insert on a long enough value. The ids this
        // application creates are GUIDs. The widths here exist to match
        // api/TheBlock.Database, which is the authority, and a test holds them
        // to it (ADR: Data first, and the database in source control).
        model.Entity<YardUser>().Property(user => user.Id).HasMaxLength(IdentityKeyLength);
        model.Entity<IdentityRole>().Property(role => role.Id).HasMaxLength(IdentityKeyLength);
        model.Entity<IdentityUserClaim<string>>().Property(claim => claim.UserId).HasMaxLength(IdentityKeyLength);
        model.Entity<IdentityRoleClaim<string>>().Property(claim => claim.RoleId).HasMaxLength(IdentityKeyLength);
        model.Entity<IdentityUserLogin<string>>(login =>
        {
            login.Property(row => row.LoginProvider).HasMaxLength(IdentityKeyLength);
            login.Property(row => row.ProviderKey).HasMaxLength(IdentityKeyLength);
            login.Property(row => row.UserId).HasMaxLength(IdentityKeyLength);
        });
        model.Entity<IdentityUserRole<string>>(role =>
        {
            role.Property(row => row.UserId).HasMaxLength(IdentityKeyLength);
            role.Property(row => row.RoleId).HasMaxLength(IdentityKeyLength);
        });
        model.Entity<IdentityUserToken<string>>(token =>
        {
            token.Property(row => row.UserId).HasMaxLength(IdentityKeyLength);
            token.Property(row => row.LoginProvider).HasMaxLength(IdentityKeyLength);
            token.Property(row => row.Name).HasMaxLength(IdentityKeyLength);
        });

        model.Entity<VehicleRow>(vehicle =>
        {
            vehicle.ToTable("Vehicles");

            // Natural keys throughout. A vehicle already has an id the rest of
            // the system uses, a photo already has a unique file name, and a
            // bid is one per buyer per vehicle by definition, so a surrogate
            // key here would be a second identity to keep in step with the
            // first.
            vehicle.HasKey(row => row.Id);

            // Insertion order, kept explicitly. The synthetic scale-up expands
            // the seed catalogue deterministically from its order, so a set
            // that came back in a different order would be a different hundred
            // thousand vehicles. A table has no order of its own; this column
            // is what makes the answer the same every time.
            vehicle.Property(row => row.Seq).IsRequired();
            vehicle.HasIndex(row => row.Seq).IsUnique();

            // Which index is clustered is not decided here. On SQL Server the
            // physical design belongs to the SQL project, which puts the
            // clustered index on Seq because that is the only column this table
            // is ever read in order of, and leaves the primary key
            // nonclustered. This model maps to that schema and does not build it
            // (ADR: Data first, and the database in source control).

            vehicle.Property(row => row.Id).HasMaxLength(IdLength);

            // Seventeen characters, from a defined alphabet, by ISO 3779. This
            // is the one string in the catalogue whose length and character set
            // are fixed by a standard rather than by whatever the dataset
            // happens to hold, so it is the one that gets varchar rather than
            // nvarchar. Not fixed-length: nchar pads on read, and a padded
            // string would stop matching the record it came from.
            vehicle.Property(row => row.Vin).HasMaxLength(17).IsUnicode(false);

            vehicle.Property(row => row.Make).HasMaxLength(64);
            vehicle.Property(row => row.Model).HasMaxLength(64);
            vehicle.Property(row => row.Trim).HasMaxLength(64);
            vehicle.Property(row => row.BodyStyle).HasMaxLength(32);
            vehicle.Property(row => row.ExteriorColor).HasMaxLength(32);
            vehicle.Property(row => row.InteriorColor).HasMaxLength(32);
            vehicle.Property(row => row.Engine).HasMaxLength(128);
            vehicle.Property(row => row.Transmission).HasMaxLength(64);
            vehicle.Property(row => row.Drivetrain).HasMaxLength(16);
            vehicle.Property(row => row.FuelType).HasMaxLength(32);
            vehicle.Property(row => row.ConditionReport).HasMaxLength(1024);
            vehicle.Property(row => row.TitleStatus).HasMaxLength(32);
            vehicle.Property(row => row.Province).HasMaxLength(64);
            vehicle.Property(row => row.City).HasMaxLength(64);
            vehicle.Property(row => row.SellingDealership).HasMaxLength(128);
            vehicle.Property(row => row.Lot).HasMaxLength(32);

            if (sqlServer)
            {
                // A grade between 1.0 and 5.0 to one decimal place. `float` can
                // represent 2.7 only approximately, and this number is compared
                // and displayed rather than accumulated, so the exact type is
                // the right one. The CLR property stays `double` because that is
                // what the domain record uses and the persistence layer does not
                // get to change the domain's shape.
                vehicle.Property(row => row.ConditionGrade).HasConversion<decimal>().HasPrecision(3, 1);

                // An instant, stored as an instant. The dataset carries it as
                // `2026-04-05T19:00:00`, which is a local wall-clock time with no
                // zone, so `datetime2(0)` is exactly the type for it: seconds
                // precision, no offset, and sortable and comparable in the
                // database rather than only in C#. The property stays a string
                // for the same reason the grade stays a double.
                vehicle.Property(row => row.AuctionStart)
                    .HasConversion(AuctionStartToDateTime)
                    .HasColumnType("datetime2(0)");
            }

            // DamageNotes and Images are primitive collections: EF stores each
            // as a JSON array in one column. They are read and written whole and
            // never queried into, which is the case a JSON column is for. The
            // day something filters on a damage note is the day this becomes a
            // table, and not before (ADR: The SQL Server backend).
        });

        model.Entity<PhotoRow>(photo =>
        {
            photo.ToTable("Photos");
            photo.HasKey(row => row.File);
            photo.Property(row => row.Seq).IsRequired();
            photo.HasIndex(row => row.Seq).IsUnique();

            photo.Property(row => row.File).HasMaxLength(128);
            photo.Property(row => row.Style).HasMaxLength(32);
            photo.Property(row => row.Title).HasMaxLength(256);
        });

        model.Entity<BidRow>(bid =>
        {
            bid.ToTable("Bids");

            // A bid belongs to a person and to a vehicle, and one person has one
            // standing bid per vehicle, so the pair is the key
            // (ADR: Accounts and per-user bids).
            bid.HasKey(row => new { row.UserId, row.VehicleId });

            // Identity's key column is nvarchar(450) on SQL Server, and a
            // foreign key has to match the column it points at.
            bid.Property(row => row.UserId).HasMaxLength(IdentityKeyLength);
            bid.Property(row => row.VehicleId).HasMaxLength(IdLength);

            // A real foreign key, and the only one this model can honestly
            // carry. Deleting an account takes its bids with it, which is the
            // right answer and is now the database's answer rather than
            // something the application has to remember to do.
            bid.HasOne<YardUser>()
                .WithMany()
                .HasForeignKey(row => row.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // There is deliberately no foreign key from VehicleId to Vehicles.
            // The catalogue in this table is 200 rows expanded in memory to
            // 100,000 by SyntheticVehicleSource, and a visitor bids on the
            // expanded set, so 99.8 per cent of legitimate bids name a vehicle
            // id that has no row here. A constraint would reject them. The
            // constraint becomes correct the day the expansion is persisted,
            // and writing it before then would be writing a rule the
            // application breaks on purpose (ADR: The SQL Server backend).

            // No index on UserId either, and its absence is the point: the
            // primary key is (UserId, VehicleId), so its leading column already
            // answers every "what has this person bid on" query. The index that
            // used to be here was a second copy of the first half of the key,
            // costing a write on every bid and earning nothing.

            // The concurrency token. Two containers, or two requests that get
            // past one container's lock, can both read this row and both decide
            // to write it; the token is what makes the second write fail
            // instead of silently overwriting the first.
            var version = bid.Property(row => row.RowVersion);
            if (sqlServer)
            {
                // SQL Server keeps the token itself: `rowversion` is a database
                // generated, monotonically increasing 8-byte value, and nothing
                // in the application can forget to bump it.
                version.IsRowVersion();
            }
            else
            {
                // SQLite has no rowversion. The token still exists and still
                // fails a stale write; the difference is that the store assigns
                // it, which is why EfBidStore sets one on every save.
                version.IsConcurrencyToken();
            }
        });
    }
    // #endregion model

    /// <summary>
    /// Room for a seed vehicle's id (36 characters today) and for the synthetic
    /// ids the scale-up derives from them, which add six more. Bids reference
    /// those, so the two columns are sized together.
    /// </summary>
    public const int IdLength = 64;

    /// <summary>
    /// What this application uses for Identity's key columns, narrowed from
    /// Identity's own default of 450 so that composite keys built from them stay
    /// inside SQL Server's 900-byte clustered index limit.
    /// </summary>
    public const int IdentityKeyLength = 128;

    /// <summary>The dataset's timestamp format: a local wall-clock instant to the second, with no zone.</summary>
    public const string AuctionStartFormat = "yyyy-MM-ddTHH:mm:ss";

    // #region auction-start
    /// <summary>
    /// The catalogue's `auction_start` on the wire is a string and in the
    /// database is a `datetime2(0)`. Round-tripping through this converter is
    /// exact for every row in the dataset, which a test asserts over all two
    /// hundred of them rather than over an example.
    /// </summary>
    public static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<string, DateTime>
        AuctionStartToDateTime = new(
            text => DateTime.ParseExact(text, AuctionStartFormat, System.Globalization.CultureInfo.InvariantCulture),
            moment => moment.ToString(AuctionStartFormat, System.Globalization.CultureInfo.InvariantCulture));
    // #endregion auction-start
}
