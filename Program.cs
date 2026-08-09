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
        builder.Services.AddScoped<IXmlService, XmlService>();
        builder.Services.AddScoped<ShotcutService>();
        builder.Services.AddScoped<FileSearchService>();

        // Video rendering services
        builder.Services.AddScoped<MeltRenderService>();
        builder.Services.AddSingleton<HardwareDetectionService>();

        // Database for render queue
        builder.Services.AddDbContext<RenderJobDbContext>(options =>
            options.UseSqlite("Data Source=renderjobs.db"));

        // Repositories
        builder.Services.AddScoped<IRenderJobRepository, RenderJobRepository>();

        // Queue infrastructure
        builder.Services.AddSingleton<IBackgroundTaskQueue>(_ =>
            new BackgroundTaskQueue(capacity: 100));

        // Render queue service (singleton for background service)
        builder.Services.AddSingleton<RenderQueueService>(serviceProvider =>
            new RenderQueueService(
                serviceProvider,
                serviceProvider.GetRequiredService<IBackgroundTaskQueue>(),
                maxConcurrentRenders: 1 // Configure: 1 for video rendering (CPU/GPU intensive)
            ));

        // Register as both IRenderQueueService and IHostedService
        builder.Services.AddSingleton<IRenderQueueService>(sp =>
            sp.GetRequiredService<RenderQueueService>());
        builder.Services.AddHostedService(sp =>
            sp.GetRequiredService<RenderQueueService>());

        // Initialization hosted services (run after app starts)
        builder.Services.AddHostedService<DatabaseInitializationService>();

        // Configure graceful shutdown
        builder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        // Run the app - all Avalonia complexity handled by the package
        builder.RunApp(args);
    }
}
