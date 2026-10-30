namespace JobHunt.Domain.Shared.Tracker;

/// <summary>
/// Counts the data rows of Job_Hunt_Tracker.csv. Its quoted fields hold newlines of their own, so a
/// count of the file's lines reads high and reads high by a different amount every pass, which makes
/// every number built on it quietly wrong rather than obviously wrong. This walks the quotes the way
/// csv.reader does, so a newline inside a field is part of its row and not a row of its own.
/// </summary>
public static class TrackerCsv
{
    public static int DataRows(string text)
    {
        int records = 0;
        bool inQuotes = false;
        bool fieldSeen = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { i++; }
                    else { inQuotes = false; }
                }
                continue;
            }
            switch (c)
            {
                case '"': inQuotes = true; fieldSeen = true; break;
                case '\r': break;
                case '\n':
                    if (fieldSeen) { records++; }
                    fieldSeen = false;
                    break;
                default: fieldSeen = true; break;
            }
        }
        if (fieldSeen) { records++; }
        return Math.Max(0, records - 1);
    }
}
