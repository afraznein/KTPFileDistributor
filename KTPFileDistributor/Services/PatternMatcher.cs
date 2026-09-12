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

    /// <summary>
    /// True for a pattern containing a wildcard shape this matcher doesn't implement --
    /// "maps/*", "**/*.cfg", "*cfg", "*.cfg*" -- so it falls through to the exact-path
    /// branch and matches nothing, ever. Only the literal "*.*" and a single leading "*."
    /// with no further "*" are genuine extension patterns; a pattern with no "*" at all is
    /// a legitimate exact path and is not flagged. (A real filename containing a literal
    /// "*", e.g. "we*ird.cfg", is legal on Linux and would still match via the exact-path
    /// branch -- flagged here anyway, since that shape is otherwise indistinguishable from
    /// a broken glob and the cost of a spurious warning is low.)
    /// </summary>
    public static bool HasUnsupportedWildcardShape(string pattern) =>
        pattern.Contains('*')
        && pattern != "*.*"
        && !(pattern.StartsWith("*.") && !pattern.AsSpan(2).Contains('*'));

    /// <summary>
    /// Patterns in a configured list that <see cref="HasUnsupportedWildcardShape"/> flags.
    /// Tolerates a null list (an explicit `"excludePatterns": null` in servers.json) and a
    /// null element within it (`["*.cfg", null]`) -- this runs unconditionally at startup,
    /// unlike <see cref="MatchesAny"/>'s per-event use, so a crash here takes the whole
    /// service down rather than failing one file's match.
    /// </summary>
    public static IEnumerable<string> FindUnsupportedShapes(IEnumerable<string>? patterns) =>
        patterns?.Where(p => p is not null && HasUnsupportedWildcardShape(p)) ?? Enumerable.Empty<string>();

    /// <summary>
    /// Human-readable form of a pattern list for startup logging: <paramref name="emptyLabel"/>
    /// for both `null` and an empty list (JSON can produce either), otherwise a comma-joined
    /// list.
    /// </summary>
    public static string DescribeForLog(IReadOnlyCollection<string>? patterns, string emptyLabel) =>
        patterns is not { Count: > 0 } ? emptyLabel : string.Join(", ", patterns);
}
