using Avalonia;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaSitcpTMCM
{
    internal sealed class Program
    {
        static volatile bool running = true;
        static volatile bool monitoring = false;
        static Task? monitoringTask;
        static CancellationTokenSource? monitoringCts;
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "-cli")
            {
                if (args.Length > 1 && args[1] == "-test")
                    RunCliTest(args);
                else
                    RunCli(args);
            }
            else if (args.Length > 0 && args[0] == "-server")
            {
                RunServer(args);
            }
            else
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
        }
        // Server mode for running the application
        private static void RunServer(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            string userIp = config["Settings:IPAddress"] ?? "127.0.0.1";
            string portStr = config["Settings:Port"] ?? "5025";
            string commPortStr = config["Settings:PortForCommunication"] ?? "9000";
            string maxRetryStr = config["Settings:MaxRetry"] ?? "3";
            string bindAddressStr = config["Settings:CommandBindAddress"] ?? "127.0.0.1"; // 追加

            int userPort = int.TryParse(portStr, out var up) ? up : 5025;
            int portForCommunication = int.TryParse(commPortStr, out var cp) ? cp : 9000;
            int maxRetry = int.TryParse(maxRetryStr, out var mr) ? mr : 3;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-ip":
                        if (i + 1 < args.Length) userIp = args[++i];
                        break;
                    case "-port":
                        if (i + 1 < args.Length) userPort = int.Parse(args[++i]);
                        break;
                    case "-commport":
                        if (i + 1 < args.Length) portForCommunication = int.Parse(args[++i]);
                        break;
                    case "-bind": // 追加
                        if (i + 1 < args.Length) bindAddressStr = args[++i];
                        break;
                    case "-r":
                        if (i + 1 < args.Length) maxRetry = int.Parse(args[++i]);
                        break;
                    case "-h":
                        Console.WriteLine("Usage: AvaSitcpTMCM -server [-ip <IP>] [-port <Port>] [-commport <Port>] [-bind <BindIP|*>] [-r <MaxRetry>]");
                        return;
                }
            }

            IPAddress bindAddress;
            if (bindAddressStr is "*" or "0.0.0.0")
            {
                bindAddress = IPAddress.Any;
            }
            else if (bindAddressStr is "::" or "any6")
            {
                bindAddress = IPAddress.IPv6Any;
            }
            else
            {
                if (!IPAddress.TryParse(bindAddressStr, out bindAddress))
                {
                    Console.WriteLine($"[WARN] Invalid bind address '{bindAddressStr}', fallback 127.0.0.1(localhost)");
                    bindAddress = IPAddress.Loopback;
                }
            }

            using var stopCts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("[SIGNAL] Ctrl+C received. Shutting down...");
                stopCts.Cancel();
                running = false;
            };

            var sitcp = new SitcpFunctions();
            sitcp.SendMessageEvent += Console.Write;

            bool connected = false;
            for (int attempt = 1; attempt <= maxRetry && !stopCts.IsCancellationRequested; attempt++)
            {
                Console.WriteLine($"[CONNECT] {userIp}:{userPort} attempt {attempt}/{maxRetry}");
                int rc;
                try
                {
                    rc = sitcp.UserConnect(userIp, userPort);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] UserConnect: {ex.Message}");
                    rc = -1;
                }
                if (rc != 0)
                {
                    Thread.Sleep(1000);
                    continue;
                }

                Console.WriteLine("[CONNECT] Checking InfluxDB...");
                bool influxOk = false;
                try
                {
                    influxOk = sitcp.CheckInfluxConnectionAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Influx: {ex.Message}");
                }
                if (!influxOk)
                {
                    sitcp.UserDisconnect();
                    Thread.Sleep(1000);
                    continue;
                }

                Console.WriteLine("[READY] Connected to InfluxDB.");
                Console.WriteLine($"[INFO] Influx URL: {sitcp.GetInfluxUrl()}");
                connected = true;
                break;
            }

            if (!connected)
            {
                Console.WriteLine(stopCts.IsCancellationRequested
                    ? "[EXIT] Cancelled before connection established."
                    : "[FAIL] Max retry reached.");
                try { sitcp.UserDisconnect(); } catch { }
                return;
            }

            TcpListener? listener = null;
            try
            {
                listener = new TcpListener(bindAddress, portForCommunication);
                listener.Start();
                Console.WriteLine($"[SERVER] Command listener on 127.0.0.1:{portForCommunication}");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[ERROR] Listener start failed: {ex.Message}");
                sitcp.UserDisconnect();
                return;
            }

            var acceptLoop = Task.Run(async () =>
            {
                while (running && !stopCts.IsCancellationRequested)
                {
                    TcpClient? client = null;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync(stopCts.Token);
                        _ = Task.Run(() => HandleClientAsync(client, sitcp, stopCts.Token));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Accept loop: {ex.Message}");
                        await Task.Delay(200);
                    }
                }
            });

            Console.WriteLine("[SERVER] Type 'exit' into a local client to stop.");

            // 待機
            while (running && !stopCts.IsCancellationRequested)
            {
                Thread.Sleep(300);
            }

            Console.WriteLine("[SHUTDOWN] Stopping...");
            monitoringCts?.Cancel();
            try { acceptLoop.Wait(TimeSpan.FromSeconds(5)); } catch { }
            try { listener.Stop(); } catch { }
            StopMonitoring(sitcp);
            try { sitcp.UserDisconnect(); } catch { }
            Console.WriteLine("[EXIT] Done.");
        }

        private static async Task HandleClientAsync(TcpClient client, SitcpFunctions sitcp, CancellationToken token)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, leaveOpen: true))
            using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true) { AutoFlush = true })
            {
                try
                {
                    // 1 行 = 1 コマンド
                    string? line = await reader.ReadLineAsync(token);
                    if (line == null) return;
                    string cmd = line.Trim();
                    Console.WriteLine($"[CMD] {cmd}");
                    string resp = HandleCommand(cmd, sitcp);
                    await writer.WriteAsync(resp);
                    if (!running) Console.WriteLine("[INFO] Exit command processed.");
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Client handler: {ex.Message}");
                }
            }
        }

        static string HandleCommand(string cmd, SitcpFunctions sitcp)
        {
            switch (cmd)
            {
                case "start":
                    StartMonitoring(sitcp);
                    return "MONITORING STARTED\n";
                case "stop":
                    StopMonitoring(sitcp);
                    return "MONITORING STOPPED\n";
                case "status":
                    return $"STATUS monitoring={monitoring}\n";
                case "exit":
                    running = false;
                    StopMonitoring(sitcp);
                    return "SERVER EXITING\n";
                default:
                    return "UNKNOWN COMMAND\n";
            }
        }

        static void StartMonitoring(SitcpFunctions sitcp)
        {
            if (monitoring) return;
            monitoring = true;
            monitoringCts = new CancellationTokenSource();
            var token = monitoringCts.Token;
            monitoringTask = Task.Run(() =>
            {
                try
                {
                    sitcp.StartAllAcqSend();
                    // ここで内部ループが存在するなら token 認識するよう SitcpFunctions の改善が必要
                    while (!token.IsCancellationRequested && monitoring) Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Monitoring: {ex.Message}");
                }
            });
            Console.WriteLine("[INFO] Monitoring started.");
        }

        static void StopMonitoring(SitcpFunctions sitcp)
        {
            if (!monitoring) return;
            monitoring = false;
            monitoringCts?.Cancel();
            try { sitcp.StopAllAcqSend(); } catch { }
            // 内部タスクのクリーンアップ時間を少し与える（200〜500ms程度）
            Thread.Sleep(300);
            try { monitoringTask?.Wait(TimeSpan.FromSeconds(5)); } catch { }
            Console.WriteLine("[INFO] Monitoring stopped.");
        }
        // CLI mode for running the application
        private static void RunCli(string[] args)
        {
            var config = new ConfigurationBuilder().SetBasePath(System.AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).Build();

            string userIp = config.GetSection("Settings:IPAddress").Value ?? "193.169.11.17";
            string Port = config.GetSection("Settings:Port").Value ?? "25";
            //string FolderPathAtTcpReadToFile = config.GetSection("Settings:FolderPathAtTcpReadToFile").Value ?? "";
            //string runNumber = config.GetSection("Setting:RunNumber").Value ?? "";
             int maxRetry = int.TryParse(config.GetSection("Settings:MaxRetry").Value, out var retry) ? retry : 3;
            // ex:  --user-ip 192.168.10.16 --user-port 24
            int userPort = int.TryParse(Port, out var port) ? port : 25;
            //string folder = FolderPathAtTcpReadToFile;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-ip":
                        userIp = args[++i];
                        break;
                    case "-port":
                        userPort = int.Parse(args[++i]);
                        break;
                    case "-r":
                        maxRetry = int.Parse(args[++i]);
                        break;
                    case "-h":
                        Console.WriteLine("Usage: AvaSitcpTMCM -cli [-ip <IP Address>] [-port <Port>] [-r <Max Retry>]");
                        return;
                }
            }
            var stopDaqCts = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent the process from terminating immediately
                Console.WriteLine("Ctrl+C detected. Stopping Monitoring...");
                stopDaqCts.Cancel();
            };

            int j = 0;
            while (j < maxRetry)
            {
                var sitcp = new SitcpFunctions();
                sitcp.SendMessageEvent += Console.Write;
                Console.WriteLine($"Connecting to {userIp}:{userPort}");
                if (sitcp.UserConnect(userIp, userPort) != 0 || stopDaqCts.Token.IsCancellationRequested)
                {
                    j++;
                    Console.WriteLine($"Connection failed. Retrying {j}/{maxRetry}...");
                    continue;
                }
                Console.WriteLine("Connectting to the influxDB");
                bool a = sitcp.CheckInfluxConnectionAsync().GetAwaiter().GetResult();
                if (a == false)
                {
                    j++;
                    Console.WriteLine($"Connection to influxDB failed. Retrying {j}/{maxRetry}...");
                    sitcp.UserDisconnect();
                    continue;
                }
                Thread.Sleep(1000); // Wait for a second to ensure connection is established
                Console.WriteLine($"Connected to InfluxDB successfully. \n\r");
                Console.WriteLine($"URL: {sitcp.GetInfluxUrl()}\n\r");
                Console.WriteLine("Press S to start data acquisition...");
                while (true)
                {
                    if (Console.KeyAvailable)
                    {
                        if (Console.ReadKey(true).Key == ConsoleKey.S)
                        {
                            Console.WriteLine("'S' key pressed. Starting Monitoring...");
                            break;
                        }
                    }
                    if (stopDaqCts.Token.IsCancellationRequested)
                    {
                        Console.WriteLine("Cancellation requested before starting data acquisition. Exiting...");
                        sitcp.UserDisconnect();
                        return;
                    }
                    Thread.Sleep(100); // Sleep briefly to reduce CPU usage
                }
                Console.WriteLine("Starting data acquisition. Press Ctrl+C or press-S to stop.");

                try
                {
                    sitcp.StartAllAcqSend();
                    // Keep the application running until cancellation is requested
                    while (!stopDaqCts.Token.IsCancellationRequested)
                    {
                        if (Console.KeyAvailable)
                        {
                            if (Console.ReadKey(true).Key == ConsoleKey.S)
                            {
                                Console.WriteLine("'S' key pressed. Stopping Monitoring...");
                                stopDaqCts.Cancel();
                            }
                        }
                        Thread.Sleep(100); // Sleep briefly to reduce CPU usage
                    }
                    sitcp.StopAllAcqSend();
                    sitcp.UserDisconnect();
                    Console.WriteLine("Data acquisition stopped. Exiting application.");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during data acquisition: {ex.Message}");
                    // TODO: Handle the TCP disconnection and attempt to reconnect.
                    Console.WriteLine("Retry to exexute");
                    sitcp.StopAllAcqSend();
                    sitcp.UserDisconnect();
                    j++;
                }
            }

            return;
        }

        // CLI mode for running the application test
        private static void RunCliTest(string[] args)
        {
            var config = new ConfigurationBuilder().SetBasePath(System.AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).Build();

            string userIp = config.GetSection("Settings:IPAddress").Value ?? "193.169.11.17";
            string Port = config.GetSection("Settings:Port").Value ?? "25";
            //string FolderPathAtTcpReadToFile = config.GetSection("Settings:FolderPathAtTcpReadToFile").Value ?? "";
            //string runNumber = config.GetSection("Setting:RunNumber").Value ?? "";
            int maxRetry = int.TryParse(config.GetSection("Settings:MaxRetry").Value, out var retry) ? retry : 3;
            // ex:  --user-ip 192.168.10.16 --user-port 24
            int userPort = int.TryParse(Port, out var port) ? port : 25;
            //string folder = FolderPathAtTcpReadToFile;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-ip":
                        userIp = args[++i];
                        break;
                    case "-port":
                        userPort = int.Parse(args[++i]);
                        break;
                    case "-h":
                        Console.WriteLine("Usage: AvaSitcpTMCM -cli -test [-ip <IP Address>] [-port <Port>]");
                        return;
                }
            }
            var stopDaqCts = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent the process from terminating immediately
                Console.WriteLine("Ctrl+C detected. Stopping Monitoring...");
                stopDaqCts.Cancel();
            };
            Console.WriteLine("Running in test mode...");
            Console.WriteLine("Select the test to perform:");
            Console.WriteLine("1. TCP Connection Test");
            Console.WriteLine("2. InfluxDB Connection Test");
            Console.WriteLine("3. Monitoring Data Acquisition Test");
            string choice = Console.ReadLine();
            if (choice != null)
            {
                Console.WriteLine(choice);
                if (choice == "1")
                {
                    var sitcp = new SitcpFunctions();
                    sitcp.SendMessageEvent += Console.Write;
                    Console.WriteLine($"Connecting to {userIp}:{userPort}");
                    if (sitcp.UserConnect(userIp, userPort) != 0)
                    {
                        Console.WriteLine("TCP Connection Test Failed.");
                    }
                    else
                    {
                        Console.WriteLine("TCP Connection Test Succeeded.");
                    }
                    Console.WriteLine("Test finished.");
                    sitcp.UserDisconnect();
                    return;
                }
                else if (choice == "2")
                {
                    var sitcp = new SitcpFunctions();
                    sitcp.SendMessageEvent += Console.Write;
                    Console.WriteLine("Connectting to the influxDB");
                    bool a = sitcp.CheckInfluxConnectionAsync().GetAwaiter().GetResult();
                    if (a == false)
                    {
                        Console.WriteLine("InfluxDB Connection Test Failed.");
                    }
                    else
                    {
                        Console.WriteLine("InfluxDB Connection Test Succeeded.");
                    }
                    Console.WriteLine("Test finished.");
                    return;
                }
                else if (choice == "3")
                {
                    var sitcp = new SitcpFunctions();
                    sitcp.SendMessageEvent += Console.Write;
                    sitcp.SendCurrentEvent += (s) => Console.WriteLine(s);
                    Console.WriteLine($"Connecting to {userIp}:{userPort}");
                    if (sitcp.UserConnect(userIp, userPort) != 0)
                    {
                        Console.WriteLine("TCP Connection Failed. Cannot perform Monitoring Data Acquisition Test.");
                        return;
                    }
                    Console.WriteLine("Starting data acquisition test.");
                    Console.WriteLine("Acquiring current data");
                    sitcp.Current_Refresh();
                    Console.WriteLine("Monitoring Data Acquisition Test completed.");
                    sitcp.UserDisconnect();
                    return;
                }
                else
                {
                    Console.WriteLine("Invalid choice. Exiting.");
                    return;
                }
            }
            else
            {
                Console.WriteLine("No test selected. Exiting.");
                return;
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}

