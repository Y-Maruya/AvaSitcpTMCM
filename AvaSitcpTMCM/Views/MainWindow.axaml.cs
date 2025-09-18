using Avalonia.Controls;
using Avalonia.Threading;
using AvaSitcpTMCM.ViewModels;

namespace AvaSitcpTMCM.Views
{
    public partial class MainWindow : Window
    {
        private SecondWindow? _monitorWindow;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void LogTextBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() => LogScrollViewer.ScrollToEnd());
        }

        private void OnOpenMonitorClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_monitorWindow == null || !_monitorWindow.IsVisible)
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    _monitorWindow = new SecondWindow();
                    _monitorWindow.SetViewModel(vm);
                    _monitorWindow.Show();
                }
            }
            else
            {
                _monitorWindow.Activate();
            }
        }
    }
}