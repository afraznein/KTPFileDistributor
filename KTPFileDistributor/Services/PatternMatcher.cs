namespace KTPFileDistributor.Services;

/// <summary>
/// The one glob rule used everywhere: "*.*" matches anything, "*.ext" matches a path
/// ending in ".ext" (case-insensitive, any subdirectory), and anything else must equal
/// the whole watch-relative path. Watch patterns and per-server filters share it so a
/// pattern means the same thing in appsettings.json and servers.json.
/// </summary>
public static class PatternMatcher
{
    public static bool MatchesAny(string relativePath, IEnumerable<string>? patterns)
    {
        if (patterns is null)
            return false;

        foreach (var pattern in patterns)
        {
            if (pattern == "*.*")
                return true;

            if (pattern.StartsWith("*."))
            {
                if (relativePath.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (pattern.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Watch-pattern gate: an empty list watches everything.
    /// </summary>
    public static bool MatchesWatchPatterns(string relativePath, IReadOnlyCollection<string> watchPatterns) =>
        watchPatterns.Count == 0 || MatchesAny(relativePath, watchPatterns);
}
