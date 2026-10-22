-- The blog's own schema (ADR: Every LinkedIn post is also a blog post): the posts, their images and
-- tags, and the two logs every read and every event is one row in, kept apart from the auction's tables
-- so a reader in SSMS sees the blog as one folder and the views can join the logs to the posts.
CREATE SCHEMA [blog];
GO
