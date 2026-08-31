namespace TheBlock.Domain;

/// <summary>
/// FNV-1a 32-bit — the same stable id hash the frontend uses, so every value
/// derived from a vehicle id (photo galleries, auction windows) is identical
/// on both sides and survives restarts and reloads.
/// </summary>
public static class Fnv1a
{
    public static uint Hash(string input)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in input)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }
}
