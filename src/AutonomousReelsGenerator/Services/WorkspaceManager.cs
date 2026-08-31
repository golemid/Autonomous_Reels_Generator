using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AutonomousReelsGenerator.Services
{
    public class WorkspaceManager
    {
        private readonly ILogger<WorkspaceManager> _logger;
        private readonly AppConfig _config;

        public WorkspaceManager(ILogger<WorkspaceManager> logger, AppConfig config)
        {
            _logger = logger;
            _config = config;
        }

        /// <summary>
        /// Initializes the workspace directory structure
        /// </summary>
        public void InitializeWorkspace()
        {
            try
            {
                _config.EnsureDirectoriesExist();
                _logger.LogInformation("Workspace initialized at {WorkspacePath}", _config.WorkspacePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize workspace");
                throw;
            }
        }

        /// <summary>
        /// Checks if workspace is empty (first launch scenario)
        /// </summary>
        public bool IsWorkspaceEmpty()
        {
            try
            {
                if (!Directory.Exists(_config.WorkspacePath))
                {
                    return true;
                }

                var files = Directory.GetFiles(_config.WorkspacePath, "*.*", SearchOption.AllDirectories);
                return files.Length == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking workspace status");
                return true; // Assume empty on error to trigger extraction
            }
        }

        /// <summary>
        /// Cleans up temporary files from previous generations
        /// </summary>
        public async Task CleanupTemporaryFilesAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(_config.TempPath))
                    {
                        return;
                    }

                    _logger.LogInformation("Cleaning up temporary files...");

                    var tempDir = new DirectoryInfo(_config.TempPath);
                    
                    foreach (var file in tempDir.GetFiles())
                    {
                        try
                        {
                            file.Delete();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete temp file: {File}", file.Name);
                        }
                    }

                    foreach (var dir in tempDir.GetDirectories())
                    {
                        try
                        {
                            dir.Delete(true);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete temp directory: {Dir}", dir.Name);
                        }
                    }

                    _logger.LogInformation("Temporary files cleanup completed");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during temporary files cleanup");
                }
            });
        }

        /// <summary>
        /// Creates a new generation session with unique ID
        /// </summary>
        public string CreateGenerationSession()
        {
            var sessionId = Guid.NewGuid().ToString("N")[..8];
            var sessionPath = Path.Combine(_config.TempPath, $"session_{sessionId}");
            
            Directory.CreateDirectory(sessionPath);
            Directory.CreateDirectory(Path.Combine(sessionPath, "proxies"));
            Directory.CreateDirectory(Path.Combine(sessionPath, "frames"));
            Directory.CreateDirectory(Path.Combine(sessionPath, "audio"));
            Directory.CreateDirectory(Path.Combine(sessionPath, "transitions"));

            _logger.LogInformation("Created generation session: {SessionId}", sessionId);
            return sessionId;
        }

        /// <summary>
        /// Gets the path for a specific generation session
        /// </summary>
        public string GetSessionPath(string sessionId)
        {
            return Path.Combine(_config.TempPath, $"session_{sessionId}");
        }

        /// <summary>
        /// Deletes a generation session and all its files
        /// </summary>
        public async Task DeleteGenerationSessionAsync(string sessionId)
        {
            await Task.Run(() =>
            {
                try
                {
                    var sessionPath = GetSessionPath(sessionId);
                    
                    if (Directory.Exists(sessionPath))
                    {
                        Directory.Delete(sessionPath, recursive: true);
                        _logger.LogInformation("Deleted generation session: {SessionId}", sessionId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete generation session: {SessionId}", sessionId);
                }
            });
        }

        /// <summary>
        /// Gets available disk space in MB
        /// </summary>
        public long GetAvailableDiskSpaceMB()
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(_config.WorkspacePath) ?? "C:\\");
                return drive.AvailableFreeSpace / (1024 * 1024);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get available disk space");
                return 0;
            }
        }

        /// <summary>
        /// Checks if there's enough disk space for operations
        /// </summary>
        public bool HasSufficientDiskSpace(long requiredMB)
        {
            var available = GetAvailableDiskSpaceMB();
            var hasSpace = available >= requiredMB;
            
            if (!hasSpace)
            {
                _logger.LogWarning("Insufficient disk space. Required: {Required}MB, Available: {Available}MB", 
                    requiredMB, available);
            }
            
            return hasSpace;
        }
    }
}
