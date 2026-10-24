-- The hiring-manager email campaign's schema (ADR: The campaign sends, the model writes rows): the
-- companies it may write to, the leads the model writes, and every email the Function sent or, in
-- shadow, would have sent. One schema so a reader sees the campaign as one folder.
CREATE SCHEMA [campaign];
GO
