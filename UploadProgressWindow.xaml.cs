using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SaveTracker
{
    public sealed partial class UploadProgressWindow : INotifyPropertyChanged
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _isCompleted;
        private readonly DispatcherTimer _uiTimer;

        public event PropertyChangedEventHandler PropertyChanged;

        public class UploadProgress
        {
            public string Status { get; set; } = "";
            public int TotalFiles { get; set; }
            public int ProcessedFiles { get; set; }
            public int ValidFiles { get; set; }
            public int UploadedFiles { get; set; }
            public int BlacklistedFiles { get; set; } = 0;
            public int MissingFiles { get; set; }
            public List<string> LogMessages { get; set; } = new List<string>();
            public bool IsCompleted { get; set; }
        }

        private UploadProgress _progress = new UploadProgress();

        public UploadProgressWindow(string gameName, string provider)
        {
            InitializeComponent();
            _cancellationTokenSource = new CancellationTokenSource();

            GameNameText.Text = $"Game: {gameName}";
            ProviderText.Text = $"Provider: {provider}";
            
            // Timer to update UI periodically
            _uiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _uiTimer.Tick += UpdateUi;
            _uiTimer.Start();
        }

        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public void UpdateProgress(UploadProgress progress)
        {
            _progress = progress;
        }

        private void UpdateUi(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // Update progress bar
                if (_progress.TotalFiles > 0)
                {
                    double percentage = (double)_progress.ProcessedFiles / _progress.TotalFiles * 100;
                    MainProgressBar.Value = percentage;
                }

                // Update text
                ProgressText.Text = _progress.Status;
                ProgressDetails.Text = $"{_progress.ProcessedFiles} of {_progress.TotalFiles} files processed";
                
                // Update statistics
                ValidFilesCount.Text = _progress.ValidFiles.ToString();
                UploadedCount.Text = _progress.UploadedFiles.ToString();
                BlacklistedCount.Text = _progress.BlacklistedFiles.ToString();
                MissingCount.Text = _progress.MissingFiles.ToString();

                // Update log
                if (_progress.LogMessages.Any())
                {
                    var lastLogs = _progress.LogMessages.Skip(Math.Max(0, _progress.LogMessages.Count - 50));
                    StatusLog.Text = string.Join("\n", lastLogs);
                    LogScrollViewer.ScrollToEnd();

                }

                // Handle completion
                if (_progress.IsCompleted && !_isCompleted)
                {
                    _isCompleted = true;
                    TitleText.Text = TitleText.Text.IndexOf("upload", StringComparison.OrdinalIgnoreCase) >= 0
                        ? "Upload Completed!"
                        : "Download Completed!";
                    ProgressText.Text = "All files have been processed.";
                    CancelButton.IsEnabled = false;
                    CloseButton.IsEnabled = true;
                    MainProgressBar.Value = 100;
                    _uiTimer.Stop();
                }
            }));
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource.Cancel();
            TitleText.Text = "Upload Cancelled";
            ProgressText.Text = "Cancelling upload...";
            CancelButton.IsEnabled = false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isCompleted && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                var result = MessageBox.Show(
                    "Upload is still in progress. Are you sure you want to close?",
                    "Upload in Progress",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
                
                _cancellationTokenSource.Cancel();
            }

            _uiTimer?.Stop();
            base.OnClosing(e);
        }

        
    }
}
