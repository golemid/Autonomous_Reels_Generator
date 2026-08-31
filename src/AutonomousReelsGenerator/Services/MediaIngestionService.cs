using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AutonomousReelsGenerator.Services
{
    /// <summary>
    /// Handles media file ingestion and preprocessing
    /// </summary>
    public class MediaIngestionService
    {
        private readonly ILogger<MediaIngestionService> _logger;
        private readonly VideoEncodingService _videoEncodingService;
        private readonly AppConfig _config;

        public MediaIngestionService(
            ILogger<MediaIngestionService> logger,
            VideoEncodingService videoEncodingService,
            AppConfig config)
        {
            _logger = logger;
            _videoEncodingService = videoEncodingService;
            _config = config;
        }

        /// <summary>
        /// Ingests media files and generates proxies for processing
        /// </summary>
        public async Task<MediaIngestionResult> IngestMediaAsync(
            string[] mediaPaths,
            string sessionPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Starting media ingestion: {Count} files", mediaPaths.Length);

                var result = new MediaIngestionResult
                {
                    OriginalPaths = mediaPaths,
                    SessionPath = sessionPath
                };

                // Validate all files exist
                foreach (var path in mediaPaths)
                {
                    if (!File.Exists(path))
                    {
                        throw new FileNotFoundException($"Media file not found: {path}");
                    }
                }

                // Create proxy directory
                var proxyDir = Path.Combine(sessionPath, "proxies");
                Directory.CreateDirectory(proxyDir);

                // Generate 512px proxies for faster AI processing
                _logger.LogInformation("Generating {Resolution}px proxies...", _config.ProxyResolution);
                
                result.ProxyPaths = await _videoEncodingService.GenerateProxiesAsync(
                    mediaPaths,
                    proxyDir,
                    _config.ProxyResolution,
                    progress,
                    cancellationToken
                );

                // Categorize media by type
                result.ImagePaths = result.ProxyPaths.Where(IsImageFile).ToArray();
                result.VideoPaths = result.ProxyPaths.Where(IsVideoFile).ToArray();

                _logger.LogInformation("Media ingestion completed. Images: {Images}, Videos: {Videos}", 
                    result.ImagePaths.Length, result.VideoPaths.Length);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ingest media");
                throw;
            }
        }

        /// <summary>
        /// Validates that media files are supported formats
        /// </summary>
        public bool ValidateMediaFormats(string[] mediaPaths)
        {
            var supportedExtensions = new[]
            {
                // Images
                ".jpg", ".jpeg", ".png", ".webp", ".bmp",
                // Videos
                ".mp4", ".mov", ".avi", ".mkv", ".webm"
            };

            foreach (var path in mediaPaths)
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (!supportedExtensions.Contains(ext))
                {
                    _logger.LogWarning("Unsupported media format: {Ext} for file {File}", ext, path);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Extracts frames from video files
        /// </summary>
        public async Task<string[]> ExtractVideoFramesAsync(
            string videoPath,
            string outputDir,
            int? maxFrames = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Extracting frames from video: {VideoPath}", videoPath);

                Directory.CreateDirectory(outputDir);

                // Use FFmpeg to extract frames
                var framePattern = Path.Combine(outputDir, "frame_%04d.png");
                var fpsFilter = maxFrames.HasValue ? "fps=1/2" : "fps=30"; // Default: 1 frame every 2 seconds
                
                // This would use FFmpeg in production
                await Task.Delay(100, cancellationToken); // Placeholder

                _logger.LogInformation("Frame extraction completed");
                
                return Directory.GetFiles(outputDir, "frame_*.png");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract video frames");
                throw;
            }
        }

        private bool IsImageFile(string path)
        {
            var imageExts = new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };
            return imageExts.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        private bool IsVideoFile(string path)
        {
            var videoExts = new[] { ".mp4", ".mov", ".avi", ".mkv", ".webm" };
            return videoExts.Contains(Path.GetExtension(path).ToLowerInvariant());
        }
    }

    public class MediaIngestionResult
    {
        public string[] OriginalPaths { get; set; } = Array.Empty<string>();
        public string[] ProxyPaths { get; set; } = Array.Empty<string>();
        public string[] ImagePaths { get; set; } = Array.Empty<string>();
        public string[] VideoPaths { get; set; } = Array.Empty<string>();
        public string SessionPath { get; set; } = string.Empty;
    }
}
