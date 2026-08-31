using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using AutonomousReelsGenerator.Services;

namespace AutonomousReelsGenerator.Views
{
    public partial class MainWindow : Window
    {
        private readonly ILogger<MainWindow> _logger;
        private readonly PayloadExtractor _payloadExtractor;
        private readonly GenerationOrchestrator _orchestrator;
        private readonly WorkspaceManager _workspaceManager;
        
        private readonly ObservableCollection<string> _selectedMedia = new();
        private CancellationTokenSource? _generationCts;

        public MainWindow(
            ILogger<MainWindow> logger,
            PayloadExtractor payloadExtractor,
            GenerationOrchestrator orchestrator,
            WorkspaceManager workspaceManager)
        {
            InitializeComponent();
            
            _logger = logger;
            _payloadExtractor = payloadExtractor;
            _orchestrator = orchestrator;
            _workspaceManager = workspaceManager;
            
            LstSelectedMedia.ItemsSource = _selectedMedia;
            
            // Setup slider value change handlers
            SldFramesPerImage.ValueChanged += (s, e) => TxtFramesPerImage.Text = $"{(int)e.NewValue} frames";
            SldFramesPerVideo.ValueChanged += (s, e) => TxtFramesPerVideo.Text = $"{(int)e.NewValue} frames";
            SldTransitionFrames.ValueChanged += (s, e) => TxtTransitionFrames.Text = $"{(int)e.NewValue} frames";
            
            _logger.LogInformation("MainWindow initialized");
        }

        /// <summary>
        /// Shows the extraction UI for first-time setup
        /// </summary>
        public void ShowExtractionUI()
        {
            MainContent.Visibility = Visibility.Collapsed;
            ExtractionUI.Visibility = Visibility.Visible;
            BtnGenerate.IsEnabled = false;
            
            Task.Run(async () => await RunExtractionAsync());
        }

        /// <summary>
        /// Runs the payload extraction process
        /// </summary>
        private async Task RunExtractionAsync()
        {
            try
            {
                var progress = new Progress<double>(percent =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        PrbExtraction.Value = percent;
                        TxtExtractionStatus.Text = $"Extracting... {percent:F0}%";
                    });
                });

                Dispatcher.Invoke(() => TxtExtractionStatus.Text = "Starting extraction...");
                
                var success = await _payloadExtractor.ExtractPayloadAsync(progress);
                
                if (success && _payloadExtractor.ValidateExtraction())
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtExtractionStatus.Text = "✅ Extraction complete! Starting application...";
                    });
                    
                    await Task.Delay(1000);
                    
                    Dispatcher.Invoke(() =>
                    {
                        ExtractionUI.Visibility = Visibility.Collapsed;
                        MainContent.Visibility = Visibility.Visible;
                        BtnGenerate.IsEnabled = true;
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtExtractionStatus.Text = "❌ Extraction failed. Please check logs.";
                        BtnGenerate.IsEnabled = true;
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Extraction failed");
                
                Dispatcher.Invoke(() =>
                {
                    TxtExtractionStatus.Text = $"❌ Error: {ex.Message}";
                    BtnGenerate.IsEnabled = true;
                });
            }
        }

        private void BtnSelectMedia_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Images and Videos",
                Filter = "Media Files|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.mp4;*.mov;*.avi;*.mkv;*.webm|All Files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (!_selectedMedia.Contains(file))
                    {
                        _selectedMedia.Add(file);
                    }
                }
                
                UpdateStatus($"{_selectedMedia.Count} media file(s) selected");
            }
        }

        private void BtnRemoveMedia_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is string path)
            {
                _selectedMedia.Remove(path);
                UpdateStatus($"{_selectedMedia.Count} media file(s) selected");
            }
        }

        private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMedia.Count == 0)
            {
                MessageBox.Show(
                    "Please select at least one image or video to create your reel.",
                    "No Media Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Disable UI during generation
            SetGenerationUIState(isGenerating: true);
            
            _generationCts = new CancellationTokenSource();
            
            try
            {
                var settings = new GenerationSettings
                {
                    FramesPerImage = (int)SldFramesPerImage.Value,
                    FramesPerVideo = (int)SldFramesPerVideo.Value,
                    TransitionFrames = (int)SldTransitionFrames.Value,
                    IncludeBackgroundMusic = ChkBackgroundMusic.IsChecked ?? true,
                    MusicStyle = ((System.Windows.Controls.ComboBoxItem)CmbMusicStyle.SelectedItem)?.Content?.ToString()
                };

                var progress = new Progress<double>(percent =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        PrbGeneration.Value = percent;
                        UpdateProgressText(percent);
                    });
                });

                _logger.LogInformation("Starting generation with {Count} media files", _selectedMedia.Count);

                var result = await _orchestrator.GenerateVideoAsync(
                    _selectedMedia.ToArray(),
                    settings,
                    progress,
                    _generationCts.Token
                );

                if (result.Success)
                {
                    UpdateStatus($"✅ Generation complete! Saved to: {result.OutputPath}");
                    
                    MessageBox.Show(
                        $"Your reel has been created successfully!\n\nScript:\n{result.Script}\n\nSaved to:\n{result.OutputPath}",
                        "Generation Complete!",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    
                    // Open output folder
                    try
                    {
                        var outputDir = Path.GetDirectoryName(result.OutputPath);
                        if (!string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", outputDir);
                        }
                    }
                    catch { /* Ignore explorer errors */ }
                }
                else
                {
                    UpdateStatus("❌ Generation failed - no output produced");
                }
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Generation cancelled by user");
                _logger.LogInformation("Generation was cancelled by user");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generation failed");
                UpdateStatus($"❌ Error: {ex.Message}");
                
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        $"An error occurred during generation:\n\n{ex.Message}",
                        "Generation Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                });
            }
            finally
            {
                _generationCts?.Dispose();
                _generationCts = null;
                SetGenerationUIState(isGenerating: false);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_generationCts != null && !_generationCts.IsCancellationRequested)
            {
                _generationCts.Cancel();
                UpdateStatus("Cancelling generation...");
            }
        }

        /// <summary>
        /// Updates UI state for generation in progress
        /// </summary>
        private void SetGenerationUIState(bool isGenerating)
        {
            Dispatcher.Invoke(() =>
            {
                BtnGenerate.IsEnabled = !isGenerating;
                BtnSelectMedia.IsEnabled = !isGenerating;
                ProgressSection.Visibility = isGenerating ? Visibility.Visible : Visibility.Collapsed;
                
                if (!isGenerating)
                {
                    PrbGeneration.Value = 0;
                    TxtProgressStatus.Text = "Ready";
                }
            });
        }

        /// <summary>
        /// Updates the progress status text based on percentage
        /// </summary>
        private void UpdateProgressText(double percent)
        {
            var status = percent switch
            {
                < 15 => "📁 Ingesting media and generating proxies...",
                < 35 => "🧠 Analyzing content with Vision LLM...",
                < 60 => "🎙️ Generating voiceover with TTS...",
                < 85 => "🎬 Creating transitions and effects...",
                < 100 => "🎞️ Encoding final video...",
                _ => "✨ Complete!"
            };
            
            TxtProgressStatus.Text = $"{status} ({percent:F0}%)";
        }

        /// <summary>
        /// Updates the status bar text
        /// </summary>
        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() => TxtStatus.Text = message);
        }
    }
}
