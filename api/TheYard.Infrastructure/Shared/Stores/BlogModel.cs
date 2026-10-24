// The model for the blog's five tables (ADR: Every LinkedIn post is also a blog post). Its own type, called from
// YardDbContext.OnModelCreating, so the blog's tables change here and nowhere else. On SQL Server they sit in the blog schema the SQL
// project declares; SQLite has no schemas, so there they are plain tables of the same names.
using Microsoft.EntityFrameworkCore;

namespace TheYard.Infrastructure.Shared.Stores;

/// <summary>The blog's five tables' part of the relational model.</summary>
public static class BlogModel
{
    // #region blog-model
    /// <summary>The blog's tables, in the blog schema on SQL Server.</summary>
    internal static void ConfigureBlog(ModelBuilder model, bool sqlServer)
    {
        string? schema = sqlServer ? "blog" : null;
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
            post.HasIndex(row => row.PublishedAt);
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
            view.HasIndex(row => new { row.PostId, row.ViewedAt });
            view.HasOne<PostRow>().WithMany().HasForeignKey(row => row.PostId).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<PostEventRow>(evt =>
        {
            evt.ToTable("PostEvent", schema);
            evt.HasKey(row => row.Id);
            evt.Property(row => row.SessionHash).HasMaxLength(32).IsFixedLength();
            evt.Property(row => row.Type).HasMaxLength(16).IsUnicode(false);
            evt.Property(row => row.Value).HasMaxLength(200);
            evt.HasIndex(row => new { row.PostId, row.Type });
            evt.HasOne<PostRow>().WithMany().HasForeignKey(row => row.PostId).OnDelete(DeleteBehavior.NoAction);
        });
    }
    // #endregion blog-model
}
