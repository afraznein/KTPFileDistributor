namespace KTPFileDistributor.Models;

/// <summary>
/// Result of distributing a file to a single server
/// </summary>
public class ServerUploadResult
{
    public string ServerName { get; set; } = string.Empty;
    public bool Success { get; set; }

    /// <summary>True when every file in the batch was filtered out for this server -- no connection was attempted, so Duration stays zero.</summary>
    public bool Skipped { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Result of distributing a batch of files to all servers
/// </summary>
public class DistributionResult
{
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime CompletedAt { get; set; }
    public TimeSpan TotalDuration => CompletedAt - StartedAt;

    /// <summary>
    /// Files that were distributed
    /// </summary>
    public List<FileChangeEvent> Files { get; set; } = new();

    /// <summary>
    /// Results per server
    /// </summary>
    public List<ServerUploadResult> ServerResults { get; set; } = new();

    /// <summary>
    /// Number of successful server uploads
    /// </summary>
    public int SuccessCount => ServerResults.Count(r => r.Success);

    /// <summary>
    /// Number of failed server uploads
    /// </summary>
    public int FailureCount => ServerResults.Count(r => !r.Success);

    /// <summary>
    /// Servers whose filter excluded every file in the batch -- never connected to, not a failure
    /// </summary>
    public int SkippedCount => ServerResults.Count(r => r.Skipped);

    /// <summary>
    /// Servers that actually received a file, as opposed to a filtered-out skip
    /// </summary>
    public int UploadedCount => ServerResults.Count(r => r.Success && !r.Skipped);

    /// <summary>
    /// Total number of servers
    /// </summary>
    public int TotalServers => ServerResults.Count;

    /// <summary>
    /// Whether all servers were updated successfully (a skip counts as successful, not a failure)
    /// </summary>
    public bool AllSuccessful => ServerResults.All(r => r.Success);

    /// <summary>
    /// Total bytes transferred (files * servers that actually uploaded -- a skip received nothing)
    /// </summary>
    public long TotalBytesTransferred => Files.Sum(f => f.FileSize) * UploadedCount;

    public string GetSummary()
    {
        var fileList = string.Join(", ", Files.Select(f => Path.GetFileName(f.RelativePath)));
        var skipped = SkippedCount > 0 ? $", {SkippedCount} skipped" : "";
        return $"{Files.Count} file(s) [{fileList}] -> {SuccessCount}/{TotalServers} servers{skipped} in {TotalDuration.TotalSeconds:F1}s";
    }
}
