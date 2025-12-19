namespace KTPFileDistributor.Models;

/// <summary>
/// Represents a file change that needs to be distributed
/// </summary>
public class FileChangeEvent
{
    /// <summary>
    /// Full local path to the file
    /// </summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>
    /// Relative path from the watch directory (used for remote path)
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Type of change (Created, Changed, Renamed, Deleted)
    /// </summary>
    public WatcherChangeTypes ChangeType { get; set; }

    /// <summary>
    /// When the change was detected
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// File size in bytes (0 for deleted files)
    /// </summary>
    public long FileSize { get; set; }

    public override string ToString() =>
        $"{ChangeType}: {RelativePath} ({FileSize:N0} bytes)";
}
