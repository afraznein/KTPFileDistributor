namespace KTPFileDistributor.Config;

/// <summary>
/// Application configuration settings
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Directory to watch for file changes
    /// </summary>
    public string WatchDirectory { get; set; } = "/srv/ktp/sync";

    /// <summary>
    /// File patterns to watch (e.g., "*.amxx", "*.bsp", "*.cfg")
    /// Empty = watch all files
    /// </summary>
    public List<string> WatchPatterns { get; set; } = new() { "*.*" };

    /// <summary>
    /// Debounce delay in milliseconds - wait this long after last change before distributing
    /// </summary>
    public int DebounceDelayMs { get; set; } = 5000;

    /// <summary>
    /// Maximum concurrent SFTP connections
    /// </summary>
    public int MaxConcurrentUploads { get; set; } = 5;

    /// <summary>
    /// Retry count for failed uploads
    /// </summary>
    public int UploadRetryCount { get; set; } = 3;

    /// <summary>
    /// Delay between retries in milliseconds
    /// </summary>
    public int RetryDelayMs { get; set; } = 2000;

    /// <summary>
    /// SFTP connection timeout in seconds
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to watch subdirectories
    /// </summary>
    public bool IncludeSubdirectories { get; set; } = true;
}

/// <summary>
/// Discord notification settings
/// </summary>
public class DiscordSettings
{
    /// <summary>
    /// Discord relay URL (e.g., "https://your-relay.run.app/reply")
    /// </summary>
    public string RelayUrl { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret for relay authentication
    /// </summary>
    public string AuthSecret { get; set; } = string.Empty;

    /// <summary>
    /// Channel ID to post notifications to
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Whether Discord notifications are enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Whether to send notifications for successful distributions
    /// </summary>
    public bool NotifyOnSuccess { get; set; } = true;

    /// <summary>
    /// Whether to send notifications for failed distributions
    /// </summary>
    public bool NotifyOnFailure { get; set; } = true;
}
