using System.Windows;
using System.Windows.Controls;

namespace SaveTracker
{
    public partial class SaveTrackerSidebarView : UserControl
    {
        public SaveTrackerSidebarView()
        {
            InitializeComponent();
            
            // Add some debug output
            DebugConsole.WriteInfo("SaveTrackerSidebarView constructor called");
        }

        private void UploadSaves_Click(object sender, RoutedEventArgs e)
        {
            DebugConsole.WriteInfo("Upload button clicked");
            StatusText.Text = "Status: Uploading...";
        }

        private void DownloadSaves_Click(object sender, RoutedEventArgs e)
        {
            DebugConsole.WriteInfo("Download button clicked");
            StatusText.Text = "Status: Downloading...";
        }
    }
}