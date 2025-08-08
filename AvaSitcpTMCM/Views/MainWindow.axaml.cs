using Avalonia.Controls;
using Avalonia.Threading;

namespace AvaSitcpTMCM.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        private void LogTextBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() => LogScrollViewer.ScrollToEnd());
        }
    }
}