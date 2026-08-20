using CheapAvaloniaBlazor.Hosting;
using CheapAvaloniaBlazor.Extensions;
using CheapShotcutRandomizer.Services;
using CheapShotcutRandomizer.Services.Queue;
using CheapShotcutRandomizer.Data;
using CheapShotcutRandomizer.Data.Repositories;
using CheapHelpers.Services.DataExchange.Xml;
using CheapHelpers.MediaProcessing.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;

namespace CheapShotcutRandomizer;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack must run first: it handles install/update/uninstall hooks and
        // exits early during those lifecycle events
        Velopack.VelopackApp.Build().Run();

        var builder = new CheapAvaloniaBlazor.Hosting.HostBuilder()
            .WithTitle("Cheap Shotcut Randomizer")
            .WithSize(1000, 800)
            .ConfigureOptions(options =>
            {
                options.EnableDevTools = false;
                options.EnableContextMenu = false;
            })
            .AddMudBlazor(config =>
            {
                config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
                config.SnackbarConfiguration.VisibleStateDuration = 2000; // 2 seconds instead of default 5
                config.SnackbarConfiguration.ShowTransitionDuration = 200;
                config.SnackbarConfiguration.HideTransitionDuration = 200;
            });

        // Register services
        builder.Services.AddSingleton<SvpDetectionService>();
        builder.Services.AddSingleton<ExecutableDetectionService>();
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<ProjectStateService>(); // Singleton to persist across page navigation
        builder.Services.AddSingleton<RenderJobDraftService>(); // Carries drafts from Randomizer to the add-job stepper
        builder.Services.AddSingleton<RenderProcessRegistry>(); // In-place pause/resume of melt processes
        builder.Services.AddSingleton<ExportPresetService>();   // Stock MLT export presets (YouTube etc.)
        builder.Services.AddSingleton<UpdateService>();         // Velopack auto-update
        builder.Services.AddScoped<IXmlService, XmlService>();
        builder.Services.AddScoped<ShotcutService>();
        builder.Services.AddScoped<FileSearchService>();

        // Video rendering services
        builder.Services.AddScoped<MeltRenderService>();
        builder.Services.AddSingleton<HardwareDetectionService>();

        // Database for render queue - in LocalAppData beside settings.json, so it doesn't
        // depend on the working directory (and works under a Program Files install)
        var dbFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CheapShotcutRandomizer");
        Directory.CreateDirectory(dbFolder);
        var dbPath = Path.Combine(dbFolder, "renderjobs.db");

        // One-time migration from the old working-directory-relative location.
        // WAL/SHM move first: if the main file move then fails, the old location is
        // still a complete database instead of a db stranded without its WAL.
        try
        {
            foreach (var dbFileSuffix in new[] { "-wal", "-shm", "" })
            {
                var legacyFile = "renderjobs.db" + dbFileSuffix;
                if (File.Exists(legacyFile) && !File.Exists(dbPath + dbFileSuffix))
                {
                    File.Move(legacyFile, dbPath + dbFileSuffix);
                }
            }
        }
        catch (Exception)
        {
            // Migration is best-effort; a locked or inaccessible legacy file just means a fresh queue DB
        }

        builder.Services.AddDbContext<RenderJobDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Repositories
        builder.Services.AddScoped<IRenderJobRepository, RenderJobRepository>();

        // Queue infrastructure
        builder.Services.AddSingleton<IBackgroundTaskQueue>(_ =>
            new BackgroundTaskQueue(capacity: 100));

        // Render queue service (singleton for background service).
        // Concurrency comes from settings; the factory runs during host start on a
        // threadpool thread, so the blocking settings read cannot deadlock a UI context.
        builder.Services.AddSingleton<RenderQueueService>(serviceProvider =>
        {
            var appSettings = serviceProvider.GetRequiredService<SettingsService>()
                .LoadSettingsAsync().GetAwaiter().GetResult();

            return new RenderQueueService(
                serviceProvider,
                serviceProvider.GetRequiredService<IBackgroundTaskQueue>(),
                maxConcurrentRenders: Math.Max(1, appSettings.MaxConcurrentRenders));
        });

        // Register as both IRenderQueueService and IHostedService
        builder.Services.AddSingleton<IRenderQueueService>(sp =>
            sp.GetRequiredService<RenderQueueService>());

        // Hosted services start in registration order: the database must exist
        // before the queue's crash recovery queries it
        builder.Services.AddHostedService<DatabaseInitializationService>();
        builder.Services.AddHostedService(sp =>
            sp.GetRequiredService<RenderQueueService>());

        // Configure graceful shutdown
        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        // Run the app - all Avalonia complexity handled by the package
        builder.RunApp(args);
    }
}
