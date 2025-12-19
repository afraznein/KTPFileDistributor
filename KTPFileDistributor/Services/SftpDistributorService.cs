using System.Diagnostics;
using KTPFileDistributor.Config;
using KTPFileDistributor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace KTPFileDistributor.Services;

/// <summary>
/// Handles SFTP distribution of files to multiple servers in parallel
/// </summary>
public class SftpDistributorService
{
    private readonly ILogger<SftpDistributorService> _logger;
    private readonly AppSettings _settings;
    private readonly List<ServerConfig> _servers;
    private readonly SemaphoreSlim _semaphore;

    public SftpDistributorService(
        ILogger<SftpDistributorService> logger,
        IOptions<AppSettings> settings,
        IOptions<List<ServerConfig>> servers)
    {
        _logger = logger;
        _settings = settings.Value;
        _servers = servers.Value.Where(s => s.Enabled).ToList();
        _semaphore = new SemaphoreSlim(_settings.MaxConcurrentUploads);
    }

    /// <summary>
    /// Distribute a batch of files to all enabled servers
    /// </summary>
    public async Task<DistributionResult> DistributeAsync(
        IReadOnlyList<FileChangeEvent> files,
        CancellationToken cancellationToken = default)
    {
        var result = new DistributionResult
        {
            StartedAt = DateTime.UtcNow,
            Files = files.ToList()
        };

        if (_servers.Count == 0)
        {
            _logger.LogWarning("No enabled servers configured for distribution");
            result.CompletedAt = DateTime.UtcNow;
            return result;
        }

        _logger.LogInformation(
            "Starting distribution of {FileCount} file(s) to {ServerCount} server(s)",
            files.Count, _servers.Count);

        // Upload to all servers in parallel (throttled by semaphore)
        var uploadTasks = _servers.Select(server =>
            UploadToServerAsync(server, files, cancellationToken));

        var serverResults = await Task.WhenAll(uploadTasks);
        result.ServerResults = serverResults.ToList();
        result.CompletedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Distribution complete: {Success}/{Total} servers, {Duration:F1}s",
            result.SuccessCount, result.TotalServers, result.TotalDuration.TotalSeconds);

        return result;
    }

    private async Task<ServerUploadResult> UploadToServerAsync(
        ServerConfig server,
        IReadOnlyList<FileChangeEvent> files,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new ServerUploadResult { ServerName = server.Name };

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            using var client = CreateSftpClient(server);

            for (int attempt = 1; attempt <= _settings.UploadRetryCount; attempt++)
            {
                try
                {
                    client.Connect();
                    _logger.LogDebug("Connected to {Server}", server.Name);

                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (file.ChangeType == WatcherChangeTypes.Deleted)
                        {
                            await DeleteFileAsync(client, server, file);
                        }
                        else
                        {
                            await UploadFileAsync(client, server, file);
                        }
                    }

                    result.Success = true;
                    _logger.LogDebug(
                        "Uploaded {Count} file(s) to {Server} in {Duration}ms",
                        files.Count, server.Name, stopwatch.ElapsedMilliseconds);
                    break;
                }
                catch (Exception ex) when (attempt < _settings.UploadRetryCount)
                {
                    _logger.LogWarning(
                        ex, "Attempt {Attempt}/{Max} failed for {Server}, retrying...",
                        attempt, _settings.UploadRetryCount, server.Name);

                    if (client.IsConnected)
                        client.Disconnect();

                    await Task.Delay(_settings.RetryDelayMs, cancellationToken);
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                    _logger.LogError(
                        ex, "Failed to upload to {Server} after {Attempts} attempts",
                        server.Name, _settings.UploadRetryCount);
                }
                finally
                {
                    if (client.IsConnected)
                        client.Disconnect();
                }
            }
        }
        finally
        {
            _semaphore.Release();
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
        }

        return result;
    }

    private SftpClient CreateSftpClient(ServerConfig server)
    {
        ConnectionInfo connectionInfo;

        if (!string.IsNullOrEmpty(server.PrivateKeyPath))
        {
            // Key-based authentication
            var keyFile = string.IsNullOrEmpty(server.PrivateKeyPassphrase)
                ? new PrivateKeyFile(server.PrivateKeyPath)
                : new PrivateKeyFile(server.PrivateKeyPath, server.PrivateKeyPassphrase);

            connectionInfo = new ConnectionInfo(
                server.Host,
                server.Port,
                server.Username,
                new PrivateKeyAuthenticationMethod(server.Username, keyFile));
        }
        else
        {
            // Password authentication
            connectionInfo = new ConnectionInfo(
                server.Host,
                server.Port,
                server.Username,
                new PasswordAuthenticationMethod(server.Username, server.Password ?? ""));
        }

        connectionInfo.Timeout = TimeSpan.FromSeconds(_settings.ConnectionTimeoutSeconds);

        return new SftpClient(connectionInfo);
    }

    private async Task UploadFileAsync(SftpClient client, ServerConfig server, FileChangeEvent file)
    {
        var remotePath = BuildRemotePath(server.RemoteBasePath, file.RelativePath);
        var remoteDir = Path.GetDirectoryName(remotePath)?.Replace('\\', '/') ?? "/";

        // Ensure directory exists
        EnsureRemoteDirectoryExists(client, remoteDir);

        // Upload file
        await using var fileStream = File.OpenRead(file.FullPath);
        client.UploadFile(fileStream, remotePath, true);

        _logger.LogDebug("Uploaded {File} to {Server}:{Path}", file.RelativePath, server.Name, remotePath);
    }

    private Task DeleteFileAsync(SftpClient client, ServerConfig server, FileChangeEvent file)
    {
        var remotePath = BuildRemotePath(server.RemoteBasePath, file.RelativePath);

        try
        {
            if (client.Exists(remotePath))
            {
                client.DeleteFile(remotePath);
                _logger.LogDebug("Deleted {File} from {Server}", file.RelativePath, server.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete {File} from {Server}", file.RelativePath, server.Name);
        }

        return Task.CompletedTask;
    }

    private static string BuildRemotePath(string basePath, string relativePath)
    {
        // Normalize to forward slashes for Linux
        var normalized = relativePath.Replace('\\', '/');
        return $"{basePath.TrimEnd('/')}/{normalized.TrimStart('/')}";
    }

    private void EnsureRemoteDirectoryExists(SftpClient client, string remotePath)
    {
        var parts = remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentPath = "";

        foreach (var part in parts)
        {
            currentPath = $"{currentPath}/{part}";
            try
            {
                if (!client.Exists(currentPath))
                {
                    client.CreateDirectory(currentPath);
                    _logger.LogDebug("Created remote directory: {Path}", currentPath);
                }
            }
            catch
            {
                // Directory might already exist, continue
            }
        }
    }
}
