using KTPFileDistributor.Config;
using KTPFileDistributor.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KTPFileDistributor.Services;

/// <summary>
/// Background worker that watches for file changes and triggers distribution
/// </summary>
public class FileWatcherWorker : BackgroundService
{
    private readonly ILogger<FileWatcherWorker> _logger;
    private readonly AppSettings _settings;
    private readonly List<ServerConfig> _servers;
    private readonly SftpDistributorService _distributor;
    private readonly DiscordNotificationService _discord;
    private readonly ChangeDebouncer _debouncer;
    private FileSystemWatcher? _watcher;

    public FileWatcherWorker(
        ILogger<FileWatcherWorker> logger,
        IOptions<AppSettings> settings,
        IOptions<List<ServerConfig>> servers,
        SftpDistributorService distributor,
        DiscordNotificationService discord,
        ILogger<ChangeDebouncer> debouncerLogger)
    {
        _logger = logger;
        _settings = settings.Value;
        _servers = servers.Value;
        _distributor = distributor;
        _discord = discord;
        _debouncer = new ChangeDebouncer(debouncerLogger, _settings.DebounceDelayMs);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KTP File Distributor starting...");
        _logger.LogInformation("Watch directory: {Directory}", _settings.WatchDirectory);
        _logger.LogInformation("Patterns: {Patterns}", string.Join(", ", _settings.WatchPatterns));
        _logger.LogInformation("Target servers: {Count} ({Servers})",
            _servers.Count(s => s.Enabled),
            string.Join(", ", _servers.Where(s => s.Enabled).Select(s => s.Name)));

        // Validate watch directory
        if (!Directory.Exists(_settings.WatchDirectory))
        {
            _logger.LogError("Watch directory does not exist: {Directory}", _settings.WatchDirectory);
            _logger.LogInformation("Creating watch directory...");
            try
            {
                Directory.CreateDirectory(_settings.WatchDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Failed to create watch directory");
                return;
            }
        }

        // Wire up debouncer to distributor
        _debouncer.OnBatchReady += async changes =>
        {
            await ProcessChangesAsync(changes, stoppingToken);
        };

        // Set up file watcher
        SetupFileWatcher();

        // Send startup notification
        await _discord.NotifyStartupAsync(
            _settings.WatchDirectory,
            _servers.Count(s => s.Enabled),
            stoppingToken);

        _logger.LogInformation("File watcher started. Waiting for changes...");

        // Keep running until cancelled
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Shutdown requested");
        }
    }

    private void SetupFileWatcher()
    {
        _watcher = new FileSystemWatcher(_settings.WatchDirectory)
        {
            IncludeSubdirectories = _settings.IncludeSubdirectories,
            NotifyFilter = NotifyFilters.LastWrite
                         | NotifyFilters.FileName
                         | NotifyFilters.DirectoryName
                         | NotifyFilters.Size
        };

        _watcher.Created += OnFileChanged;
        _watcher.Changed += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Deleted += OnFileChanged;
        _watcher.Error += OnWatcherError;

        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Skip directories
        if (Directory.Exists(e.FullPath))
            return;

        // Check if file matches any of our patterns
        if (!MatchesPattern(e.Name ?? ""))
            return;

        var change = new FileChangeEvent
        {
            FullPath = e.FullPath,
            RelativePath = GetRelativePath(e.FullPath),
            ChangeType = e.ChangeType,
            DetectedAt = DateTime.UtcNow,
            FileSize = GetFileSize(e.FullPath)
        };

        _logger.LogInformation("File {ChangeType}: {Path}", e.ChangeType, change.RelativePath);
        _debouncer.AddChange(change);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Skip directories
        if (Directory.Exists(e.FullPath))
            return;

        // Check if new name matches any of our patterns
        if (!MatchesPattern(e.Name ?? ""))
            return;

        var change = new FileChangeEvent
        {
            FullPath = e.FullPath,
            RelativePath = GetRelativePath(e.FullPath),
            ChangeType = WatcherChangeTypes.Renamed,
            DetectedAt = DateTime.UtcNow,
            FileSize = GetFileSize(e.FullPath)
        };

        _logger.LogInformation("File Renamed: {OldPath} -> {NewPath}",
            GetRelativePath(e.OldFullPath), change.RelativePath);
        _debouncer.AddChange(change);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "File watcher error");

        // Try to restart the watcher
        try
        {
            _watcher?.Dispose();
            SetupFileWatcher();
            _logger.LogInformation("File watcher restarted");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to restart file watcher");
        }
    }

    private async Task ProcessChangesAsync(IReadOnlyList<FileChangeEvent> changes, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing {Count} file change(s)", changes.Count);

        try
        {
            var result = await _distributor.DistributeAsync(changes, cancellationToken);

            if (result.AllSuccessful)
            {
                _logger.LogInformation("Distribution successful: {Summary}", result.GetSummary());
            }
            else
            {
                _logger.LogWarning("Distribution partially failed: {Summary}", result.GetSummary());
                foreach (var failure in result.ServerResults.Where(r => !r.Success))
                {
                    _logger.LogWarning("  {Server}: {Error}", failure.ServerName, failure.ErrorMessage);
                }
            }

            await _discord.NotifyDistributionResultAsync(result, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during distribution");
        }
    }

    private bool MatchesPattern(string fileName)
    {
        if (_settings.WatchPatterns.Count == 0 || _settings.WatchPatterns.Contains("*.*"))
            return true;

        foreach (var pattern in _settings.WatchPatterns)
        {
            // Simple glob matching
            if (pattern.StartsWith("*."))
            {
                var extension = pattern[1..]; // e.g., ".amxx"
                if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (pattern.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string GetRelativePath(string fullPath)
    {
        return Path.GetRelativePath(_settings.WatchDirectory, fullPath);
    }

    private static long GetFileSize(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }
        }
        catch
        {
            // File might be locked or deleted
        }
        return 0;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping file watcher...");

        _watcher?.Dispose();
        _debouncer.Dispose();

        await _discord.NotifyShutdownAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
