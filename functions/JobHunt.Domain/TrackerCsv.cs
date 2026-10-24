namespace JobHunt.Domain;

/// <summary>
/// Counts the data rows of Job_Hunt_Tracker.csv. Its quoted fields hold newlines, so a line
/// count is wrong (failure-log 2026-09-03); this walks the quotes the way csv.reader does.
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
