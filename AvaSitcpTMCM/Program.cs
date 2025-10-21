using Avalonia;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading;

namespace AvaSitcpTMCM
{
    internal sealed class Program
    {
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
            else
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
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

            string userIp = config.GetSection("Settings:IpAddress").Value ?? "193.169.11.17";
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

