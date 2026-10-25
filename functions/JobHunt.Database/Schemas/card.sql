-- The campaign card's schema (ADR: The campaign sends, the model writes rows, Decision 6): counts only, never a
-- name, a company, an address, a subject or a code. The site's identity may read this schema and nothing else in
-- the campaign's database, so the card cannot show what the views do not hold.
CREATE SCHEMA [card];
GO
