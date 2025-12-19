namespace KTPFileDistributor.Models;

/// <summary>
/// Result of distributing a file to a single server
/// </summary>
public class ServerUploadResult
{
    public string ServerName { get; set; } = string.Empty;
    public bool Success { get; set; }
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
    /// Total number of servers
    /// </summary>
    public int TotalServers => ServerResults.Count;

    /// <summary>
    /// Whether all servers were updated successfully
    /// </summary>
    public bool AllSuccessful => ServerResults.All(r => r.Success);

    /// <summary>
    /// Total bytes transferred (files * successful servers)
    /// </summary>
    public long TotalBytesTransferred => Files.Sum(f => f.FileSize) * SuccessCount;

    public string GetSummary()
    {
        var fileList = string.Join(", ", Files.Select(f => Path.GetFileName(f.RelativePath)));
        return $"{Files.Count} file(s) [{fileList}] -> {SuccessCount}/{TotalServers} servers in {TotalDuration.TotalSeconds:F1}s";
    }
}
