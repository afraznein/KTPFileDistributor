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
            await SendMessageAsync("KTP File Distributor shutting down", null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send shutdown notification");
        }
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
                : $"- {s.ServerName} FAILED: {s.ErrorMessage}"));

        if (serverStatus.Length > 1000)
            serverStatus = serverStatus[..997] + "...";

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
        if (string.IsNullOrEmpty(_settings.RelayUrl) || string.IsNullOrEmpty(_settings.ChannelId))
        {
            _logger.LogWarning("Discord relay not configured");
            return;
        }

        var payload = new
        {
            channelId = _settings.ChannelId,
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
            _logger.LogWarning("Discord relay returned {StatusCode}: {Body}", response.StatusCode, body);
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
