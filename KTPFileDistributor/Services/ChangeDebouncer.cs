using System.Collections.Concurrent;
using KTPFileDistributor.Models;
using Microsoft.Extensions.Logging;

namespace KTPFileDistributor.Services;

/// <summary>
/// Debounces file change events to batch them together
/// Waits for a quiet period before triggering distribution
/// </summary>
public class ChangeDebouncer : IDisposable
{
    private readonly ILogger<ChangeDebouncer> _logger;
    private readonly int _debounceDelayMs;
    private readonly ConcurrentDictionary<string, FileChangeEvent> _pendingChanges = new();
    private readonly object _timerLock = new();
    private Timer? _debounceTimer;
    private bool _disposed;

    public event Func<IReadOnlyList<FileChangeEvent>, Task>? OnBatchReady;

    public ChangeDebouncer(ILogger<ChangeDebouncer> logger, int debounceDelayMs = 5000)
    {
        _logger = logger;
        _debounceDelayMs = debounceDelayMs;
    }

    /// <summary>
    /// Add a file change event to the pending batch
    /// </summary>
    public void AddChange(FileChangeEvent change)
    {
        // Use relative path as key to deduplicate changes to the same file
        _pendingChanges.AddOrUpdate(
            change.RelativePath,
            change,
            (_, existing) =>
            {
                // Keep the latest change type, but preserve earliest detection time
                return new FileChangeEvent
                {
                    FullPath = change.FullPath,
                    RelativePath = change.RelativePath,
                    ChangeType = change.ChangeType,
                    DetectedAt = existing.DetectedAt,
                    FileSize = change.FileSize
                };
            });

        _logger.LogDebug("Queued change: {Change}, pending: {Count}", change, _pendingChanges.Count);

        // Reset the debounce timer
        ResetTimer();
    }

    private void ResetTimer()
    {
        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(OnTimerElapsed, null, _debounceDelayMs, Timeout.Infinite);
        }
    }

    private async void OnTimerElapsed(object? state)
    {
        if (_pendingChanges.IsEmpty)
            return;

        // Snapshot and clear pending changes
        var changes = new List<FileChangeEvent>();
        foreach (var key in _pendingChanges.Keys.ToList())
        {
            if (_pendingChanges.TryRemove(key, out var change))
            {
                changes.Add(change);
            }
        }

        if (changes.Count == 0)
            return;

        _logger.LogInformation("Debounce complete. Processing {Count} file change(s)", changes.Count);

        try
        {
            if (OnBatchReady != null)
            {
                await OnBatchReady.Invoke(changes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing batch of {Count} changes", changes.Count);
        }
    }

    /// <summary>
    /// Get current pending change count
    /// </summary>
    public int PendingCount => _pendingChanges.Count;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
