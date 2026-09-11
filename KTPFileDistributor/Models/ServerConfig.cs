using KTPFileDistributor.Services;

namespace KTPFileDistributor.Models;

/// <summary>
/// Configuration for a target game server
/// </summary>
public class ServerConfig
{
    /// <summary>
    /// Display name for this server (e.g., "KTP Dallas 1")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// SFTP hostname or IP address
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// SFTP port (default: 22)
    /// </summary>
    public int Port { get; set; } = 22;

    /// <summary>
    /// SFTP username
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// SFTP password (if using password auth)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Path to SSH private key file (if using key auth)
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Passphrase for the private key (if encrypted)
    /// </summary>
    public string? PrivateKeyPassphrase { get; set; }

    /// <summary>
    /// Base remote path where files should be uploaded
    /// e.g., "/home/dod/server" or "/srv/dod"
    /// </summary>
    public string RemoteBasePath { get; set; } = "/";

    /// <summary>
    /// Whether this server is enabled for distribution
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// If set, only paths matching one of these are sent to this server. Empty (the
    /// default) sends everything. Same pattern rules as WatchPatterns.
    /// </summary>
    public List<string> IncludePatterns { get; set; } = new();

    /// <summary>
    /// Paths matching any of these are never uploaded to, or deleted from, this server.
    /// Checked after IncludePatterns, so an exclude always wins.
    /// </summary>
    public List<string> ExcludePatterns { get; set; } = new();

    /// <summary>
    /// Whether a watch-relative path is distributed to this server at all.
    /// </summary>
    public bool Accepts(string relativePath) =>
        (IncludePatterns is not { Count: > 0 } || PatternMatcher.MatchesAny(relativePath, IncludePatterns))
        && !PatternMatcher.MatchesAny(relativePath, ExcludePatterns);
}
