using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AutonomousReelsGenerator.Services
{
    /// <summary>
    /// Manages VRAM allocation and model loading/unloading for GPU memory efficiency
    /// Implements sequential staging to work within consumer GPU constraints
    /// </summary>
    public class VRAMManager
    {
        private readonly ILogger<VRAMManager> _logger;
        private readonly AppConfig _config;
        
        private string? _currentLoadedModel;
        private long _currentVRAMUsage;
        private bool _isDisposed;

        public VRAMManager(ILogger<VRAMManager> logger, AppConfig config)
        {
            _logger = logger;
            _config = config;
            _currentLoadedModel = null;
            _currentVRAMUsage = 0;
        }

        /// <summary>
        /// Gets current VRAM usage in MB
        /// </summary>
        public long GetCurrentVRAMUsageMB()
        {
            if (!_config.EnableVRAMMonitoring)
            {
                return 0;
            }

            try
            {
                // In production, this would query NVIDIA CUDA APIs
                // For now, track internally
                return _currentVRAMUsage;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get VRAM usage");
                return 0;
            }
        }

        /// <summary>
        /// Gets total available VRAM in MB
        /// </summary>
        public long GetTotalVRAMMB()
        {
            // Default to 8GB, in production query actual GPU
            return 8192;
        }

        /// <summary>
        /// Gets available VRAM in MB
        /// </summary>
        public long GetAvailableVRAMMB()
        {
            var total = GetTotalVRAMMB();
            var used = GetCurrentVRAMUsageMB();
            return total - used - _config.VRAMSafetyMarginMB;
        }

        /// <summary>
        /// Checks if a model of given size can be loaded
        /// </summary>
        public bool CanLoadModel(long modelSizeMB)
        {
            var available = GetAvailableVRAMMB();
            var canLoad = available >= modelSizeMB;
            
            if (!canLoad)
            {
                _logger.LogWarning("Cannot load model: Required {Required}MB, Available {Available}MB", 
                    modelSizeMB, available);
            }
            
            return canLoad;
        }

        /// <summary>
        /// Loads a model into VRAM with size validation
        /// </summary>
        public async Task<bool> LoadModelAsync(string modelName, long modelSizeMB)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VRAMManager));
            }

            try
            {
                _logger.LogInformation("Loading model '{ModelName}' ({Size}MB) into VRAM...", 
                    modelName, modelSizeMB);

                // Check if we need to unload current model
                if (_currentLoadedModel != null && _currentLoadedModel != modelName)
                {
                    _logger.LogDebug("Unloading previous model '{ModelName}'", _currentLoadedModel);
                    await UnloadModelAsync(_currentLoadedModel);
                }

                // Validate VRAM availability
                if (!CanLoadModel(modelSizeMB))
                {
                    _logger.LogError("Insufficient VRAM to load model '{ModelName}'", modelName);
                    return false;
                }

                // Simulate model loading (in production, this loads ONNX models)
                await Task.Delay(100); // Placeholder for actual loading
                
                _currentLoadedModel = modelName;
                _currentVRAMUsage += modelSizeMB;

                _logger.LogInformation("Model '{ModelName}' loaded successfully. VRAM usage: {Usage}MB", 
                    modelName, _currentVRAMUsage);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load model '{ModelName}'", modelName);
                return false;
            }
        }

        /// <summary>
        /// Unloads a model from VRAM
        /// </summary>
        public async Task UnloadModelAsync(string modelName)
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                if (_currentLoadedModel != modelName)
                {
                    _logger.LogWarning("Attempted to unload model '{ModelName}' but '{Current}' is loaded", 
                        modelName, _currentLoadedModel);
                    return;
                }

                _logger.LogInformation("Unloading model '{ModelName}'...", modelName);

                // Simulate model unloading (in production, dispose ONNX session)
                await Task.Delay(50);

                // Estimate model size and reduce VRAM usage
                var estimatedSize = EstimateModelSize(modelName);
                _currentVRAMUsage = Math.Max(0, _currentVRAMUsage - estimatedSize);
                
                _currentLoadedModel = null;

                _logger.LogInformation("Model '{ModelName}' unloaded. VRAM usage: {Usage}MB", 
                    _currentVRAMUsage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error unloading model '{ModelName}'", modelName);
            }
        }

        /// <summary>
        /// Clears all models from VRAM (called on shutdown)
        /// </summary>
        public async Task ClearAllModelsAsync()
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                if (_currentLoadedModel != null)
                {
                    await UnloadModelAsync(_currentLoadedModel);
                }

                _currentVRAMUsage = 0;
                _logger.LogInformation("All models cleared from VRAM");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all models from VRAM");
            }
        }

        /// <summary>
        /// Executes an operation with a model loaded, ensuring cleanup
        /// </summary>
        public async Task<T> ExecuteWithModelAsync<T>(string modelName, long modelSizeMB, Func<Task<T>> operation)
        {
            try
            {
                var loaded = await LoadModelAsync(modelName, modelSizeMB);
                if (!loaded)
                {
                    throw new InvalidOperationException($"Failed to load model '{modelName}'");
                }

                var result = await operation();
                
                await UnloadModelAsync(modelName);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing operation with model '{ModelName}'", modelName);
                
                // Ensure cleanup on error
                await UnloadModelAsync(modelName);
                
                throw;
            }
        }

        /// <summary>
        /// Estimates model size based on name (in production, use actual model metadata)
        /// </summary>
        private long EstimateModelSize(string modelName)
        {
            return modelName.ToLower() switch
            {
                var n when n.Contains("vision") || n.Contains("llm") => 3000, // ~3GB for vision LLM
                var n when n.Contains("tts") => 1500, // ~1.5GB for TTS
                var n when n.Contains("transition") || n.Contains("depth") => 1000, // ~1GB for transition
                _ => 2000 // Default estimate
            };
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                ClearAllModelsAsync().Wait();
                _isDisposed = true;
            }
        }
    }
}
