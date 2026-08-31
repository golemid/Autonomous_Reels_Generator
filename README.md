# Autonomous Reels Generator

AI-powered video generation tool for creating short-form social media content (Reels, TikToks, Shorts) from your photos and videos.

## Features

### 🎯 Core Capabilities
- **Vision LLM Analysis**: Automatically analyzes your media and generates engaging scripts
- **Text-to-Speech**: Professional voiceover generation using AI
- **Smart Transitions**: Depth-aware morphing and smooth transitions between frames
- **GPU-Accelerated Encoding**: Fast H.264 encoding with NVIDIA NVENC support
- **Sequential VRAM Management**: Efficiently loads/unloads AI models to work within consumer GPU constraints

### 📦 Single Executable Distribution
- **Self-Contained**: All dependencies bundled into one ~6-10GB executable
- **First-Time Extraction**: Automatically extracts payload to isolated workspace on first launch
- **Persistent Workspace**: Subsequent launches skip extraction and load directly from disk
- **Clean Shutdown**: Automatic cleanup of temporary files and VRAM

## Architecture

### Execution Flow
```
User Input → Media Ingestion → Script Generation → Audio Generation → Transitions → Encoding → Output
     ↓              ↓                  ↓                 ↓                ↓            ↓          ↓
  UI/UX       512px Proxies      Vision LLM           TTS Model     Depth Models   FFmpeg    1080p MP4
```

### VRAM Management Strategy
Consumer GPUs cannot hold all models simultaneously. The app uses sequential staging:

1. **Stage 1**: Load Vision LLM (~3GB) → Generate script → Unload
2. **Stage 2**: Load TTS model (~1.5GB) → Generate audio → Unload  
3. **Stage 3**: Load transition/depth models (~1GB) → Generate morphs → Unload
4. **Stage 4**: Execute FFmpeg → Encode via GPU (NVENC)

### Directory Structure
```
%LOCALAPPDATA%/AutonomousReelsGenerator/
├── workspace/
│   ├── models/           # Extracted AI models (persistent)
│   │   ├── vision-llm.onnx
│   │   ├── tts-model.onnx
│   │   └── transition-model.onnx
│   ├── ffmpeg/           # FFmpeg binaries (persistent)
│   ├── temp/             # Session data (cleaned on exit)
│   │   └── session_*/
│   │       ├── proxies/
│   │       ├── frames/
│   │       ├── audio/
│   │       └── transitions/
│   └── output/           # Final videos (persistent)
└── logs/                 # Application logs
```

## Build Instructions

### Prerequisites
- .NET 8.0 SDK
- Windows x64 target platform
- Visual Studio 2022 or VS Code with C# extension

### Building the Single Executable

```bash
cd src/AutonomousReelsGenerator

# Restore dependencies
dotnet restore

# Build release configuration
dotnet build -c Release

# Publish as single self-contained executable
dotnet publish -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -o ./publish
```

### Creating the Payload Archive

Before publishing, bundle FFmpeg and AI models:

```bash
# Create payload directory structure
mkdir payload_build/ffmpeg
cp path/to/ffmpeg.exe payload_build/ffmpeg/
cp path/to/vision-llm.onnx payload_build/models/
cp path/to/tts-model.onnx payload_build/models/
cp path/to/transition-model.onnx payload_build/models/

# Compress into payload.zip (will be embedded)
cd payload_build
zip -r ../payload.zip ./*
```

### Final Publish Command

```bash
dotnet publish -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o ./publish
```

Output: `./publish/AutonomousReelsGenerator.exe` (~6-10GB with payload)

## Usage

### First Launch
1. Run `AutonomousReelsGenerator.exe`
2. Wait for payload extraction (6-10GB to workspace)
3. Application starts automatically after extraction

### Creating a Reel
1. Click **"Add Images & Videos"** to select media files
2. Adjust generation settings:
   - Frames per image/video
   - Transition smoothness
   - Background music style
3. Click **"Generate Reel"**
4. Monitor progress through the 4 stages
5. Video saved to `%LOCALAPPDATA%/AutonomousReelsGenerator/workspace/output/`

### Settings Reference

| Setting | Default | Description |
|---------|---------|-------------|
| Frames per Image | 15 | Duration each image appears (0.5s @ 30fps) |
| Frames per Video | 30 | Duration of video clips (1s @ 30fps) |
| Transition Frames | 5 | Smoothness of transitions |
| Background Music | Enabled | Add ambient background track |
| Music Style | Ambient | Genre of background music |

## Configuration

Edit `AppConfig.cs` to customize:

```csharp
public long MaxVRAMUsageMB { get; set; } = 6144;  // VRAM limit
public int ProxyResolution { get; set; } = 512;    // Processing resolution
public int OutputResolution { get; set; } = 1080;  // Final video height
public int BitrateMbps { get; set; } = 8;          // Video quality
```

## System Requirements

### Minimum
- **OS**: Windows 10 x64
- **CPU**: Intel i5 / AMD Ryzen 5
- **RAM**: 16 GB
- **GPU**: NVIDIA GTX 1060 (6GB VRAM)
- **Storage**: 15 GB free space

### Recommended
- **OS**: Windows 11
- **CPU**: Intel i7 / AMD Ryzen 7
- **RAM**: 32 GB
- **GPU**: NVIDIA RTX 3060 (12GB VRAM) or better
- **Storage**: SSD with 20 GB free space

## Project Structure

```
src/AutonomousReelsGenerator/
├── Config/
│   └── AppConfig.cs           # Application configuration
├── Services/
│   ├── PayloadExtractor.cs    # First-launch extraction
│   ├── WorkspaceManager.cs    # File/directory management
│   ├── VRAMManager.cs         # GPU memory management
│   ├── ScriptGenerationService.cs    # Vision LLM integration
│   ├── TTSService.cs          # Text-to-speech
│   ├── TransitionService.cs   # Depth/morph effects
│   ├── VideoEncodingService.cs# FFmpeg wrapper
│   ├── MediaIngestionService.cs # Media preprocessing
│   └── GenerationOrchestrator.cs # Pipeline coordination
├── Views/
│   └── MainWindow.xaml(.cs)   # WPF UI
├── App.xaml(.cs)              # Application entry point
└── AutonomousReelsGenerator.csproj
```

## Development

### Running in Debug Mode

```bash
dotnet run
```

Note: First launch in debug mode skips payload extraction if no payload.zip is found.

### Adding New AI Models

1. Add model file to payload structure
2. Update `AppConfig.cs` with model name
3. Create service class following existing pattern
4. Register in DI container (`App.xaml.cs`)
5. Integrate into orchestration pipeline

## Troubleshooting

### Common Issues

**"FFmpeg not found"**
- Ensure FFmpeg was extracted to workspace
- Check `%LOCALAPPDATA%/AutonomousReelsGenerator/workspace/ffmpeg/ffmpeg.exe`

**"Insufficient VRAM"**
- Close other GPU-intensive applications
- Reduce proxy resolution in config
- Upgrade to GPU with more VRAM

**Extraction fails on first launch**
- Ensure 15GB free disk space
- Check antivirus isn't blocking extraction
- Run as Administrator

## License

Proprietary - All rights reserved

## Version History

### v1.0.0 (Initial Release)
- Single executable distribution
- Sequential VRAM management
- Vision LLM script generation
- TTS voiceover
- Smart transitions
- GPU-accelerated encoding