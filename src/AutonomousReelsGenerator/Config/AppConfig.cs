namespace AutonomousReelsGenerator.Config
{
    public class AppConfig
    {
        // Workspace paths
        public string BasePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutonomousReelsGenerator"
        );

        public string WorkspacePath => Path.Combine(BasePath, "workspace");
        public string ModelsPath => Path.Combine(WorkspacePath, "models");
        public string TempPath => Path.Combine(WorkspacePath, "temp");
        public string OutputPath => Path.Combine(WorkspacePath, "output");
        public string LogsPath => Path.Combine(BasePath, "logs");

        // VRAM Management
        public long MaxVRAMUsageMB { get; set; } = 6144; // Default 6GB for consumer GPUs
        public bool EnableVRAMMonitoring { get; set; } = true;
        public int VRAMSafetyMarginMB { get; set; } = 512;

        // Video settings
        public int ProxyResolution { get; set; } = 512;
        public int OutputResolution { get; set; } = 1080;
        public int FrameRate { get; set; } = 30;
        public int BitrateMbps { get; set; } = 8;

        // AI Model settings
        public string VisionLLMModelName { get; set; } = "vision-llm.onnx";
        public string TTSModelName { get; set; } = "tts-model.onnx";
        public string TransitionModelName { get; set; } = "transition-model.onnx";

        // FFmpeg settings
        public string FFmpegPath { get; set; } = "ffmpeg";
        public bool UseHardwareEncoding { get; set; } = true;
        public string EncoderPreset { get; set; } = "medium";

        public void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(WorkspacePath);
            Directory.CreateDirectory(ModelsPath);
            Directory.CreateDirectory(TempPath);
            Directory.CreateDirectory(OutputPath);
            Directory.CreateDirectory(LogsPath);
        }
    }
}
