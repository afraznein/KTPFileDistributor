using System.Net.Http.Json;
using System.Text.Json;
using KTPFileDistributor.Config;
using KTPFileDistributor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KTPFileDistributor.Services;

/// <summary>
/// Sends notifications to Discord via the KTP Discord Relay
/// </summary>
public class DiscordNotificationService
{
    private readonly ILogger<DiscordNotificationService> _logger;
    private readonly DiscordSettings _settings;
    private readonly HttpClient _httpClient;

    public DiscordNotificationService(
        ILogger<DiscordNotificationService> logger,
        IOptions<DiscordSettings> settings,
        HttpClient httpClient)
    {
        _logger = logger;
        _settings = settings.Value;
        _httpClient = httpClient;
    }

    /// <summary>
    /// Send a distribution result notification to Discord
    /// </summary>
    public async Task NotifyDistributionResultAsync(DistributionResult result, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
            return;

        if (result.AllSuccessful && !_settings.NotifyOnSuccess)
            return;

        if (!result.AllSuccessful && !_settings.NotifyOnFailure)
            return;

        try
        {
            var embed = BuildResultEmbed(result);
            await SendMessageAsync(null, new[] { embed }, cancellationToken);
            _logger.LogDebug("Discord notification sent");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Discord notification");
        }
    }

    /// <summary>
    /// Send a startup notification
    /// </summary>
    public async Task NotifyStartupAsync(string watchDirectory, int serverCount, CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
            return;

        try
        {
            var embed = new
            {
                title = "KTP File Distributor Started",
                color = 3447003, // Blue
                fields = new[]
                {
                    new { name = "Watch Directory", value = $"`{watchDirectory}`", inline = true },
                    new { name = "Target Servers", value = serverCount.ToString(), inline = true }
                },
                timestamp = DateTime.UtcNow.ToString("O")
            };

            await SendMessageAsync(null, new[] { embed }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send startup notification");
        }
    }

    /// <summary>
    /// Send a shutdown notification
    /// </summary>
    public async Task NotifyShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Enabled)
            return;

        try
        {
            var embed = new
            {
                title = "KTP File Distributor Shutting Down",
                color = 15105570, // Orange
                timestamp = DateTime.UtcNow.ToString("O")
            };

            await SendMessageAsync(null, new[] { embed }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send shutdown notification");
        }
    }

    // Full detail stays in journalctl; the embed only needs enough to tell
    // servers apart. Paths are the long part -- 25 targets of full relative
    // paths blows the 1024-char field on the one failure that hits every host.
    private static string Summarize(string? message)
    {
        if (string.IsNullOrEmpty(message)) return "unknown error";
        return message.Length <= 90 ? message : message[..87] + "...";
    }

    private object BuildResultEmbed(DistributionResult result)
    {
        var status = result.AllSuccessful ? "SUCCESS" : "PARTIAL FAILURE";
        var color = result.AllSuccessful ? 5763719 : 15548997; // Green or Red

        var fileList = string.Join("\n", result.Files.Select(f =>
            $"- `{f.RelativePath}` ({FormatBytes(f.FileSize)})"));

        if (fileList.Length > 1000)
            fileList = fileList[..997] + "...";

        var serverStatus = string.Join("\n", result.ServerResults.Select(s =>
            s.Success
                ? $"- {s.ServerName} ({s.Duration.TotalSeconds:F1}s)"
                : $"- {s.ServerName} FAILED: {Summarize(s.ErrorMessage)}"));

        // Truncation has to say what it dropped. One bad file mode fails identically
        // on all 25 targets, which is exactly when the operator needs to see how wide
        // it went -- a bare "..." reads as a short list.
        if (serverStatus.Length > 1000)
        {
            var cut = serverStatus[..960].LastIndexOf('\n');
            if (cut < 0) cut = 960;
            var shown = serverStatus[..cut].Count(c => c == '\n') + 1;
            serverStatus = serverStatus[..cut]
                + $"\n... (+{result.ServerResults.Count - shown} more server(s))";
        }

        return new
        {
            title = $"File Distribution: {status}",
            color,
            fields = new object[]
            {
                new { name = "Files", value = fileList, inline = false },
                new { name = "Servers", value = $"{result.SuccessCount}/{result.TotalServers} successful", inline = true },
                new { name = "Duration", value = $"{result.TotalDuration.TotalSeconds:F1}s", inline = true },
                new { name = "Data Transferred", value = FormatBytes(result.TotalBytesTransferred), inline = true },
                new { name = "Server Details", value = serverStatus, inline = false }
            },
            timestamp = DateTime.UtcNow.ToString("O")
        };
    }

    private async Task SendMessageAsync(string? content, object[]? embeds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_settings.RelayUrl))
        {
            _logger.LogWarning("Discord relay URL not configured");
            return;
        }

        var channelIds = _settings.GetAllChannelIds().ToList();
        if (channelIds.Count == 0)
        {
            _logger.LogWarning("No Discord channel IDs configured");
            return;
        }

        // Send to all configured channels
        foreach (var channelId in channelIds)
        {
            try
            {
                var payload = new
                {
                    channelId,
                    content = content ?? "",
                    embeds
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, _settings.RelayUrl);
                request.Headers.Add("X-Relay-Auth", _settings.AuthSecret);
                request.Content = JsonContent.Create(payload);

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning("Discord relay returned {StatusCode} for channel {ChannelId}: {Body}",
                        response.StatusCode, channelId, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send to Discord channel {ChannelId}", channelId);
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F1} {sizes[order]}";
    }
}
