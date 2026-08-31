using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AutonomousReelsGenerator.Services
{
    public class PayloadExtractor
    {
        private readonly ILogger<PayloadExtractor> _logger;
        private readonly AppConfig _config;

        public PayloadExtractor(ILogger<PayloadExtractor> logger, AppConfig config)
        {
            _logger = logger;
            _config = config;
        }

        /// <summary>
        /// Extracts the embedded payload (FFmpeg, AI models, etc.) to the workspace
        /// </summary>
        public async Task<bool> ExtractPayloadAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Starting payload extraction...");

                // Ensure workspace directories exist
                _config.EnsureDirectoriesExist();

                // Check if payload archive exists in resources
                var payloadPath = FindEmbeddedPayload();
                
                if (string.IsNullOrEmpty(payloadPath))
                {
                    _logger.LogError("Embedded payload not found. This should only happen during development.");
                    // In development mode, skip extraction
                    return true;
                }

                var totalBytes = new FileInfo(payloadPath).Length;
                var extractedBytes = 0L;

                await Task.Run(() =>
                {
                    using (var archive = ZipFile.OpenRead(payloadPath))
                    {
                        var entries = archive.Entries;
                        var completedEntries = 0;

                        foreach (var entry in entries)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            var destinationPath = Path.Combine(_config.WorkspacePath, entry.FullName);
                            
                            // Create directory if needed
                            var destinationDir = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(destinationDir))
                            {
                                Directory.CreateDirectory(destinationDir);
                            }

                            // Skip directories
                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                continue;
                            }

                            // Extract file
                            entry.ExtractToFile(destinationPath, overwrite: true);
                            
                            extractedBytes += entry.CompressedLength;
                            completedEntries++;

                            var percentage = (double)extractedBytes / totalBytes * 100;
                            progress?.Report(percentage);

                            _logger.LogDebug("Extracted {EntryName} ({Completed}/{Total})", 
                                entry.Name, completedEntries, entries.Length);
                        }
                    }
                }, cancellationToken);

                _logger.LogInformation("Payload extraction completed successfully. Extracted {Bytes} bytes", extractedBytes);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Payload extraction was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract payload");
                return false;
            }
        }

        /// <summary>
        /// Finds the embedded payload archive in the application resources
        /// </summary>
        private string? FindEmbeddedPayload()
        {
            // Strategy 1: Check for payload in same directory as executable
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                var exeDir = Path.GetDirectoryName(exePath);
                var payloadCandidate = Path.Combine(exeDir ?? "", "payload.zip");
                
                if (File.Exists(payloadCandidate))
                {
                    return payloadCandidate;
                }
            }

            // Strategy 2: Check embedded resources
            // This would be populated during the build process
            var resourceName = "AutonomousReelsGenerator.Resources.payload.zip";
            var assembly = typeof(PayloadExtractor).Assembly;
            
            if (assembly.GetManifestResourceNames().Contains(resourceName))
            {
                // Extract to temp location
                var tempPayloadPath = Path.Combine(Path.GetTempPath(), "reels_payload_" + Guid.NewGuid() + ".zip");
                
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                using (var fileStream = File.Create(tempPayloadPath))
                {
                    stream?.CopyTo(fileStream);
                }
                
                return tempPayloadPath;
            }

            return null;
        }

        /// <summary>
        /// Validates that all required components were extracted
        /// </summary>
        public bool ValidateExtraction()
        {
            var requiredFiles = new[]
            {
                Path.Combine(_config.ModelsPath, _config.VisionLLMModelName),
                Path.Combine(_config.ModelsPath, _config.TTSModelName),
                Path.Combine(_config.ModelsPath, _config.TransitionModelName),
                Path.Combine(_config.WorkspacePath, "ffmpeg", "ffmpeg.exe")
            };

            foreach (var file in requiredFiles)
            {
                if (!File.Exists(file))
                {
                    _logger.LogWarning("Required file missing after extraction: {File}", file);
                    return false;
                }
            }

            _logger.LogInformation("Extraction validation passed");
            return true;
        }
    }
}
