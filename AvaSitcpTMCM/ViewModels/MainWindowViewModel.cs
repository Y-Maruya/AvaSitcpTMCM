using Avalonia.Controls; // Windowを使うために必要
using Avalonia.Platform.Storage; // StorageProviderを使うために必要
using Avalonia.Threading;
using AvaSitcpTMCM.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using System;
using System.Net;
using System.Threading.Tasks;

namespace AvaSitcpTMCM.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        public string Greeting { get; } = "Welcome to Avalonia!";
        private readonly SitcpFunctions _sitcpFunctions = new();
        [ObservableProperty]
        private string _ipAddress = "193.169.11.17";

        [ObservableProperty]
        private string _port = "24";

        [ObservableProperty]
        private string _logText = "";

        [ObservableProperty]
        private string _tcpWriteText = "";

        [ObservableProperty]
        private string _tcpReadText = "";

        [ObservableProperty]
        private string _folderPathAtTcpReadToFile = "";

        [ObservableProperty]
        private string _folderPathAtTM = "";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartTCPReadFromUserToFileCommand))]
        private bool _isStartTCPReadFromUserToFileEnabled = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StopTCPReadFromUserToFileCommand))]
        private bool _isStopTCPReadFromUserToFileEnabled = false;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartTMCommand))]
        private bool _isStartTMEnabled = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StopTMCommand))]
        private bool _isStopTMEnabled = false;

        [ObservableProperty]
        private string[] _current_data = new string[40];

        [ObservableProperty]
        private string[] _temperature_max_data = new string[40];

        [ObservableProperty]
        private string[] _temperature_avg_data = new string[40];

        [ObservableProperty]
        private string[] _temperature_min_data = new string[40];

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartCurrentAcqSendCommand))]
        private bool _isStartCurrentAcqSendEnabled = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StopCurrentAcqSendCommand))]
        private bool _isStopCurrentAcqSendEnabled = false;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StartTemperatureAcqSendCommand))]
        private bool _isStartTemperatureAcqSendEnabled = true;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(StopTemperatureAcqSendCommand))]
        private bool _isStopTemperatureAcqSendEnabled = false;
        //[ObservableProperty]
        //private bool _isSendEnabled;

        //[ObservableProperty]
        //private bool _ledOrElecCheck;
        public MainWindowViewModel()
        {
            // Default configuration loading
            var config = new ConfigurationBuilder().SetBasePath(System.AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).Build();

            IpAddress = config.GetSection("Settings:IpAddress").Value ?? "192.168.10.16";
            Port = config.GetSection("Settings:Port").Value ?? "26";
            FolderPathAtTcpReadToFile = config.GetSection("Settings:FolderPathAtTcpReadToFile").Value ?? "";
            FolderPathAtTM = config.GetSection("Settings:FolderPathAtTM").Value ?? "";

            _sitcpFunctions.SendMessageEvent += OnSendMessageEvent;
            _sitcpFunctions.SendCurrentEvent += OnSendCurrentEvent;
            _sitcpFunctions.SendTemperatureEvent += OnSendTemperatureEvent;
        }
        public bool IsSendEnabled
        {
            get => _sitcpFunctions.IsSendEnabled;
            set
            {
                SetProperty(_sitcpFunctions.IsSendEnabled, value, _sitcpFunctions, (service, val) => service.IsSendEnabled = val);
            }
        }

        private void OnSendMessageEvent(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogText += message;
            });
        }
        private void OnSendCurrentEvent(Tuple<int, string> currentData)
        {
            if (currentData.Item1 < 0 || currentData.Item1 > 39) return;
            Dispatcher.UIThread.Post(() =>
            {
                Current_data[currentData.Item1] = currentData.Item2;
            });
        }
        private void OnSendTemperatureEvent(Tuple<int, string, string, string> temperatureData)
        {
            if (temperatureData.Item1 < 0 || temperatureData.Item1 > 39) return;
            Dispatcher.UIThread.Post(() =>
            {
                Temperature_max_data[temperatureData.Item1] = temperatureData.Item2;
                Temperature_avg_data[temperatureData.Item1] = temperatureData.Item3;
                Temperature_min_data[temperatureData.Item1] = temperatureData.Item4;
            });
        }
        [RelayCommand]
        private void UserConnect()
        {
            if (int.TryParse(Port, out int port) && !string.IsNullOrWhiteSpace(IpAddress))
            {
                _sitcpFunctions.UserConnect(IpAddress, port);
            }
            else
            {
                LogText += "Invalid IP address or port number.\n";
            }
        }
        [RelayCommand]
        private void TCPWriteToUser()
        {
            _sitcpFunctions.TCPWriteToUser(TcpWriteText);
        }

        [RelayCommand]
        private void TCPReadFromUser()
        {
            TcpReadText = _sitcpFunctions.TCPReadFromUser();
        }

        [RelayCommand]
        private async Task SelectFolder(Window? owner)
        {
            if (owner is null) return;

            var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select a folder",
                AllowMultiple = false
            });

            if (folders is { Count: > 0 })
            {
                FolderPathAtTcpReadToFile = folders[0].Path.LocalPath;
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartTCPReadFromUserToFile))]
        private void StartTCPReadFromUserToFile()
        {
            if (string.IsNullOrWhiteSpace(FolderPathAtTcpReadToFile))
            {
                LogText += "Folder path is not set.\n";
                return;
            }
            _sitcpFunctions.StartTCPReadFromUserToFile(FolderPathAtTcpReadToFile);
            IsStartTCPReadFromUserToFileEnabled = false;
            IsStopTCPReadFromUserToFileEnabled = true;
        }
        private bool CanStartTCPReadFromUserToFile()
        {
            return IsStartTCPReadFromUserToFileEnabled && !string.IsNullOrWhiteSpace(FolderPathAtTcpReadToFile);
        }

        [RelayCommand(CanExecute = nameof(CanStopTCPReadFromUserToFile))]
        private void StopTCPReadFromUserToFile()
        {
            _sitcpFunctions.StopTCPReadFromUserToFile();
            IsStartTCPReadFromUserToFileEnabled = true;
            IsStopTCPReadFromUserToFileEnabled = false;
        }
        private bool CanStopTCPReadFromUserToFile()
        {
            return IsStopTCPReadFromUserToFileEnabled;
        }

        [RelayCommand]
        private async Task SelectFolderAtTM(Window? owner)
        {
            if (owner is null) return;
            var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select a folder for Temperature Monitoring",
                AllowMultiple = false
            });
            if (folders is { Count: > 0 })
            {
                FolderPathAtTM = folders[0].Path.LocalPath;
            }
        }

        [RelayCommand(CanExecute = nameof(CanStartTM))]
        private void StartTM()
        {
            if (string.IsNullOrWhiteSpace(FolderPathAtTM))
            {
                LogText += "Folder path for TM is not set.\n";
                return;
            }
            _sitcpFunctions.StartTemperatureMonitoring(FolderPathAtTM);
            IsStartTMEnabled = false;
            IsStopTMEnabled = true;
        }
        private bool CanStartTM()
        {
            return IsStartTMEnabled && !string.IsNullOrWhiteSpace(FolderPathAtTM);
        }

        [RelayCommand(CanExecute = nameof(CanStopTM))]
        private void StopTM()
        {
            _sitcpFunctions.StopTemperatureMonitoring();
            IsStartTMEnabled = true;
            IsStopTMEnabled = false;
        }
        private bool CanStopTM()
        {
            return IsStopTMEnabled;
        }

        [RelayCommand]
        private void RefreshCurrentMonitoring()
        {
            _sitcpFunctions.Current_Refresh();
        }

        [RelayCommand]
        private void StartInfluxTest()
        {
            _sitcpFunctions.InfluxTestStart();

        }
        [RelayCommand]
        private void StopInfluxTest()
        {
            _sitcpFunctions.InfluxTestStop();
        }

        [RelayCommand(CanExecute = nameof(CanStartCurrentAcqSend))]
        private void StartCurrentAcqSend()
        {
            _sitcpFunctions.StartCurrentAcqSend();
            IsStartCurrentAcqSendEnabled = false;
            IsStopCurrentAcqSendEnabled = true;
        }
        private bool CanStartCurrentAcqSend()
        {
            return IsStartCurrentAcqSendEnabled;
        }

        [RelayCommand(CanExecute = nameof(CanStopCurrentAcqSend))]
        private void StopCurrentAcqSend()
        {
            _sitcpFunctions.StopCurrentAcqSend();
            IsStartCurrentAcqSendEnabled = true;
            IsStopCurrentAcqSendEnabled = false;
        }
        private bool CanStopCurrentAcqSend()
        {
            return IsStopCurrentAcqSendEnabled;
        }
        [RelayCommand(CanExecute = nameof(CanStartTemperatureAcqSend))]
        private void StartTemperatureAcqSend()
        {
            _sitcpFunctions.StartTemperatureAcqSend();
            IsStartTemperatureAcqSendEnabled = false;
            IsStopTemperatureAcqSendEnabled = true;
        }
        private bool CanStartTemperatureAcqSend()
        {
            return IsStartTemperatureAcqSendEnabled;
        }
        [RelayCommand(CanExecute = nameof(CanStopTemperatureAcqSend))]
        private void StopTemperatureAcqSend()
        {
            _sitcpFunctions.StopTemperatureAcqSend();
            IsStartTemperatureAcqSendEnabled = true;
            IsStopTemperatureAcqSendEnabled = false;
        }
        private bool CanStopTemperatureAcqSend()
        {
            return IsStopTemperatureAcqSendEnabled;
        }
        [RelayCommand]
        private void ClearLog()
        {
            LogText = string.Empty;
        }
    }
}

