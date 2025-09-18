using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaSitcpTMCM.ViewModels;
using System.Collections.ObjectModel;


namespace AvaSitcpTMCM.Views
{
    public partial class SecondWindow : Window
    {
        public ObservableCollection<LayerDisplay> Layers { get; } = new();

        public SecondWindow()
        {
            InitializeComponent();
            DataContext = this;

            // 初期ダミーデータ（0で埋める）
            for (int i = 0; i < 40; i++)
            {
                Layers.Add(new LayerDisplay
                {
                    Layer = i + 1,
                    Current = "0",
                    TempMax = "0",
                    TempAvg = "0",
                    TempMin = "0"
                });
            }
        }

        // MainWindowViewModel からデータを反映させるメソッド
        public void SetViewModel(MainWindowViewModel vm)
        {
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(vm.Current_data) ||
                    e.PropertyName == nameof(vm.Temperature_max_data) ||
                    e.PropertyName == nameof(vm.Temperature_avg_data) ||
                    e.PropertyName == nameof(vm.Temperature_min_data))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        for (int i = 0; i < 40; i++)
                        {
                            Layers[i].Current = vm.Current_data[i];
                            Layers[i].TempMax = vm.Temperature_max_data[i];
                            Layers[i].TempAvg = vm.Temperature_avg_data[i];
                            Layers[i].TempMin = vm.Temperature_min_data[i];
                        }
                    });
                }
            };

            // 初期値反映
            Dispatcher.UIThread.Post(() =>
            {
                for (int i = 0; i < 40; i++)
                {
                    Layers[i].Current = vm.Current_data[i];
                    Layers[i].TempMax = vm.Temperature_max_data[i];
                    Layers[i].TempAvg = vm.Temperature_avg_data[i];
                    Layers[i].TempMin = vm.Temperature_min_data[i];
                }
            });
        }
    }

    public class LayerDisplay
    {
        public int Layer { get; set; }
        public string Current { get; set; }
        public string TempMax { get; set; }
        public string TempAvg { get; set; }
        public string TempMin { get; set; }
    }
}