-- The job hunt's own records beside the campaign (ADR: The campaign sends, the model writes rows): the tracker's tables when the tracker moves into SQL, one reader at a time. The Function's identity may
-- read and write this schema; the site's identity is denied it, as it is denied the campaign's.
CREATE SCHEMA [jobhunt];
GO
