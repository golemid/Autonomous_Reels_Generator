using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using AutonomousReelsGenerator.Services;
using AutonomousReelsGenerator.Config;

namespace AutonomousReelsGenerator
{
    public partial class App : Application
    {
        private readonly IHost _host;
        private bool _isFirstLaunch;

        public App()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Configuration
                    services.AddSingleton<AppConfig>();
                    
                    // Services
                    services.AddSingleton<WorkspaceManager>();
                    services.AddSingleton<PayloadExtractor>();
                    services.AddSingleton<VRAMManager>();
                    services.AddSingleton<ScriptGenerationService>();
                    services.AddSingleton<TTSService>();
                    services.AddSingleton<TransitionService>();
                    services.AddSingleton<VideoEncodingService>();
                    services.AddSingleton<MediaIngestionService>();
                    services.AddSingleton<GenerationOrchestrator>();
                    
                    // Views
                    services.AddTransient<MainWindow>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    Log.Logger = new LoggerConfiguration()
                        .WriteTo.File("logs/reels-generator-.log", rollingInterval: RollingInterval.Day)
                        .CreateLogger();
                    logging.AddSerilog();
                })
                .Build();

            _isFirstLaunch = CheckFirstLaunch();
        }

        private bool CheckFirstLaunch()
        {
            var workspacePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AutonomousReelsGenerator",
                "workspace"
            );

            return !Directory.Exists(workspacePath) || 
                   Directory.GetFiles(workspacePath, "*.*", SearchOption.AllDirectories).Length == 0;
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await _host.StartAsync();

            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            logger.LogInformation("Application starting...");

            if (_isFirstLaunch)
            {
                logger.LogInformation("First launch detected. Starting payload extraction...");
                
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.ShowExtractionUI();
            }
            else
            {
                logger.LogInformation("Subsequent launch. Loading from disk...");
                
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.Show();
            }

            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            logger.LogInformation("Application shutting down...");

            // Cleanup VRAM and temporary files
            var vramManager = _host.Services.GetRequiredService<VRAMManager>();
            await vramManager.ClearAllModelsAsync();

            var workspaceManager = _host.Services.GetRequiredService<WorkspaceManager>();
            await workspaceManager.CleanupTemporaryFilesAsync();

            await _host.StopAsync();
            _host.Dispose();

            base.OnExit(e);
        }
    }
}
