using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AutonomousReelsGenerator.Services
{
    /// <summary>
    /// Orchestrates the complete video generation pipeline
    /// Manages sequential AI stages with VRAM-efficient model loading/unloading
    /// </summary>
    public class GenerationOrchestrator
    {
        private readonly ILogger<GenerationOrchestrator> _logger;
        private readonly WorkspaceManager _workspaceManager;
        private readonly MediaIngestionService _mediaIngestionService;
        private readonly ScriptGenerationService _scriptService;
        private readonly TTSService _ttsService;
        private readonly TransitionService _transitionService;
        private readonly VideoEncodingService _videoEncodingService;

        public GenerationOrchestrator(
            ILogger<GenerationOrchestrator> logger,
            WorkspaceManager workspaceManager,
            MediaIngestionService mediaIngestionService,
            ScriptGenerationService scriptService,
            TTSService ttsService,
            TransitionService transitionService,
            VideoEncodingService videoEncodingService)
        {
            _logger = logger;
            _workspaceManager = workspaceManager;
            _mediaIngestionService = mediaIngestionService;
            _scriptService = scriptService;
            _ttsService = ttsService;
            _transitionService = transitionService;
            _videoEncodingService = videoEncodingService;
        }

        /// <summary>
        /// Executes the complete video generation pipeline
        /// </summary>
        public async Task<GenerationResult> GenerateVideoAsync(
            string[] mediaPaths,
            GenerationSettings settings,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var sessionId = _workspaceManager.CreateGenerationSession();
            var sessionPath = _workspaceManager.GetSessionPath(sessionId);
            
            try
            {
                _logger.LogInformation("Starting video generation. Session: {SessionId}", sessionId);
                
                var result = new GenerationResult { SessionId = sessionId };

                // Stage 0: Media Ingestion & Proxy Generation
                _logger.LogInformation("[Stage 0/4] Ingesting media files...");
                progress?.Report(5);
                
                var ingestionResult = await _mediaIngestionService.IngestMediaAsync(
                    mediaPaths,
                    sessionPath,
                    CreateSubProgress(progress, 5, 15),
                    cancellationToken
                );
                
                result.IngestionResult = ingestionResult;

                // Stage 1: Script Generation (Vision LLM)
                _logger.LogInformation("[Stage 1/4] Generating script with Vision LLM...");
                progress?.Report(20);
                
                var script = await _scriptService.GenerateScriptAsync(
                    ingestionResult.ProxyPaths,
                    CreateSubProgress(progress, 20, 35),
                    cancellationToken
                );
                
                result.Script = script;

                // Stage 2: Audio Generation (TTS)
                _logger.LogInformation("[Stage 2/4] Generating audio with TTS...");
                progress?.Report(40);
                
                var audioPath = Path.Combine(sessionPath, "audio", "narration.wav");
                await _ttsService.GenerateAudioAsync(
                    script,
                    audioPath,
                    CreateSubProgress(progress, 40, 60),
                    cancellationToken
                );
                
                result.AudioPath = audioPath;

                // Optional: Generate background music
                if (settings.IncludeBackgroundMusic)
                {
                    var bgMusicPath = Path.Combine(sessionPath, "audio", "background.wav");
                    await _ttsService.GenerateBackgroundAudioAsync(
                        settings.MusicStyle ?? "ambient",
                        30, // Default duration
                        bgMusicPath,
                        cancellationToken
                    );
                    
                    var mixedAudioPath = Path.Combine(sessionPath, "audio", "mixed.wav");
                    await _ttsService.MixAudioTracksAsync(
                        new[] { audioPath, bgMusicPath },
                        mixedAudioPath,
                        cancellationToken
                    );
                    
                    result.AudioPath = mixedAudioPath;
                }

                // Stage 3: Transition Generation (Depth/Transition Models)
                _logger.LogInformation("[Stage 3/4] Generating transitions...");
                progress?.Report(65);
                
                var allFrames = new System.Collections.Generic.List<string>();
                
                // Process each image/video and generate frames
                foreach (var proxyPath in ingestionResult.ProxyPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    if (_mediaIngestionService.GetType().GetMethod("IsVideoFile") != null)
                    {
                        // For videos, extract frames
                        var frameDir = Path.Combine(sessionPath, "frames", Path.GetFileNameWithoutExtension(proxyPath));
                        var frames = await _mediaIngestionService.ExtractVideoFramesAsync(
                            proxyPath,
                            frameDir,
                            settings.FramesPerVideo,
                            cancellationToken
                        );
                        allFrames.AddRange(frames);
                    }
                    else
                    {
                        // For images, apply Ken Burns effect
                        var effectDir = Path.Combine(sessionPath, "frames", Path.GetFileNameWithoutExtension(proxyPath));
                        var effectFrames = await _transitionService.GenerateKenBurnsEffectAsync(
                            proxyPath,
                            1920,
                            1080,
                            settings.FramesPerImage,
                            effectDir,
                            cancellationToken
                        );
                        allFrames.AddRange(effectFrames);
                    }
                }

                // Generate smooth transitions between frames
                var transitionDir = Path.Combine(sessionPath, "transitions");
                await _transitionService.GenerateTransitionsAsync(
                    allFrames.ToArray(),
                    transitionDir,
                    settings.TransitionFrames,
                    CreateSubProgress(progress, 65, 85),
                    cancellationToken
                );

                // Combine all frames for final output
                var finalFramesDir = Path.Combine(sessionPath, "frames_final");
                Directory.CreateDirectory(finalFramesDir);
                
                // Merge original frames and transitions (simplified - production would interleave)
                allFrames.AddRange(Directory.GetFiles(transitionDir, "*.png"));
                result.FrameCount = allFrames.Count;

                // Stage 4: Video Encoding (FFmpeg NVENC)
                _logger.LogInformation("[Stage 4/4] Encoding final video with FFmpeg...");
                progress?.Report(90);
                
                var outputPath = Path.Combine(
                    _workspaceManager.GetSessionPath(sessionId).Replace("temp", "output"),
                    $"reel_{DateTime.Now:yyyyMMdd_HHmmss}.mp4"
                );
                
                // Ensure output directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                
                await _videoEncodingService.EncodeVideoAsync(
                    allFrames.ToArray(),
                    result.AudioPath,
                    outputPath,
                    CreateSubProgress(progress, 90, 100),
                    cancellationToken
                );
                
                result.OutputPath = outputPath;
                progress?.Report(100);

                _logger.LogInformation("Video generation completed successfully! Output: {OutputPath}", outputPath);
                
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Video generation was cancelled by user");
                
                // Cleanup on cancellation
                await _workspaceManager.DeleteGenerationSessionAsync(sessionId);
                
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Video generation failed");
                
                // Cleanup on error
                await _workspaceManager.DeleteGenerationSessionAsync(sessionId);
                
                throw;
            }
        }

        /// <summary>
        /// Creates a sub-progress reporter that maps a range to 0-100
        /// </summary>
        private IProgress<double>? CreateSubProgress(IProgress<double>? parent, double startPercent, double endPercent)
        {
            if (parent == null) return null;
            
            return new Progress<double>(value =>
            {
                var range = endPercent - startPercent;
                var mappedValue = startPercent + (value / 100 * range);
                parent.Report(mappedValue);
            });
        }
    }

    public class GenerationSettings
    {
        public int FramesPerImage { get; set; } = 15; // 0.5 seconds at 30fps
        public int FramesPerVideo { get; set; } = 30; // 1 second at 30fps
        public int TransitionFrames { get; set; } = 5;
        public bool IncludeBackgroundMusic { get; set; } = true;
        public string? MusicStyle { get; set; } = "ambient";
        public string? Theme { get; set; }
        public string? AspectRatio { get; set; } = "9:16"; // Vertical for reels
    }

    public class GenerationResult
    {
        public string SessionId { get; set; } = string.Empty;
        public string? Script { get; set; }
        public string? AudioPath { get; set; }
        public int FrameCount { get; set; }
        public string? OutputPath { get; set; }
        public MediaIngestionResult? IngestionResult { get; set; }
        public bool Success => !string.IsNullOrEmpty(OutputPath);
    }
}
