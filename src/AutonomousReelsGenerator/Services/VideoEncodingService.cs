using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AutonomousReelsGenerator.Services
{
    /// <summary>
    /// Stage 4: Encodes final video using FFmpeg with GPU acceleration (NVENC)
    /// </summary>
    public class VideoEncodingService
    {
        private readonly ILogger<VideoEncodingService> _logger;
        private readonly AppConfig _config;

        public VideoEncodingService(ILogger<VideoEncodingService> logger, AppConfig config)
        {
            _logger = logger;
            _config = config;
        }

        /// <summary>
        /// Encodes frames and audio into final MP4 video
        /// </summary>
        public async Task<string> EncodeVideoAsync(
            string[] framePaths,
            string? audioPath,
            string outputPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Starting video encoding: {Count} frames, Audio: {HasAudio}", 
                    framePaths.Length, !string.IsNullOrEmpty(audioPath));

                // Validate FFmpeg availability
                var ffmpegPath = await GetFFmpegPathAsync();
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    throw new InvalidOperationException("FFmpeg not found. Please ensure it's installed.");
                }

                // Create temporary file list for FFmpeg
                var tempFileList = Path.GetTempFileName();
                
                try
                {
                    await File.WriteAllLinesAsync(tempFileList, 
                        framePaths.Select(f => $"file '{f}'"), 
                        cancellationToken);

                    // Build FFmpeg command
                    var arguments = BuildFFmpegArguments(tempFileList, audioPath, outputPath);

                    _logger.LogDebug("FFmpeg arguments: {Args}", arguments);

                    // Execute FFmpeg
                    var success = await RunFFmpegAsync(ffmpegPath, arguments, progress, cancellationToken);

                    if (!success)
                    {
                        throw new Exception("FFmpeg encoding failed");
                    }

                    progress?.Report(100);

                    _logger.LogInformation("Video encoding completed successfully: {OutputPath}", outputPath);
                    
                    return outputPath;
                }
                finally
                {
                    // Cleanup temp file
                    if (File.Exists(tempFileList))
                    {
                        File.Delete(tempFileList);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Video encoding was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to encode video");
                throw;
            }
        }

        /// <summary>
        /// Builds FFmpeg command-line arguments
        /// </summary>
        private string BuildFFmpegArguments(string fileListPath, string? audioPath, string outputPath)
        {
            var args = System.Text.StringBuilder.Empty;
            
            // Input: frame sequence
            args += $" -f concat -safe 0 -i \"{fileListPath}\"";
            
            // Input: audio (if provided)
            if (!string.IsNullOrEmpty(audioPath))
            {
                args += $" -i \"{audioPath}\"";
            }

            // Video codec settings
            if (_config.UseHardwareEncoding)
            {
                // NVIDIA NVENC hardware encoding
                args += " -c:v h264_nvenc";
                args += $" -preset {_config.EncoderPreset}";
            }
            else
            {
                // Software encoding fallback
                args += " -c:v libx264";
                args += $" -preset {_config.EncoderPreset}";
            }

            // Output resolution and quality
            args += $" -vf scale={_config.OutputResolution}:-2";
            args += $" -b:v {_config.BitrateMbps}M";
            args += $" -r {_config.FrameRate}";

            // Audio codec settings
            if (!string.IsNullOrEmpty(audioPath))
            {
                args += " -c:a aac";
                args += " -b:a 192k";
                args += " -shortest"; // End when shortest stream ends
            }
            else
            {
                args += " -an"; // No audio
            }

            // Output format
            args += " -pix_fmt yuv420p";
            args += " -movflags +faststart";
            
            // Overwrite output
            args += " -y";
            
            // Output path
            args += $" \"{outputPath}\"";

            return args.ToString();
        }

        /// <summary>
        /// Runs FFmpeg process and captures progress
        /// </summary>
        private async Task<bool> RunFFmpegAsync(
            string ffmpegPath,
            string arguments,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = new Process { StartInfo = startInfo };
            
            var outputLines = new System.Collections.Generic.List<string>();
            
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    outputLines.Add(e.Data);
                    
                    // Parse FFmpeg progress from stderr
                    if (e.Data.Contains("time="))
                    {
                        var progressPercent = ParseFFmpegProgress(e.Data);
                        progress?.Report(progressPercent);
                    }
                }
            };

            process.Exited += (sender, e) =>
            {
                tcs.TrySetResult(process.ExitCode == 0);
            };

            try
            {
                process.Start();
                process.BeginErrorReadLine();
                
                cancellationToken.Register(() =>
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        tcs.TrySetCanceled(cancellationToken);
                    }
                });

                await process.WaitForExitAsync(cancellationToken);
                
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FFmpeg process error");
                return false;
            }
            finally
            {
                process.Dispose();
            }
        }

        /// <summary>
        /// Parses FFmpeg progress output to extract completion percentage
        /// </summary>
        private double ParseFFmpegProgress(string line)
        {
            // Example: time=00:00:15.50 bitrate=8000.0kbits/s speed=3.1x
            if (line.Contains("time="))
            {
                // Extract time value and calculate progress based on total duration
                // This is simplified - in production, track total duration
                return 50; // Placeholder
            }
            
            return 0;
        }

        /// <summary>
        /// Locates FFmpeg executable
        /// </summary>
        private async Task<string?> GetFFmpegPathAsync()
        {
            // Strategy 1: Check workspace FFmpeg directory
            var workspaceFFmpeg = Path.Combine(_config.WorkspacePath, "ffmpeg", "ffmpeg.exe");
            if (File.Exists(workspaceFFmpeg))
            {
                return workspaceFFmpeg;
            }

            // Strategy 2: Check PATH
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var testProcess = Process.Start(processStartInfo);
                if (testProcess != null)
                {
                    await testProcess.WaitForExitAsync();
                    if (testProcess.ExitCode == 0)
                    {
                        return "ffmpeg"; // Available in PATH
                    }
                }
            }
            catch
            {
                // FFmpeg not in PATH
            }

            // Strategy 3: Use configured path
            if (!string.IsNullOrEmpty(_config.FFmpegPath) && File.Exists(_config.FFmpegPath))
            {
                return _config.FFmpegPath;
            }

            return null;
        }

        /// <summary>
        /// Generates proxy images at lower resolution for faster processing
        /// </summary>
        public async Task<string[]> GenerateProxiesAsync(
            string[] sourcePaths,
            string outputDir,
            int targetResolution,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Generating proxies: {Count} files at {Resolution}px", 
                    sourcePaths.Length, targetResolution);

                Directory.CreateDirectory(outputDir);
                var proxyPaths = new System.Collections.Generic.List<string>();

                var ffmpegPath = await GetFFmpegPathAsync();
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    throw new InvalidOperationException("FFmpeg not found");
                }

                for (int i = 0; i < sourcePaths.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var source = sourcePaths[i];
                    var proxyName = Path.GetFileNameWithoutExtension(source) + "_proxy.jpg";
                    var proxyPath = Path.Combine(outputDir, proxyName);

                    var args = $"-i \"{source}\" -vf scale={targetResolution}:-2 -q:v 2 -y \"{proxyPath}\"";
                    
                    var success = await RunFFmpegAsync(ffmpegPath, args, null, cancellationToken);
                    
                    if (success)
                    {
                        proxyPaths.Add(proxyPath);
                    }

                    progress?.Report((i + 1) * 100 / sourcePaths.Length);
                }

                _logger.LogInformation("Proxy generation completed: {Count} proxies created", proxyPaths.Count);
                
                return proxyPaths.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate proxies");
                throw;
            }
        }
    }
}
