using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TheBlock.Infrastructure;

/// <summary>
/// The relational store (ADR: The relational store). Three tables: the seed
/// catalogue, the photo manifest, and the bids, which are the only rows in
/// here that change after startup.
///
/// The context lives in Infrastructure because it is an adapter. Nothing above
/// this layer references EF Core except the composition root, which is the one
/// place that is supposed to: wiring an adapter means naming it. Application
/// and Domain do not, and the three ports they read through are the same three
/// whether a database is underneath them or a pair of JSON files is.
/// </summary>
public sealed class YardDbContext(DbContextOptions<YardDbContext> options)
    : IdentityDbContext<YardUser>(options)
{
    public DbSet<VehicleRow> Vehicles => Set<VehicleRow>();

    public DbSet<PhotoRow> Photos => Set<PhotoRow>();

    public DbSet<BidRow> Bids => Set<BidRow>();

    // #region model
    protected override void OnModelCreating(ModelBuilder model)
    {
        // Identity's own tables first. Skipping this call is the classic way to
        // get a context that compiles, migrates, and has nowhere to put a user.
        base.OnModelCreating(model);

        // Natural keys throughout. A vehicle already has an id the rest of the
        // system uses, a photo already has a unique file name, and a bid is one
        // per vehicle by definition, so a surrogate key here would be a second
        // identity to keep in step with the first.
        model.Entity<VehicleRow>(vehicle =>
        {
            vehicle.HasKey(row => row.Id);
            // Insertion order, kept explicitly. The synthetic scale-up expands
            // the seed catalogue deterministically from its order, so a set
            // that came back in a different order would be a different hundred
            // thousand vehicles. A table has no order of its own; this column
            // is what makes the answer the same every time.
            vehicle.Property(row => row.Seq).IsRequired();
            vehicle.HasIndex(row => row.Seq).IsUnique();
        });

        model.Entity<PhotoRow>(photo =>
        {
            photo.HasKey(row => row.File);
            photo.Property(row => row.Seq).IsRequired();
        });

        // A bid belongs to a person and to a vehicle, and one person has one
        // standing bid per vehicle, so the pair is the key. No surrogate id:
        // there is nothing else to identify a bid by, and a second identity
        // would be a second thing to keep in step (ADR: Accounts and per-user
        // bids).
        model.Entity<BidRow>(bid =>
        {
            bid.HasKey(row => new { row.UserId, row.VehicleId });
            // The only query this table serves that is not "load everything at
            // startup" is the user's own history.
            bid.HasIndex(row => row.UserId);
        });

        // No other indexes, deliberately. There are no queries to serve: both
        // catalogue tables are read whole, once, at startup, and every filter
        // and sort after that runs in memory over the result. An index here
        // would cost writes at seed time and earn nothing back.
    }
    // #endregion model
}

/// <summary>
/// What `dotnet ef` uses when it needs the model. Without this the tooling
/// boots the application to find a context, which would run the migrate and
/// seed block against a schema that does not exist yet, on the one command
/// whose whole job is to create that schema.
/// </summary>
public sealed class YardDbContextFactory : IDesignTimeDbContextFactory<YardDbContext>
{
    public YardDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<YardDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options);
}
