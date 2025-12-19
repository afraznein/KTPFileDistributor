using KTPFileDistributor.Config;
using KTPFileDistributor.Models;
using KTPFileDistributor.Services;
using Microsoft.Extensions.Hosting.Systemd;
using Serilog;
using System.Text.Json;

// Configure Serilog early for startup logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting KTP File Distributor...");

    var builder = Host.CreateApplicationBuilder(args);

    // Configure Serilog from configuration
    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: Path.Combine(AppContext.BaseDirectory, "logs", "distributor-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

    // Load configuration
    builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));
    builder.Services.Configure<DiscordSettings>(builder.Configuration.GetSection("Discord"));

    // Load server configuration from servers.json
    var serversPath = Path.Combine(AppContext.BaseDirectory, "servers.json");
    if (File.Exists(serversPath))
    {
        var serversJson = File.ReadAllText(serversPath);
        var servers = JsonSerializer.Deserialize<List<ServerConfig>>(serversJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<ServerConfig>();

        builder.Services.Configure<List<ServerConfig>>(opt =>
        {
            opt.Clear();
            opt.AddRange(servers);
        });

        Log.Information("Loaded {Count} server(s) from servers.json", servers.Count);
    }
    else
    {
        Log.Warning("servers.json not found at {Path}. Creating example file...", serversPath);
        CreateExampleServerConfig(serversPath);
        builder.Services.Configure<List<ServerConfig>>(_ => { });
    }

    // Register services
    builder.Services.AddHttpClient<DiscordNotificationService>();
    builder.Services.AddSingleton<SftpDistributorService>();
    builder.Services.AddSingleton<DiscordNotificationService>();
    builder.Services.AddHostedService<FileWatcherWorker>();

    // Add systemd integration on Linux
    builder.Services.AddSystemd();

    // Build and run
    var host = builder.Build();

    if (OperatingSystem.IsLinux())
    {
        Log.Information("Running on Linux with systemd integration");
    }

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;

static void CreateExampleServerConfig(string path)
{
    var example = new List<ServerConfig>
    {
        new()
        {
            Name = "KTP Dallas 1",
            Host = "192.168.1.100",
            Port = 22,
            Username = "dod",
            Password = "your-password-here",
            RemoteBasePath = "/home/dod/server/dod",
            Enabled = true
        },
        new()
        {
            Name = "KTP Chicago 1",
            Host = "192.168.1.101",
            Port = 22,
            Username = "dod",
            PrivateKeyPath = "/path/to/id_rsa",
            RemoteBasePath = "/srv/dod",
            Enabled = true
        }
    };

    var json = JsonSerializer.Serialize(example, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });

    File.WriteAllText(path, json);
    Log.Information("Created example servers.json at {Path}", path);
}
