// The model for the blog's eight tables (ADR: Every LinkedIn post is also a blog post). Its own type, called from
// YardDbContext.OnModelCreating, so the blog's tables change here and nowhere else. On SQL Server they sit in the blog schema the SQL
// project declares; SQLite has no schemas, so there they are plain tables of the same names.
using Microsoft.EntityFrameworkCore;

namespace TheYard.Infrastructure.Shared.Stores;

/// <summary>The blog's eight tables' part of the relational model.</summary>
public static class BlogModel
{
    // #region blog-model
    /// <summary>The blog's tables, in the blog schema on SQL Server.</summary>
    internal static void ConfigureBlog(ModelBuilder model, bool sqlServer)
    {
        string? schema = sqlServer ? "blog" : null;
        if (!sqlServer)
        {
            // SQLite has no instant type, and EF cannot order or compare a DateTimeOffset kept there as text, so the
            // blog's instants are kept as their UTC ticks: the list orders and pages on them and the counts cut their
            // windows on them in SQL, the same queries SQL Server runs on datetimeoffset.
            UtcTicks(model.Entity<PostRow>().Property(row => row.PublishedAt));
            UtcTicks(model.Entity<PostRow>().Property(row => row.CreatedAt));
            UtcTicks(model.Entity<PostRow>().Property(row => row.UpdatedAt));
            UtcTicks(model.Entity<PostViewRow>().Property(row => row.ViewedAt));
            UtcTicks(model.Entity<PostEventRow>().Property(row => row.At));
        }
        model.Entity<PostRow>(post =>
        {
            post.ToTable("Post", schema);
            post.HasKey(row => row.Id);
            post.Property(row => row.Slug).HasMaxLength(120);
            post.Property(row => row.Title).HasMaxLength(200);
            post.Property(row => row.Summary).HasMaxLength(400);
            post.Property(row => row.LinkedInUrl).HasMaxLength(400);
            post.Property(row => row.CodeUrl).HasMaxLength(400);
            post.HasIndex(row => row.Slug).IsUnique();
            post.HasIndex(row => row.LinkedInUrl).IsUnique();
            // The list's index: published posts newest first, covering what a card shows (the SQL project's
            // IX_Post_Published, filtered and with its INCLUDE on SQL Server, where the list reads it).
            var published = post.HasIndex(row => new { row.PublishedAt, row.Id }).HasDatabaseName("IX_Post_Published").IsDescending(true, true);
            if (sqlServer)
            {
                published.HasFilter("[IsPublished] = 1").IncludeProperties(row => new { row.Slug, row.Title, row.Summary, row.ReadingMinutes });
            }
        });
        model.Entity<PostImageRow>(image =>
        {
            image.ToTable("PostImage", schema);
            image.HasKey(row => new { row.PostId, row.Position });
            image.Property(row => row.Url).HasMaxLength(400);
            image.Property(row => row.AltText).HasMaxLength(300);
            image.HasOne<PostRow>().WithMany().HasForeignKey(row => row.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<PostTagRow>(tag =>
        {
            tag.ToTable("PostTag", schema);
            tag.HasKey(row => new { row.PostId, row.Tag });
            tag.Property(row => row.Tag).HasMaxLength(60);
            tag.HasOne<PostRow>().WithMany().HasForeignKey(row => row.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<PostViewRow>(view =>
        {
            view.ToTable("PostView", schema);
            view.HasKey(row => row.Id);
            view.Property(row => row.Source).HasMaxLength(16).IsUnicode(false);
            view.Property(row => row.RefCode).HasMaxLength(6).IsUnicode(false).IsFixedLength();
            view.Property(row => row.ReferrerHost).HasMaxLength(120).IsUnicode(false);
            view.Property(row => row.UtmSource).HasMaxLength(60).IsUnicode(false);
            view.Property(row => row.UtmMedium).HasMaxLength(60).IsUnicode(false);
            view.Property(row => row.UtmCampaign).HasMaxLength(60).IsUnicode(false);
            view.Property(row => row.Device).HasMaxLength(8).IsUnicode(false);
            view.Property(row => row.Country).HasMaxLength(2).IsUnicode(false).IsFixedLength();
            view.Property(row => row.SessionHash).HasMaxLength(32).IsFixedLength();
            // People only: no number the blog shows counts a bot, so both indexes leave the bots' rows out.
            var reads = view.HasIndex(row => new { row.PostId, row.ViewedAt }).HasDatabaseName("IX_PostView_Reads");
            var window = view.HasIndex(row => row.ViewedAt).HasDatabaseName("IX_PostView_Window");
            if (sqlServer)
            {
                reads.HasFilter("[IsBot] = 0").IncludeProperties(row => new { row.SessionHash, row.RefCode, row.Source });
                window.HasFilter("[IsBot] = 0").IncludeProperties(row => new { row.PostId, row.Source, row.SessionHash, row.RefCode });
            }
            view.HasOne<PostRow>().WithMany().HasForeignKey(row => row.PostId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<PostEventRow>(evt =>
        {
            evt.ToTable("PostEvent", schema);
            evt.HasKey(row => row.Id);
            evt.Property(row => row.SessionHash).HasMaxLength(32).IsFixedLength();
            evt.Property(row => row.Type).HasMaxLength(16).IsUnicode(false);
            evt.Property(row => row.Value).HasMaxLength(200);
            var events = evt.HasIndex(row => row.At).HasDatabaseName("IX_PostEvent_Window");
            if (sqlServer)
            {
                events.HasFilter("[IsBot] = 0").IncludeProperties(row => new { row.PostId, row.Type, row.SessionHash, row.Value });
            }
            evt.HasOne<PostRow>().WithMany().HasForeignKey(row => row.PostId).OnDelete(DeleteBehavior.NoAction);
        });
        ConfigureSearch(model, schema);
    }

    /// <summary>Keeps an instant as its UTC ticks, read back at offset zero; the instant is the same, the offset it was written with is not kept.</summary>
    private static void UtcTicks(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTimeOffset> instant) =>
        instant.HasConversion(value => value.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    /// <summary>The three tables the search and the keyword chips read: a post's words, its keywords, and the synonyms.</summary>
    private static void ConfigureSearch(ModelBuilder model, string? schema)
    {
        model.Entity<PostWordRow>(word =>
        {
            word.ToTable("PostWord", schema);
            word.HasKey(row => new { row.Word, row.PostId, row.Field });
            word.Property(row => row.Word).HasMaxLength(80);
            word.HasIndex(row => row.PostId).HasDatabaseName("IX_PostWord_PostId");
            word.HasOne<PostRow>().WithMany().HasForeignKey(row => row.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<PostKeywordRow>(keyword =>
        {
            keyword.ToTable("PostKeyword", schema);
            keyword.HasKey(row => new { row.PostId, row.Keyword });
            keyword.Property(row => row.Keyword).HasMaxLength(40).IsUnicode(false);
            keyword.HasIndex(row => new { row.Keyword, row.PostId }).HasDatabaseName("IX_PostKeyword_Keyword");
            keyword.HasOne<PostRow>().WithMany().HasForeignKey(row => row.PostId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SearchSynonymRow>(synonym =>
        {
            synonym.ToTable("SearchSynonym", schema);
            synonym.HasKey(row => new { row.Word, row.Term });
            synonym.Property(row => row.Word).HasMaxLength(40);
            synonym.Property(row => row.Term).HasMaxLength(80);
        });
    }
    // #endregion blog-model
}
