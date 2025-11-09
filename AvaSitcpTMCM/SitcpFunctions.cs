using Microsoft.Extensions.Configuration;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlTypes;
using System.Globalization; 
using System.IO;
using System.Linq;
using System.Net.Http; 
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


namespace AvaSitcpTMCM
{
    internal class SitcpFunctions
    {
        private TcpClient ServerClient = new TcpClient();
        private TcpClient UserClient = new TcpClient();

        private string _ip = "";
        private int _port = 0;
        private readonly SemaphoreSlim _reconnectLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource DataAcqTokens = new CancellationTokenSource();
        private CancellationTokenSource TemperatureMonitoringTokens = new CancellationTokenSource();
        private CancellationTokenSource TcpReceiveTokens = new CancellationTokenSource();
        private CancellationTokenSource CheckerTokens = new CancellationTokenSource();
        private CancellationTokenSource BwWriteTokens = new CancellationTokenSource();
        private CancellationTokenSource DataAcqMultiThreadTokens = new CancellationTokenSource();
        private CancellationTokenSource AutoAcqTokens = new CancellationTokenSource();
        private CancellationTokenSource InfluxTestTokens = new CancellationTokenSource();
        private CancellationTokenSource CurrentAcqSendTokens = new CancellationTokenSource();
        private CancellationTokenSource TemperatureAcqSendTokens = new CancellationTokenSource();
        private CancellationTokenSource StatusAcqSendTokens = new CancellationTokenSource();
        private CancellationTokenSource AllAcqSendTokens = new CancellationTokenSource();
        byte[] DataBuffer0 = new byte[1024 * 1024];
        byte[] DataBuffer1 = new byte[1024 * 1024];
        byte[] DataBuffer2 = new byte[1024 * 1024];
        byte[] DataBuffer3 = new byte[1024 * 1024];
        bool DataBuffer0_canread = false;
        bool DataBuffer1_canread = false;
        bool DataBuffer0_canwrite = true;
        bool DataBuffer1_canwrite = true;
        bool DataBuffer2_canread = false;
        bool DataBuffer2_canwrite = true;
        bool DataBuffer3_canread = false;
        bool DataBuffer3_canwrite = true;
        bool DataBuffer0_cancheck = false;
        bool DataBuffer1_cancheck = false;
        bool DataBuffer2_cancheck = false;
        bool DataBuffer3_cancheck = false;
        int DataBuffer0_canreadlength = 0;
        int DataBuffer1_canreadlength = 0;
        int DataBuffer2_canreadlength = 0;
        int DataBuffer3_canreadlength = 0;
        int DataBuffer0_canchecklength = 0;
        int DataBuffer1_canchecklength = 0;
        int DataBuffer2_canchecklength = 0;
        int DataBuffer3_canchecklength = 0;
        int FooterWarnigCount = 0;

        private string FileName;
        private StringBuilder exceptionReport = new StringBuilder();
        int SumBytes = 0;
        delegate void SetTextCallback(string text);
        public event Action<string>? SendMessageEvent;
        public event Action<Tuple<int, string>>? SendCurrentEvent;
        public event Action<Tuple<int, string, string, string>>? SendTemperatureEvent;
        public event Action<Tuple<int, string>>? SendStatusEvent;
        public bool IsSendEnabled { get; set; } = false;
        public string HTTPInfluxUrl = "http://172.27.132.8:8086";
        int CurrentSendWaitTimeMs = 1000;
        int TemperatureSendWaitTimeMs = 1000;
        int StatusSendWaitTimeMs = 1000;
        int replyTimeoutMs = 2000;

        public static string ReplaceEnvironmentVariables(string input)
        {
            return Regex.Replace(input, @"\$([A-Za-z0-9_]+)", match =>
            {
                var varName = match.Groups[1].Value;
                var config = new ConfigurationBuilder().SetBasePath(System.AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).Build();
                var envConfig = new ConfigurationBuilder().SetBasePath(System.AppContext.BaseDirectory).AddJsonFile(config.GetSection("Settings:SecretFile").Value ?? "/etc/faser-secrets.json", optional: true, reloadOnChange: true).Build();
                if (envConfig.GetSection(varName).Value != null)
                {
                    return envConfig.GetSection(varName).Value ?? match.Value;
                }
                else
                {
                    return Environment.GetEnvironmentVariable(varName) ?? match.Value;
                }
            });
        }
        public static int GetBitFromByteArray(byte[] value, int i)
        {
            if (value == null || value.Length < 5) throw new ArgumentException("value must be byte[5]");
            if (i < 0 || i >= 40) throw new ArgumentOutOfRangeException(nameof(i), "i must be 0-39");

            int byteIndex = i / 8;
            int bitIndex = i % 8;

            return (value[byteIndex] >> bitIndex) & 0x1;
        }
        public SitcpFunctions()
        {
            // Initialize the DataBuffer arrays
            DataBuffer0 = new byte[1024 * 1024];
            DataBuffer1 = new byte[1024 * 1024];
            DataBuffer2 = new byte[1024 * 1024];
            DataBuffer3 = new byte[1024 * 1024];
            var config = new ConfigurationBuilder().SetBasePath(System.AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).Build();
            HTTPInfluxUrl = config.GetSection("Settings:HTTPInfluxUrl").Value ?? "http://172.27.132.8:8086";
            HTTPInfluxUrl = ReplaceEnvironmentVariables(HTTPInfluxUrl);
            string user = config.GetSection("Settings:DataBaseUser").Value ?? "admin";
            if (user.Contains("$") == true)
            {
                user = ReplaceEnvironmentVariables(user);
            }
            string password = config.GetSection("Settings:DataBasePassword").Value ?? "admin";
            if (password.Contains("$") == true)
            {
                password = ReplaceEnvironmentVariables(password);
            }
            string db = config.GetSection("Settings:DataBaseName").Value ?? "test_TMCM";
            if (db.Contains("$") == true)
            {
                db = ReplaceEnvironmentVariables(db);
            }
            if (HTTPInfluxUrl.Contains("/write?db=") == false)
            {
                HTTPInfluxUrl += "/write?db=" + db;
            }
            else
            {
                if (HTTPInfluxUrl.EndsWith(db) == false)
                {
                    HTTPInfluxUrl += db;
                }
            }
            if (HTTPInfluxUrl.Contains("u=") == false)
            {
                HTTPInfluxUrl += "&u=" + user + "&p=" + password;
            }
            else
            {
                if (HTTPInfluxUrl.EndsWith(user) == false)
                {
                    HTTPInfluxUrl += "&u=" + user + "&p=" + password;
                }
            }
            CurrentSendWaitTimeMs = int.Parse(config.GetSection("Settings:CurrentSendWaitTimeMs").Value ?? "1000");
            TemperatureSendWaitTimeMs = int.Parse(config.GetSection("Settings:TemperatureSendWaitTimeMs").Value ?? "1000");
            StatusSendWaitTimeMs = int.Parse(config.GetSection("Settings:StatusSendWaitTimeMs").Value ?? "1000");
            replyTimeoutMs = int.Parse(config.GetSection("Settings:ReplyTimeoutMs").Value ?? "2000");
            Console.WriteLine("InfluxDB URL: " + HTTPInfluxUrl);
        }
        public string GetInfluxUrl()
        {
            return HTTPInfluxUrl;
        }
        private byte[] HexStringToByteArray(string HexString)
        {
            int StringNum = HexString.Length;
            byte[] hexBytes = new byte[StringNum / 2];
            for (int i = 0; i < StringNum; i += 2)
            {
                hexBytes[i / 2] = Convert.ToByte(HexString.Substring(i, 2), 16);
            }
            return hexBytes;
        }
        public int UserConnect(string ip, int port)
        {
            try
            {
                if (UserClient.Connected)
                {
                    UserClient.Close();
                }
                UserClient = new TcpClient(); // New instance
                UserClient.ReceiveTimeout = 3000;
                UserClient.Connect(ip, port);
                _ip = ip;
                _port = port;
                SendMessageEvent?.Invoke("IP: " + ip + " Port: " + port + " connected successfully\r\n");
                return 0;
            }
            catch (Exception ex)
            {
                SendMessageEvent?.Invoke("Failed to connect to " + ip + ":" + port + ". Error: " + ex.Message + "\r\n");
                return -1;
            }
        }
        public int UserDisconnect()
        {
            try
            {
                if (UserClient.Connected)
                {
                    UserClient.Close();
                    SendMessageEvent?.Invoke("User client disconnected successfully\r\n");
                }
                else
                {
                    SendMessageEvent?.Invoke("User client is already disconnected\r\n");
                }
                return 0;
            }
            catch (Exception ex)
            {
                SendMessageEvent?.Invoke("Failed to disconnect user client. Error: " + ex.Message + "\r\n");
                return -1;
            }
        }

        // check TcpClient connection status
        private bool IsClientAlive(TcpClient c)
        {
            try
            {
                if (c == null) return false;
                if (!c.Client.Connected) return false;
                if (c.Client.Poll(1, SelectMode.SelectRead) && c.Available == 0) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Wait and attempt to reconnect
        private async Task<bool> WaitReconnectAsync(CancellationToken token)
        {
            if (string.IsNullOrEmpty(_ip) || _port == 0)
            {
                SendMessageEvent?.Invoke("Reconnect skipped: no stored endpoint.\r\n");
                return false;
            }

            await _reconnectLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (IsClientAlive(UserClient)) return true; 

                SendMessageEvent?.Invoke("TCP disconnected. Trying to reconnect...\r\n");
                int attempt = 0;
                while (!token.IsCancellationRequested)
                {
                    attempt++;
                    try
                    {
                        UserConnect(_ip, _port);
                        if (IsClientAlive(UserClient))
                        {
                            SendMessageEvent?.Invoke($"Reconnected (attempt {attempt}).\r\n");
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        SendMessageEvent?.Invoke($"Reconnect attempt {attempt} failed: {ex.Message}\r\n");
                    }
                    await Task.Delay(replyTimeoutMs, token).ConfigureAwait(false);
                }
                return false;
            }
            finally
            {
                _reconnectLock.Release();
            }
        }

        public void TCPWriteToUser(string HexString)
        {
            if (UserClient.Connected)
            {
                try
                {
                    byte[] hexBytes = HexStringToByteArray(HexString);
                    byte[] CmdBytes = new byte[hexBytes.Length];
                    for (int i = 0; i < hexBytes.Length; i++)
                    {
                        CmdBytes[i] = hexBytes[i]; // HexString to byte array does not need further inversion
                    }
                    NetworkStream stream = UserClient.GetStream();
                    stream.Write(CmdBytes, 0, CmdBytes.Length);
                    SendMessageEvent?.Invoke(HexString + " sent successfully\r\n");
                    SendMessageEvent?.Invoke("Sent Data: " + BitConverter.ToString(CmdBytes).Replace("-", " ") + "\r\n");
                }
                catch (Exception ex)
                {
                    SendMessageEvent?.Invoke("Failed to send data. Error: " + ex.Message + "\r\n");
                }
            }
            else
            {
                SendMessageEvent?.Invoke("User client is not connected.\r\n");
            }
        }

        public string TCPReadFromUser()
        {
            UserClient.ReceiveBufferSize = 2;
            UserClient.ReceiveTimeout = 1000; // Set a timeout for reading
            if (UserClient.Connected)
            {
                try
                {
                    NetworkStream stream = UserClient.GetStream();
                    if (stream.DataAvailable)
                    {
                        byte[] buffer = new byte[UserClient.ReceiveBufferSize];
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        string receivedData = BitConverter.ToString(buffer, 0, bytesRead).Replace("-", " ");
                        SendMessageEvent?.Invoke("Received Data: " + receivedData + "\r\n");
                        return receivedData;
                    }
                    else
                    {
                        SendMessageEvent?.Invoke("No data available to read.\r\n");
                        return string.Empty;
                    }
                }
                catch (Exception ex)
                {
                    SendMessageEvent?.Invoke("Failed to read data. Error: " + ex.Message + "\r\n");
                    return string.Empty;
                }
            }
            else
            {
                SendMessageEvent?.Invoke("User client is not connected.\r\n");
                return string.Empty;
            }
        }

        public void StartTCPReadFromUserToFile(string FileDic)
        {
            DataAcqMultiThreadTokens.Dispose();
            DataAcqMultiThreadTokens = new CancellationTokenSource();
            BwWriteTokens.Dispose();
            BwWriteTokens = new CancellationTokenSource();
            TcpReceiveTokens.Dispose();
            TcpReceiveTokens = new CancellationTokenSource();
            FileName = string.Format("{0:yyyyMMdd_HHHHmmss}", DateTime.Now) + ".dat";
            if (!Directory.Exists(FileDic))
            {
                Directory.CreateDirectory(FileDic);
            }
            BinaryWriter bw = new BinaryWriter(File.Open(Path.Combine(FileDic, FileName), FileMode.Append, FileAccess.Write, FileShare.Read));
            //write pkg_begin
            try
            {
                Task DataAcqMultiThreadTask = Task.Factory.StartNew(() => this.DataAcqFunction_MultiThread(DataAcqMultiThreadTokens.Token, BwWriteTokens.Token, TcpReceiveTokens.Token, UserClient, ServerClient, bw), DataAcqMultiThreadTokens.Token);

            }
            catch (AggregateException excption)
            {
                foreach (var v in excption.InnerExceptions)
                {
                    SendMessageEvent?.Invoke(excption.Message + " " + v.Message);
                    //MessageTextbox.AppendText(excption.Message + " " + v.Message);
                }
            }
        }

        public void StopTCPReadFromUserToFile()
        {
            DataAcqMultiThreadTokens.Cancel();
            BwWriteTokens.Cancel();
            TcpReceiveTokens.Cancel();
        }


        private void DataAcqFunction(CancellationToken token, TcpClient UserClient, BinaryWriter bw)
        {
            int Data_Len = 8192;
            UserClient.ReceiveBufferSize = Data_Len;
            UserClient.ReceiveTimeout = 1000;
            Timer tmr = new Timer(new TimerCallback(Received_Data_Size), null, -1, 3000);
            tmr.Change(0, 3000);
            byte[] Data_buffer = new byte[Data_Len];
            NetworkStream stream = UserClient.GetStream();
            while (true)
            {
                if (token.IsCancellationRequested == true)
                {
                    Thread.Sleep(100);
                    int TcpReceiveLength = 0;
                    if (stream.DataAvailable)
                    {
                        TcpReceiveLength = stream.Read(Data_buffer, 0, Data_buffer.Length);
                    }
                    else
                    {
                        TcpReceiveLength = 0;
                        bw.Flush();
                        break;
                    }

                    if (TcpReceiveLength == 0)
                    {
                        bw.Flush();
                        break;
                    }
                    //MessageTextbox.AppendText("Received Data Bytes: " + TcpReceiveLength + "\r\n");
                    bw.Write(Data_buffer, 0, TcpReceiveLength);
                }
                else
                {
                    if (stream.DataAvailable)
                    {
                        int TcpReceiveLength = stream.Read(Data_buffer, 0, Data_buffer.Length);
                        bw.Write(Data_buffer, 0, TcpReceiveLength);
                        SumBytes += TcpReceiveLength;
                        bw.Flush();
                    }
                }
            }
            SumBytes = 0;
            tmr.Change(Timeout.Infinite, 5000);
            tmr.Dispose();
            bw.Flush();
            bw.Close();
            bw.Dispose();
        }

        private void DataAcqFunction_MultiThread(CancellationToken token, CancellationToken BwWriteTokens, CancellationToken TcpReceiveTokens, TcpClient UserClient, TcpClient ServerClient, BinaryWriter bw)
        {
            UserClient.ReceiveBufferSize = 1024 * 1024;
            UserClient.ReceiveTimeout = 1000;
            try
            {
                Task TcpReceiveTask = Task.Factory.StartNew(() => this.TcpReceiveFunction(TcpReceiveTokens, UserClient, ServerClient), TcpReceiveTokens);
                Task BwWriteTask = Task.Factory.StartNew(() => this.BwWriteFunction(BwWriteTokens, bw), BwWriteTokens);
            }
            catch (AggregateException excption)
            {
                foreach (var v in excption.InnerExceptions)
                {
                    SendMessageEvent?.Invoke(excption.Message + " " + v.Message);
                    //MessageTextbox.AppendText(excption.Message + " " + v.Message);
                }
            }
        }

        private void BwWriteFunction(CancellationToken token, BinaryWriter bw)
        {
            Timer tmr = new Timer(new TimerCallback(Received_Data_Size), null, -1, 3000);
            tmr.Change(0, 3000);
            while (true)
            {
                if (token.IsCancellationRequested == true)
                {
                    if (DataBuffer0_canread)
                    {
                        bw.Write(DataBuffer0, 0, DataBuffer0_canreadlength);
                        DataBuffer0_canwrite = true;
                        DataBuffer0_canread = false;
                    }
                    else if (DataBuffer1_canread)
                    {
                        bw.Write(DataBuffer1, 0, DataBuffer1_canreadlength);
                        DataBuffer1_canwrite = true;
                        DataBuffer1_canread = false;
                    }
                    else if (DataBuffer2_canread)
                    {
                        bw.Write(DataBuffer2, 0, DataBuffer2_canreadlength);
                        DataBuffer2_canwrite = true;
                        DataBuffer2_canread = false;
                    }
                    else if (DataBuffer3_canread)
                    {
                        bw.Write(DataBuffer3, 0, DataBuffer3_canreadlength);
                        DataBuffer3_canwrite = true;
                        DataBuffer3_canread = false;
                    }
                    else
                    {
                        bw.Flush();
                        break;
                    }
                    bw.Flush();
                }
                else
                {

                    if (DataBuffer0_canread)
                    {
                        bw.Write(DataBuffer0, 0, DataBuffer0_canreadlength);
                        DataBuffer0_canwrite = true;
                        DataBuffer0_canread = false;
                        SumBytes += DataBuffer0_canreadlength;
                    }
                    else if (DataBuffer1_canread)
                    {
                        bw.Write(DataBuffer1, 0, DataBuffer1_canreadlength);
                        DataBuffer1_canwrite = true;
                        DataBuffer1_canread = false;
                        SumBytes += DataBuffer1_canreadlength;
                    }
                    else if (DataBuffer2_canread)
                    {
                        bw.Write(DataBuffer2, 0, DataBuffer2_canreadlength);
                        DataBuffer2_canwrite = true;
                        DataBuffer2_canread = false;
                        SumBytes += DataBuffer2_canreadlength;
                    }
                    else if (DataBuffer3_canread)
                    {
                        bw.Write(DataBuffer3, 0, DataBuffer3_canreadlength);
                        DataBuffer3_canwrite = true;
                        DataBuffer3_canread = false;
                        SumBytes += DataBuffer3_canreadlength;
                    }
                    else
                    {
                    }
                    bw.Flush();
                }
            }
            SumBytes = 0;
            tmr.Change(Timeout.Infinite, 5000);
            tmr.Dispose();
            bw.Flush();
            bw.Close();
            bw.Dispose();
        }

        private void TcpReceiveFunction(CancellationToken token, TcpClient UserClient, TcpClient ServerClient)
        {
            NetworkStream stream = UserClient.GetStream();
            int ReBuffSize = 1024 * 1024;
            while (true)
            {
                if (token.IsCancellationRequested == true)
                {
                    int TcpReceiveLength = 0;
                    if (stream.DataAvailable)
                    {
                        if (DataBuffer0_canwrite)
                        {
                            TcpReceiveLength = stream.Read(DataBuffer0, 0, ReBuffSize);
                            if (this.IsSendEnabled)
                            {
                                NetworkStream serverStream = ServerClient.GetStream();
                                serverStream.Write(DataBuffer0, 0, TcpReceiveLength);
                            }
                            DataBuffer0_canreadlength = TcpReceiveLength;
                            DataBuffer0_canread = true;
                            DataBuffer0_canwrite = false;
                        }
                        else if (DataBuffer1_canwrite)
                        {
                            TcpReceiveLength = stream.Read(DataBuffer1, 0, ReBuffSize);
                            if (this.IsSendEnabled)
                            {
                                NetworkStream serverStream = ServerClient.GetStream();
                                serverStream.Write(DataBuffer1, 0, TcpReceiveLength);
                            }
                            DataBuffer1_canreadlength = TcpReceiveLength;
                            DataBuffer1_canread = true;
                            DataBuffer1_canwrite = false;

                        }
                        else if (DataBuffer2_canwrite)
                        {
                            TcpReceiveLength = stream.Read(DataBuffer2, 0, ReBuffSize);
                            if (this.IsSendEnabled)
                            {
                                NetworkStream serverStream = ServerClient.GetStream();
                                serverStream.Write(DataBuffer2, 0, TcpReceiveLength);
                            }
                            DataBuffer2_canreadlength = TcpReceiveLength;
                            DataBuffer2_canread = true;
                            DataBuffer2_canwrite = false;
                        }
                        else if (DataBuffer3_canwrite)
                        {
                            TcpReceiveLength = stream.Read(DataBuffer3, 0, ReBuffSize);
                            if (this.IsSendEnabled)
                            {
                                NetworkStream serverStream = ServerClient.GetStream();
                                serverStream.Write(DataBuffer3, 0, TcpReceiveLength);
                            }
                            DataBuffer3_canreadlength = TcpReceiveLength;
                            DataBuffer3_canread = true;
                            DataBuffer3_canwrite = false;
                        }
                        else
                        {
                            SendMessage("WARNING: DATA MAY OVERFLOW!");
                            //return;
                        }
                    }
                    else
                    {
                        TcpReceiveLength = 0;
                        DataBuffer0_cancheck = false;
                        DataBuffer0_canwrite = true;
                        DataBuffer1_cancheck = false;
                        DataBuffer1_canwrite = true;
                        DataBuffer2_cancheck = false;
                        DataBuffer2_canwrite = true;
                        DataBuffer3_cancheck = false;
                        DataBuffer3_canwrite = true;
                        break;
                    }

                    if (TcpReceiveLength == 0)
                    {
                        DataBuffer0_canread = false;
                        DataBuffer0_canwrite = true;
                        DataBuffer1_canread = false;
                        DataBuffer1_canwrite = true;
                        DataBuffer2_canread = false;
                        DataBuffer2_canwrite = true;
                        DataBuffer3_canread = false;
                        DataBuffer3_canwrite = true;
                        break;
                    }
                }
                else
                {
                    if (stream.DataAvailable)
                    {
                        int TcpReceiveLength = 0;
                        if (DataBuffer0_canwrite)
                        {
                            TcpReceiveLength = stream.Read(DataBuffer0, 0, ReBuffSize);
                            if (this.IsSendEnabled)
                            {
                                NetworkStream serverStream = ServerClient.GetStream();
                                serverStream.Write(DataBuffer0, 0, TcpReceiveLength);
                            }
                            DataBuffer0_canreadlength = TcpReceiveLength;
                            DataBuffer0_canread = true;
                            DataBuffer0_canwrite = false;

                        }
                        else if (DataBuffer1_canwrite)
                        {
                            TcpReceiveLength = stream.Read(DataBuffer1, 0, ReBuffSize);
                            if (this.IsSendEnabled)
                            {
                                NetworkStream serverStream = ServerClient.GetStream();
                                serverStream.Write(DataBuffer1, 0, TcpReceiveLength);
                            }
                            DataBuffer1_canreadlength = TcpReceiveLength;
                            DataBuffer1_canread = true;
                            DataBuffer1_canwrite = false;

                        }
                        else if (DataBuffer2_canwrite)
                        {
                            TcpReceiveLength = stream.Read(DataBuffer2, 0, ReBuffSize);
                            if (this.IsSendEnabled)
                            {
                                NetworkStream serverStream = ServerClient.GetStream();
                                serverStream.Write(DataBuffer2, 0, TcpReceiveLength);
                            }
                            DataBuffer2_canreadlength = TcpReceiveLength;
                            DataBuffer2_canread = true;
                            DataBuffer2_canwrite = false;
                        }
                        else if (DataBuffer3_canwrite)
                        {
                            TcpReceiveLength = stream.Read(DataBuffer3, 0, ReBuffSize);
                            if (this.IsSendEnabled)
                            {
                                NetworkStream serverStream = ServerClient.GetStream();
                                serverStream.Write(DataBuffer3, 0, TcpReceiveLength);
                            }
                            DataBuffer3_canreadlength = TcpReceiveLength;
                            DataBuffer3_canread = true;
                            DataBuffer3_canwrite = false;
                        }
                        else
                        {
                            SendMessage("WARNING: DATA MAY OVERFLOW!");
                            //return;
                        }
                    }
                }
            }
        }
        public void StartTemperatureMonitoring(string FolderDic)
        {
            TemperatureMonitoringTokens.Dispose();
            TemperatureMonitoringTokens = new CancellationTokenSource();
            //MessageTextbox.AppendText("Temperature monitoring start\n");
            SendMessageEvent?.Invoke("Temperature monitoring start\r\n");
            DataAcqTokens.Dispose();
            DataAcqTokens = new CancellationTokenSource();
            FileName = string.Format("{0:yyyyMMdd_HHHHmmss}", DateTime.Now) + ".dat";
            if (!Directory.Exists(FolderDic))
            {
                Directory.CreateDirectory(FolderDic);
            }
            BinaryWriter bw = new BinaryWriter(File.Open(Path.Combine(FolderDic, FileName), FileMode.Append, FileAccess.Write, FileShare.Read));
            try
            {
                Task TMTask = Task.Factory.StartNew(() => this.TM_threadFunc(TemperatureMonitoringTokens.Token, UserClient, bw), TemperatureMonitoringTokens.Token);
            }
            catch (AggregateException excption)
            {
                foreach (var v in excption.InnerExceptions)
                {
                    SendMessageEvent?.Invoke(excption.Message + " " + v.Message);
                    //MessageTextbox.AppendText(excption.Message + " " + v.Message);
                }
            }

        }
        public void StopTemperatureMonitoring()
        {
            TemperatureMonitoringTokens.Cancel();
            DataAcqTokens.Cancel();
        }
        private void TM_threadFunc(CancellationToken taskToken, TcpClient UserClient, BinaryWriter bw)
        {
            byte[] CmdBytes = new byte[2];
            //temperature Refresh Command is 0x1100
            CmdBytes[0] = 0x11;
            CmdBytes[1] = 0x00;
            NetworkStream stream = UserClient.GetStream();
            DateTime StartTime = DateTime.Now;
            byte[] Time_buffer = new byte[4];
            DateTime NowTime = new DateTime();
            DataAcqTokens.Dispose();
            DataAcqTokens = new CancellationTokenSource();
            while (true)
            {
                if (taskToken.IsCancellationRequested == true)
                {
                    DataAcqTokens.Cancel();
                    bw.Flush();
                    bw.Close();
                    bw.Dispose();
                    break;
                }
                else
                {
                    NowTime = DateTime.Now;
                    Time_buffer[0] = (BitConverter.GetBytes(NowTime.Day))[0];
                    Time_buffer[1] = (BitConverter.GetBytes(NowTime.Hour))[0];
                    Time_buffer[2] = (BitConverter.GetBytes(NowTime.Minute))[0];
                    Time_buffer[3] = (BitConverter.GetBytes(NowTime.Second))[0];
                    bw.Write(Time_buffer, 0, 4);
                    stream.Write(CmdBytes, 0, CmdBytes.Length);
                    Thread.Sleep(100);
                    try
                    {
                        Task DataAcqTask = Task.Factory.StartNew(() => this.DataAcqFunction(DataAcqTokens.Token, UserClient, bw), DataAcqTokens.Token);
                    }
                    catch (AggregateException excption)
                    {
                        foreach (var v in excption.InnerExceptions)
                        {
                            SendMessageEvent?.Invoke(excption.Message + " " + v.Message);
                            //MessageTextbox.AppendText(excption.Message + " " + v.Message);
                        }
                    }
                    Thread.Sleep(60000);
                }
            }
            bw.Flush();
            bw.Close();
            bw.Dispose();

        }
        private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int expected, int timeoutMs, CancellationToken token)
        {
            int offset = 0;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(timeoutMs);

            try
            {
                while (offset < expected)
                {
                    int n = await stream.ReadAsync(buffer.AsMemory(offset, expected - offset), cts.Token);
                    //if (n == 0) break;
                    offset += n;
                }
            }
            catch (OperationCanceledException)
            {
            }
            return offset;
        }
        public void Current_Refresh()
        {
            byte[] CmdBytes = new byte[2];
            //Current_Refresh Command is 0x1200
            CmdBytes[0] = 0x12;
            CmdBytes[1] = 0x00;
            NetworkStream stream = UserClient.GetStream();
            stream.Write(CmdBytes, 0, CmdBytes.Length);
            //UserClient.ReceiveBufferSize = 192;//48*4
            //UserClient.ReceiveTimeout = 1000;
            const int ExpectedBytes = 192; // 48 words * 4 bytes/word
            var buf = new byte[ExpectedBytes];
            DateTime StartTime = DateTime.Now;
            DateTime NowTime = new DateTime();
            TimeSpan DurationTime = new TimeSpan();
            while (!stream.DataAvailable)
            {
                NowTime = DateTime.Now;
                DurationTime = NowTime - StartTime;
                if (DurationTime.TotalMilliseconds > 3000)
                {
                    //MessageBox.Show("No Rata Received");
                    SendMessageEvent?.Invoke("No Data Received\r\n");
                    break;
                }
            }
            int read = ReadExactAsync(stream, buf, ExpectedBytes, 3000, CancellationToken.None).GetAwaiter().GetResult();
            if (read != ExpectedBytes)
            {
                SendMessageEvent?.Invoke("Failed to receive complete current data\r\n");
                SendMessageEvent?.Invoke("Received Data Bytes: " + read + "/" + ExpectedBytes + "\r\n");
                return;
            }
            byte[] TcpReceiveData = buf;
            int TcpReceiveLength = read;
            //DateTime StartTime = DateTime.Now;
            //DateTime NowTime = new DateTime();
            //TimeSpan DurationTime = new TimeSpan();
            //while (!stream.DataAvailable)
            //{
            //    NowTime = DateTime.Now;
            //    DurationTime = NowTime - StartTime;
            //    if (DurationTime.TotalMilliseconds > 3000)
            //    {
            //        //MessageBox.Show("No Rata Received");
            //        SendMessageEvent?.Invoke("No Data Received\r\n");
            //        break;
            //    }
            //}
            //if (stream.DataAvailable)
            //{
            //byte[] TcpReceiveData = new byte[UserClient.ReceiveBufferSize];
            //int TcpReceiveLength = stream.Read(TcpReceiveData, 0, TcpReceiveData.Length);
            ////MessageTextbox.AppendText("Received Data Bytes: " + TcpReceiveLength + "\r\n");
            SendMessageEvent?.Invoke("Received Data Bytes: " + TcpReceiveLength + "\r\n");
            int[] dec_word = new int[48];
            double[] current_value = new double[48];
            for (int i = 0; i < 48; i++)
            {
                dec_word[i] = (TcpReceiveData[4 * i] << 24) + (TcpReceiveData[4 * i + 1] << 16) + (TcpReceiveData[4 * i + 2] << 8) + TcpReceiveData[4 * i + 3];
                current_value[i] = (dec_word[i] - 0x00010000) / 65536.0 * 2.5 / 2.0 / 60.0 / 0.005 * 1000;
            }
            SendCurrentEvent?.Invoke(new Tuple<int, string>(0, current_value[0].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(1, current_value[8].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(2, current_value[1].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(3, current_value[9].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(4, current_value[2].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(5, current_value[10].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(6, current_value[3].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(7, current_value[11].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(8, current_value[4].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(9, current_value[12].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(10, current_value[5].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(11, current_value[13].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(12, current_value[6].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(13, current_value[14].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(14, current_value[7].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(15, current_value[15].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(16, current_value[16].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(17, current_value[24].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(18, current_value[17].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(19, current_value[25].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(20, current_value[18].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(21, current_value[26].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(22, current_value[19].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(23, current_value[27].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(24, current_value[20].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(25, current_value[28].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(26, current_value[21].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(27, current_value[29].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(28, current_value[22].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(29, current_value[30].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(30, current_value[23].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(31, current_value[31].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(32, current_value[32].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(33, current_value[40].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(34, current_value[33].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(35, current_value[41].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(36, current_value[34].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(37, current_value[42].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(38, current_value[35].ToString()));
            SendCurrentEvent?.Invoke(new Tuple<int, string>(39, current_value[43].ToString()));
            //}
        }
        int CurrentMatrix(int ch)
        {
            if (ch == 0) return 0;
            else if (ch == 1) return 8;
            else if (ch == 2) return 1;
            else if (ch == 3) return 9;
            else if (ch == 4) return 2;
            else if (ch == 5) return 10;
            else if (ch == 6) return 3;
            else if (ch == 7) return 11;
            else if (ch == 8) return 4;
            else if (ch == 9) return 12;
            else if (ch == 10) return 5;
            else if (ch == 11) return 13;
            else if (ch == 12) return 6;
            else if (ch == 13) return 14;
            else if (ch == 14) return 7;
            else if (ch == 15) return 15;
            else if (ch == 16) return 16;
            else if (ch == 17) return 24;
            else if (ch == 18) return 17;
            else if (ch == 19) return 25;
            else if (ch == 20) return 18;
            else if (ch == 21) return 26;
            else if (ch == 22) return 19;
            else if (ch == 23) return 27;
            else if (ch == 24) return 20;
            else if (ch == 25) return 28;
            else if (ch == 26) return 21;
            else if (ch == 27) return 29;
            else if (ch == 28) return 22;
            else if (ch == 29) return 30;
            else if (ch == 30) return 23;
            else if (ch == 31) return 31;
            else if (ch == 32) return 32;
            else if (ch == 33) return 40;
            else if (ch == 34) return 33;
            else if (ch == 35) return 41;
            else if (ch == 36) return 34;
            else if (ch == 37) return 42;
            else if (ch == 38) return 35;
            else if (ch == 39) return 43;
            else throw new ArgumentOutOfRangeException(nameof(ch), "Channel must be between 0 and 39.");
        }
        public double DecodeCurrent(int[] dec_word, int channel)
        {
            if (channel < 0 || channel > 39)
            {
                throw new ArgumentOutOfRangeException(nameof(channel), "Channel must be between 0 and 39.");
            }
            int index = CurrentMatrix(channel);
            double current_value = (dec_word[index] - 0x00010000) / 65536.0 * 2.5 / 2.0 / 60.0 / 0.005 * 1000;
            return current_value;
        }

        public async Task CurrentAcqSendFunction(CancellationToken token, TcpClient UserClient)
        {
            byte[] CmdBytes = new byte[2];
            //Current_Refresh Command is 0x1200
            CmdBytes[0] = 0x12;
            CmdBytes[1] = 0x00;
            NetworkStream stream = UserClient.GetStream();
            //UserClient.ReceiveBufferSize = 192;//48*4
            UserClient.ReceiveTimeout = 1000;
            const int ExpectedBytes = 192; // 48 words * 4 bytes/word
            var buf = new byte[ExpectedBytes];
            while (!token.IsCancellationRequested)
            {
                stream.Write(CmdBytes, 0, CmdBytes.Length);
                DateTime StartTime = DateTime.Now;
                DateTime NowTime = new DateTime();
                TimeSpan DurationTime = new TimeSpan();
                while (!stream.DataAvailable)
                {
                    NowTime = DateTime.Now;
                    DurationTime = NowTime - StartTime;
                    if (DurationTime.TotalMilliseconds > 3000)
                    {
                        //MessageBox.Show("No Rata Received");
                        SendMessageEvent?.Invoke("No Data Received\r\n");
                        break;
                    }
                }
                int read = await ReadExactAsync(stream, buf, ExpectedBytes, 1000, token);
                if (read != ExpectedBytes)
                {
                    SendMessageEvent?.Invoke($"Current frame incomplete: {read}/{ExpectedBytes} bytes\r\n");
                    await Task.Delay(CurrentSendWaitTimeMs, token);
                    continue;
                }

                DateTime now = DateTime.Now;
                int[] dec_word = new int[48];
                for (int i = 0; i < 48; i++)
                    dec_word[i] = (buf[4 * i] << 24) + (buf[4 * i + 1] << 16) + (buf[4 * i + 2] << 8) + buf[4 * i + 3];

                int[] channels = new int[40];
                double[] values = new double[40];
                for (int i = 0; i < 40; i++)
                {
                    channels[i] = i;
                    double current_value = DecodeCurrent(dec_word, i);
                    values[i] = current_value;
                    SendCurrentEvent?.Invoke(new Tuple<int, string>(i, current_value.ToString(CultureInfo.InvariantCulture)));
                }
                await UploadAllChCurrentToInfluxAsync(channels, values, now);

                await Task.Delay(CurrentSendWaitTimeMs, token);
            }
        }
        //    DateTime NowTime = new DateTime();
        //    TimeSpan DurationTime = new TimeSpan();
        //    byte[] TcpReceiveData = new byte[UserClient.ReceiveBufferSize];
        //    while (true)
        //    {
        //        stream.Write(CmdBytes, 0, CmdBytes.Length);
        //        DateTime StartTime = DateTime.Now;
        //        int TcpReceiveLength = 0;
        //        if (token.IsCancellationRequested == true)
        //        {
        //            Thread.Sleep(100);
        //            NowTime = DateTime.Now;
        //            if (stream.DataAvailable)
        //            {
        //                NowTime = DateTime.Now;
        //                TcpReceiveLength = stream.Read(TcpReceiveData, 0, TcpReceiveData.Length);
        //            }
        //            else
        //            {
        //                TcpReceiveLength = 0;
        //                break;
        //            }

        //            if (TcpReceiveLength == 0)
        //            {
        //                break;
        //            }
        //            //MessageTextbox.AppendText("Received Data Bytes: " + TcpReceiveLength + "\r\n");
        //            int[] dec_word = new int[48];
        //            for (int i = 0; i < 48; i++)
        //            {
        //                dec_word[i] = (TcpReceiveData[4 * i] << 24) + (TcpReceiveData[4 * i + 1] << 16) + (TcpReceiveData[4 * i + 2] << 8) + TcpReceiveData[4 * i + 3];
        //            }
        //            int[] channels = new int[40];
        //            double[] values = new double[40];
        //            for (int i = 0; i < 40; i++)
        //            {
        //                channels[i] = i;
        //                double current_value = DecodeCurrent(dec_word, i);
        //                values[i] = current_value;
        //                SendCurrentEvent?.Invoke(new Tuple<int, string>(i, current_value.ToString(CultureInfo.InvariantCulture)));
        //            }
        //            await UploadAllChCurrentToInfluxAsync(channels, values, NowTime);
        //        }
        //        else
        //        {
        //            while (!stream.DataAvailable)
        //            {
        //                NowTime = DateTime.Now;
        //                DurationTime = NowTime - StartTime;
        //                if (DurationTime.TotalMilliseconds > 3000)
        //                {
        //                    //MessageBox.Show("No Rata Received");
        //                    SendMessageEvent?.Invoke("No Data Received\r\n");
        //                    break;
        //                }
        //            }
        //            if (stream.DataAvailable)
        //            {
        //                NowTime = DateTime.Now;
        //                TcpReceiveLength = stream.Read(TcpReceiveData, 0, TcpReceiveData.Length);
        //                int[] dec_word = new int[48];
        //                for (int i = 0; i < 48; i++)
        //                {
        //                    dec_word[i] = (TcpReceiveData[4 * i] << 24) + (TcpReceiveData[4 * i + 1] << 16) + (TcpReceiveData[4 * i + 2] << 8) + TcpReceiveData[4 * i + 3];
        //                }
        //                int[] channels = new int[40];
        //                double[] values = new double[40];
        //                for (int i = 0; i < 40; i++)
        //                {   
        //                    channels[i] = i;
        //                    double current_value = DecodeCurrent(dec_word, i);
        //                    values[i] = current_value;
        //                    SendCurrentEvent?.Invoke(new Tuple<int, string>(i, current_value.ToString(CultureInfo.InvariantCulture)));
        //                }
        //                await UploadAllChCurrentToInfluxAsync(channels, values, NowTime);
        //            }
        //        }
        //        await Task.Delay(CurrentSendWaitTimeMs, token);
        //    }
        //}

        public void StartCurrentAcqSend()
        {
            CurrentAcqSendTokens.Dispose();
            CurrentAcqSendTokens = new CancellationTokenSource();
            SendMessageEvent?.Invoke("Current acquisition and send started.\r\n");
            try
            {
                Task CurrentAcqSendTask = Task.Factory.StartNew(() => this.CurrentAcqSendFunction(CurrentAcqSendTokens.Token, UserClient), CurrentAcqSendTokens.Token);
            }
            catch (AggregateException excption)
            {
                foreach (var v in excption.InnerExceptions)
                {
                    SendMessageEvent?.Invoke(excption.Message + " " + v.Message);
                    //MessageTextbox.AppendText(excption.Message + " " + v.Message);
                }
            }
        }
        public void StopCurrentAcqSend()
        {
            CurrentAcqSendTokens.Cancel();
            SendMessageEvent?.Invoke("Current acquisition and send stopped.\r\n");
        }
        public async Task TemperatureAcqSendFunction(CancellationToken token, TcpClient UserClient)
        {
            byte[] CmdBytes = new byte[2];
            //Tem_Refresh Command is 0x1100
            CmdBytes[0] = 0x11;
            CmdBytes[1] = 0x00;
            NetworkStream stream = UserClient.GetStream();
            //UserClient.ReceiveBufferSize = 7680; //40*48*4
            //UserClient.ReceiveBufferSize = 8192; //40*48*4 + some margin
            UserClient.ReceiveTimeout = 1000;
            const int ExpectedBytes = 7680; // 40 layers * 48 channels * 4 bytes
            var buf = new byte[ExpectedBytes];
            while (!token.IsCancellationRequested)
            {
                stream.Write(CmdBytes, 0, CmdBytes.Length);
                DateTime StartTime = DateTime.Now;
                DateTime NowTime = new DateTime();
                TimeSpan DurationTime = new TimeSpan();
                while (!stream.DataAvailable)
                {
                    NowTime = DateTime.Now;
                    DurationTime = NowTime - StartTime;
                    if (DurationTime.TotalMilliseconds > 3000)
                    {
                        //MessageBox.Show("No Rata Received");
                        SendMessageEvent?.Invoke("No Data Received\r\n");
                        break;
                    }
                }
                int read = await ReadExactAsync(stream, buf, ExpectedBytes, 1000, token);
                if (read != ExpectedBytes)
                {
                    SendMessageEvent?.Invoke($"Temperature frame incomplete: {read}/{ExpectedBytes} bytes\r\n");
                    await Task.Delay(TemperatureSendWaitTimeMs, token);
                    continue;
                }
                DateTime now = DateTime.Now;
                byte[] TcpReceiveData = buf;
                double[] temperatures = new double[40 * 48];
                int[] channels = new int[40 * 48];
                int[] layers = new int[40 * 48];
                double[] maxlayer = new double[40];
                double[] avglayer = new double[40];
                double[] minlayer = new double[40];
                for (int i = 0; i < 40; i++)
                {
                    maxlayer[i] = -100.0;
                    avglayer[i] = 0.0;
                    minlayer[i] = 200.0;
                }
                for (int i = 0; i < 40 * 48; i++)
                {
                    temperatures[i] = (TcpReceiveData[4 * i] * 256 + TcpReceiveData[4 * i + 1]) / 128.0;
                    channels[i] = TcpReceiveData[4 * i + 2];
                    layers[i] = TcpReceiveData[4 * i + 3];
                    if (temperatures[i] > maxlayer[layers[i]])
                    {
                        maxlayer[layers[i]] = temperatures[i];
                    }
                    if (temperatures[i] < minlayer[layers[i]])
                    {
                        minlayer[layers[i]] = temperatures[i];
                    }
                    avglayer[layers[i]] += temperatures[i];
                }
                for (int i = 0; i < 40; i++)
                {
                    avglayer[i] /= 48.0;
                    SendTemperatureEvent?.Invoke(new Tuple<int, string, string, string>(i, maxlayer[i].ToString("F2", CultureInfo.InvariantCulture), avglayer[i].ToString("F2", CultureInfo.InvariantCulture), minlayer[i].ToString("F2", CultureInfo.InvariantCulture)));
                }
                await UploadAllChTemperaturetToInfluxAsync(layers, channels, temperatures, now);
                await Task.Delay(TemperatureSendWaitTimeMs, token);
            }
        }
        //    DateTime NowTime = new DateTime();
        //    TimeSpan DurationTime = new TimeSpan();
        //    byte[] TcpReceiveData = new byte[UserClient.ReceiveBufferSize];
        //    while (true)
        //    {
        //        stream.Write(CmdBytes, 0, CmdBytes.Length);
        //        DateTime StartTime = DateTime.Now;
        //        int TcpReceiveLength = 0;
        //        if (token.IsCancellationRequested == true)
        //        {
        //            Thread.Sleep(100);
        //            NowTime = DateTime.Now;
        //            if (stream.DataAvailable)
        //            {
        //                NowTime = DateTime.Now;
        //                TcpReceiveLength = stream.Read(TcpReceiveData, 0, TcpReceiveData.Length);
        //            }
        //            else
        //            {
        //                TcpReceiveLength = 0;
        //                break;
        //            }

        //            if (TcpReceiveLength == 0)
        //            {
        //                break;
        //            }
        //            //MessageTextbox.AppendText("Received Data Bytes: " + TcpReceiveLength + "\r\n");
        //            double[] temperatures = new double[40*48];
        //            int[] channels = new int[40*48];
        //            int[] layers = new int[40*48];
        //            double[] maxlayer = new double[40];
        //            double[] avglayer = new double[40];
        //            double[] minlayer = new double[40];
        //            for (int i = 0; i < 40; i++)
        //            {
        //                maxlayer[i] = -100.0;
        //                avglayer[i] = 0.0;
        //                minlayer[i] = 200.0;
        //            }
        //            for (int i = 0; i < 40 * 48; i++)
        //            {
        //                temperatures[i] = (TcpReceiveData[4 * i] * 256 + TcpReceiveData[4 * i + 1]) / 128.0;
        //                channels[i] = TcpReceiveData[4 * i + 2];
        //                layers[i] = TcpReceiveData[4 * i + 3];
        //                if (temperatures[i] > maxlayer[layers[i]])
        //                {
        //                    maxlayer[layers[i]] = temperatures[i];
        //                }
        //                if (temperatures[i] < minlayer[layers[i]])
        //                {
        //                    minlayer[layers[i]] = temperatures[i];
        //                }
        //                avglayer[layers[i]] += temperatures[i];
        //            }
        //            for (int i = 0; i < 40; i++)
        //            {
        //                avglayer[i] /= 48.0;
        //                SendTemperatureEvent?.Invoke(new Tuple<int, string, string, string>(i, maxlayer[i].ToString("F2", CultureInfo.InvariantCulture), avglayer[i].ToString("F2", CultureInfo.InvariantCulture), minlayer[i].ToString("F2", CultureInfo.InvariantCulture)));
        //            }
        //            await UploadAllChTemperaturetToInfluxAsync(layers, channels, temperatures, NowTime);
        //        }
        //        else
        //        {
        //            while (!stream.DataAvailable)
        //            {
        //                NowTime = DateTime.Now;
        //                DurationTime = NowTime - StartTime;
        //                if (DurationTime.TotalMilliseconds > 3000)
        //                {
        //                    //MessageBox.Show("No Rata Received");
        //                    SendMessageEvent?.Invoke("No Data Received\r\n");
        //                    break;
        //                }
        //            }
        //            if (stream.DataAvailable)
        //            {
        //                NowTime = DateTime.Now;
        //                TcpReceiveLength = stream.Read(TcpReceiveData, 0, TcpReceiveData.Length);
        //                if (TcpReceiveLength != 7680)
        //                {
        //                    SendMessageEvent?.Invoke("Warning: Received Data Length is not 7680 Bytes\r\n");
        //                    SendMessageEvent?.Invoke("Actual Received Data Length: " + TcpReceiveLength.ToString() + " Bytes\r\n");
        //                    await Task.Delay(TemperatureSendWaitTimeMs, token);
        //                    continue;
        //                }
        //                double[] temperatures = new double[40 * 48];
        //                int[] channels = new int[40 * 48];
        //                int[] layers = new int[40 * 48];
        //                double[] maxlayer = new double[40];
        //                double[] avglayer = new double[40];
        //                double[] minlayer = new double[40];
        //                for (int i = 0; i < 40; i++)
        //                {
        //                    maxlayer[i] = -100.0;
        //                    avglayer[i] = 0.0;
        //                    minlayer[i] = 200.0;
        //                }
        //                for (int i = 0; i < 40 * 48; i++)
        //                {
        //                    temperatures[i] = (TcpReceiveData[4 * i] * 256 + TcpReceiveData[4 * i + 1]) / 128.0;
        //                    channels[i] = TcpReceiveData[4 * i + 2];
        //                    layers[i] = TcpReceiveData[4 * i + 3];
        //                    if (temperatures[i] > maxlayer[layers[i]])
        //                    {
        //                        maxlayer[layers[i]] = temperatures[i];
        //                    }
        //                    if (temperatures[i] < minlayer[layers[i]])
        //                    {
        //                        minlayer[layers[i]] = temperatures[i];
        //                    }
        //                    avglayer[layers[i]] += temperatures[i];
        //                }
        //                for (int i = 0; i < 40; i++)
        //                {
        //                    avglayer[i] /= 48.0;
        //                    SendTemperatureEvent?.Invoke(new Tuple<int, string, string, string>(i, maxlayer[i].ToString("F2", CultureInfo.InvariantCulture), avglayer[i].ToString("F2", CultureInfo.InvariantCulture), minlayer[i].ToString("F2", CultureInfo.InvariantCulture)));
        //                }
        //                await UploadAllChTemperaturetToInfluxAsync(layers, channels, temperatures, NowTime);
        //            }
        //        }
        //        await Task.Delay(TemperatureSendWaitTimeMs, token);
        //    }
        //}
        public void StartTemperatureAcqSend()
        {
            TemperatureAcqSendTokens.Dispose();
            TemperatureAcqSendTokens = new CancellationTokenSource();
            SendMessageEvent?.Invoke("Temperature acquisition and send started.\r\n");
            try
            {
                Task TemperatureAcqSendTask = Task.Factory.StartNew(() => this.TemperatureAcqSendFunction(TemperatureAcqSendTokens.Token, UserClient), TemperatureAcqSendTokens.Token);
            }
            catch (AggregateException excption)
            {
                foreach (var v in excption.InnerExceptions)
                {
                    SendMessageEvent?.Invoke(excption.Message + " " + v.Message);
                    //MessageTextbox.AppendText(excption.Message + " " + v.Message);
                }
            }
        }
        public void StopTemperatureAcqSend()
        {
            TemperatureAcqSendTokens.Cancel();
            SendMessageEvent?.Invoke("Temperature acquisition and send stopped.\r\n");
        }
        public async Task StatusAcqSendFunction(CancellationToken token, TcpClient UserClient)
        {
            byte[] CmdBytes = new byte[2];
            //Status Refresh Command is 0x1300
            CmdBytes[0] = 0x13;
            CmdBytes[1] = 0x00;
            NetworkStream stream = UserClient.GetStream();
            //UserClient.ReceiveBufferSize = 40 * 4 / 8;
            UserClient.ReceiveTimeout = 1000;
            const int ExpectedBytes = 20; // 40 bits / 8 bits/byte = 5 bytes + header/footer
            var buf = new byte[ExpectedBytes];
            while (!token.IsCancellationRequested)
            {
                stream.Write(CmdBytes, 0, CmdBytes.Length);
                DateTime StartTime = DateTime.Now;
                DateTime NowTime = new DateTime();
                TimeSpan DurationTime = new TimeSpan();
                while (!stream.DataAvailable)
                {
                    NowTime = DateTime.Now;
                    DurationTime = NowTime - StartTime;
                    if (DurationTime.TotalMilliseconds > 3000)
                    {
                        //MessageBox.Show("No Rata Received");
                        SendMessageEvent?.Invoke("No Data Received\r\n");
                        break;
                    }
                }
                int read = await ReadExactAsync(stream, buf, ExpectedBytes, 1000, token);
                if (read != ExpectedBytes)
                {
                    SendMessageEvent?.Invoke($"Status frame incomplete: {read}/{ExpectedBytes} bytes\r\n");
                    await Task.Delay(StatusSendWaitTimeMs, token);
                    continue;
                }
                DateTime now = DateTime.Now;
                byte[] TcpReceiveData = buf;
                int[] status = new int[40];
                int[] layers = new int[40];
                byte[] statuss = new byte[5];
                statuss = TcpReceiveData[10..15];
                for (int i = 0; i < 40; i++)
                {
                    try
                    {
                        status[i] = GetBitFromByteArray(statuss, i);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        status[i] = -1;
                    }
                    layers[i] = i;
                    string status_str = status[i] == 1 ? "Connected" : (status[i] == 0 ? "not Connected" : "ERROR");
                    SendStatusEvent?.Invoke(new Tuple<int, string>(i, status_str));
                }
                await UploadAllChStatusToInfluxAsync(layers, status, now);
                await Task.Delay(StatusSendWaitTimeMs, token);
            }
        }

        //    DateTime NowTime = new DateTime();
        //    TimeSpan DurationTime = new TimeSpan();
        //    byte[] TcpReceiveData = new byte[UserClient.ReceiveBufferSize];
        //    while (true)
        //    {
        //        stream.Write(CmdBytes, 0, CmdBytes.Length);
        //        DateTime StartTime = DateTime.Now;
        //        int TcpReceiveLength = 0;
        //        if (token.IsCancellationRequested == true)
        //        {
        //            Thread.Sleep(100);
        //            NowTime = DateTime.Now;
        //            if (stream.DataAvailable)
        //            {
        //                NowTime = DateTime.Now;
        //                TcpReceiveLength = stream.Read(TcpReceiveData, 0, TcpReceiveData.Length);
        //            }
        //            else
        //            {
        //                TcpReceiveLength = 0;
        //                break;
        //            }

        //            if (TcpReceiveLength == 0)
        //            {
        //                break;
        //            }
        //            //MessageTextbox.AppendText("Received Data Bytes: " + TcpReceiveLength + "\r\n");
        //            int[] status = new int[40];
        //            int[] layers = new int[40];
        //            byte[] statuss = new byte[5];
        //            statuss = TcpReceiveData[10..15];
        //            for (int i = 0; i < 40; i++)
        //            {
        //                try
        //                {
        //                    status[i] = GetBitFromByteArray(statuss, i);
        //                }
        //                catch (ArgumentOutOfRangeException)
        //                {
        //                    status[i] = -1;
        //                }
        //                layers[i] = i;
        //                string status_str = status[i] == 1 ? "Connected" : (status[i] == 0 ? "not Connected" : "ERROR");
        //                SendStatusEvent?.Invoke(new Tuple<int, string>(i, status_str));
        //            }
        //            await UploadAllChStatusToInfluxAsync(layers, status, NowTime);
        //        }
        //        else
        //        {
        //            while (!stream.DataAvailable)
        //            {
        //                NowTime = DateTime.Now;
        //                DurationTime = NowTime - StartTime;
        //                if (DurationTime.TotalMilliseconds > 3000)
        //                {
        //                    //MessageBox.Show("No Rata Received");
        //                    SendMessageEvent?.Invoke("No Data Received\r\n");
        //                    break;
        //                }
        //            }
        //            if (stream.DataAvailable)
        //            {
        //                NowTime = DateTime.Now;
        //                TcpReceiveLength = stream.Read(TcpReceiveData, 0, TcpReceiveData.Length);
        //                if (TcpReceiveLength != 20)
        //                {
        //                    SendMessageEvent?.Invoke("Warning: Received Data Length is not 20 Bytes\r\n");
        //                    SendMessageEvent?.Invoke("Received Data Length: " + TcpReceiveLength + " Bytes\r\n");
        //                    await Task.Delay(StatusSendWaitTimeMs, token);
        //                    continue;
        //                }
        //                int[] status = new int[40];
        //                int[] layers = new int[40];
        //                byte[] statuss = new byte[5];
        //                statuss = TcpReceiveData[10..15];
        //                for (int i = 0; i < 40; i++)
        //                {
        //                    try
        //                    {
        //                        status[i] = GetBitFromByteArray(statuss, i);
        //                    }
        //                    catch (ArgumentOutOfRangeException)
        //                    {
        //                        status[i] = -1;
        //                    }
        //                    layers[i] = i;
        //                    string status_str = status[i] == 1 ? "Connected" : (status[i] == 0 ? "not Connected" : "ERROR");
        //                    SendStatusEvent?.Invoke(new Tuple<int, string>(i, status_str));
        //                }
        //                await UploadAllChStatusToInfluxAsync(layers, status, NowTime);
        //            }
        //        }
        //        await Task.Delay(StatusSendWaitTimeMs, token);
        //    }
        //}
        public void StartStatusAcqSend()
        {
            StatusAcqSendTokens.Dispose();
            StatusAcqSendTokens = new CancellationTokenSource();
            SendMessageEvent?.Invoke("Status acquisition and send started.\r\n");
            try
            {
                Task StatusAcqSendTask = Task.Factory.StartNew(() => this.StatusAcqSendFunction(StatusAcqSendTokens.Token, UserClient), StatusAcqSendTokens.Token);
            }
            catch (AggregateException excption)
            {
                foreach (var v in excption.InnerExceptions)
                {
                    SendMessageEvent?.Invoke(excption.Message + " " + v.Message);
                    //MessageTextbox.AppendText(excption.Message + " " + v.Message);
                }
            }
        }
        public void StopStatusAcqSend()
        {
            StatusAcqSendTokens.Cancel();
            SendMessageEvent?.Invoke("Status acquisition and send stopped.\r\n");
        }

        private void Received_Data_Size(object a)
        {
            if (SumBytes < 1024)
            {
                SendMessage("Received Data Size: " + SumBytes + " Bytes\r\n");
            }
            else if (SumBytes < 1024 * 1024)
            {
                SendMessage("Received Data Size: " + SumBytes / 1024 + "KB\r\n");
            }
            else
            {
                SendMessage("Received Data Size: " + SumBytes / 1024 / 1024 + "MB\r\n");
            }

        }

        private void SendMessage(string text)
        {
            SendMessageEvent?.Invoke(text);
        }

        private static readonly HttpClient httpClient = new HttpClient();

        // sending current data to InfluxDB

        public async Task UploadAllChCurrentToInfluxAsync(int[] channels, double[] values, DateTime? timestamp = null)
        {
            // setting up the InfluxDB
            var influxUrl = HTTPInfluxUrl;
            string measurement = "AHCALstatus";
            var lineBuilder = new StringBuilder();
            string time = timestamp.HasValue
                ? $" {((DateTimeOffset)timestamp.Value).ToUnixTimeMilliseconds()}000000"
                : "";
            for (int i = 0; i < channels.Length; i++)
            {
                string tag = $"layer={channels[i]}";
                string field = $"current={values[i].ToString(CultureInfo.InvariantCulture)}";
                // Line Protocol
                lineBuilder.Append(measurement).Append(',').Append(tag).Append(' ').Append(field).Append(time).Append('\n');
            }
            // remove the last newline
            if (lineBuilder.Length > 0)
            {
                lineBuilder.Length--;
            }
            var content = new StringContent(lineBuilder.ToString(), Encoding.UTF8);

            try
            {
                var response = await httpClient.PostAsync(influxUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    SendMessageEvent?.Invoke($"InfluxDB upload failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                }
                else
                {
                    //SendMessageEvent?.Invoke($"InfluxDB upload success: {line}");
                }
            }
            catch (Exception ex)
            {
                SendMessageEvent?.Invoke($"InfluxDB upload error: {ex.Message}");
            }
        }
        public async Task UploadAllChStatusToInfluxAsync(int[] channels, int[] values, DateTime? timestamp = null)
        {
            // setting up the InfluxDB
            var influxUrl = HTTPInfluxUrl;
            string measurement = "AHCALstatus";
            var lineBuilder = new StringBuilder();
            string time = timestamp.HasValue
                ? $" {((DateTimeOffset)timestamp.Value).ToUnixTimeMilliseconds()}000000"
                : "";
            for (int i = 0; i < channels.Length; i++)
            {
                string tag = $"layer={channels[i]}";
                string field = $"status={values[i].ToString(CultureInfo.InvariantCulture)}";
                // Line Protocol
                lineBuilder.Append(measurement).Append(',').Append(tag).Append(' ').Append(field).Append(time).Append('\n');
            }
            // remove the last newline
            if (lineBuilder.Length > 0)
            {
                lineBuilder.Length--;
            }
            var content = new StringContent(lineBuilder.ToString(), Encoding.UTF8);

            try
            {
                var response = await httpClient.PostAsync(influxUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    SendMessageEvent?.Invoke($"InfluxDB upload failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                }
                else
                {
                    //SendMessageEvent?.Invoke($"InfluxDB upload success: {line}");
                }
            }
            catch (Exception ex)
            {
                SendMessageEvent?.Invoke($"InfluxDB upload error: {ex.Message}");
            }
        }

        public async Task UploadAllChTemperaturetToInfluxAsync(int[] layers, int[] channels, double[] values, DateTime? timestamp = null)
        {
            // setting up the InfluxDB
            var influxUrl = HTTPInfluxUrl;
            string measurement = "AHCALTemperature";
            var lineBuilder = new StringBuilder();
            string time = timestamp.HasValue
                ? $" {((DateTimeOffset)timestamp.Value).ToUnixTimeMilliseconds()}000000"
                : "";
            for (int i = 0; i < channels.Length; i++)
            {
                string tag = $"layer={layers[i]},channel={channels[i]}";
                string field = $"value={values[i].ToString(CultureInfo.InvariantCulture)}";
                // Line Protocol
                lineBuilder.Append(measurement).Append(',').Append(tag).Append(' ').Append(field).Append(time).Append('\n');
            }
            // remove the last newline
            if (lineBuilder.Length > 0)
            {
                lineBuilder.Length--;
            }
            var content = new StringContent(lineBuilder.ToString(), Encoding.UTF8);

            try
            {
                var response = await httpClient.PostAsync(influxUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    SendMessageEvent?.Invoke($"InfluxDB upload failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                }
                else
                {
                    //SendMessageEvent?.Invoke($"InfluxDB upload success: {line}");
                }
            }
            catch (Exception ex)
            {
                SendMessageEvent?.Invoke($"InfluxDB upload error: {ex.Message}");
            }
        }
        public async Task<bool> CheckInfluxConnectionAsync()
        {
            try
            {
                var config = new ConfigurationBuilder().SetBasePath(System.AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true, reloadOnChange: true).Build();
                var pingUrl = config.GetSection("Settings:HTTPInfluxUrl").Value ?? "http://172.27.132.8:8086";
                pingUrl = ReplaceEnvironmentVariables(pingUrl);
                if (pingUrl.Contains("write"))
                {
                    pingUrl = Regex.Replace(pingUrl, "/write\\?db=[^&]+", "/ping");
                }
                else
                {
                    if (pingUrl.EndsWith("/"))
                    {
                        pingUrl += "ping";
                    }
                    else
                    {
                        pingUrl += "/ping";
                    }
                }
                SendMessageEvent?.Invoke($"Pinging InfluxDB at {pingUrl}...\r\n");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var response = await httpClient.GetAsync(pingUrl, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    SendMessageEvent?.Invoke("InfluxDB ping success.\r\n");
                    return true;
                }
                else
                {
                    SendMessageEvent?.Invoke($"InfluxDB ping failed: {response.StatusCode}\r\n" +
                        $"{response.ReasonPhrase}\r\n");
                    return false;
                }
            }
            catch (Exception ex)
            {
                SendMessageEvent?.Invoke($"InfluxDB ping error: {ex.Message}\r\n");
                var inner = ex.InnerException;
                if (inner != null)
                {
                    SendMessageEvent?.Invoke($"Error: {ex.Message}\nInner: {inner.Message}\nStackTrace: {inner.StackTrace}");
                }
                else
                {
                    SendMessageEvent?.Invoke($"Error: {ex.Message}\nStackTrace: {ex.StackTrace}");
                }
                return false;
            }
        }
        public async Task InfluxTestAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int[] testLayers = new int[40];
                double[] testdata = new double[40];
                DateTime timestamp = DateTime.Now; // timestamp
                for (int i = 0; i < 40; i++)
                {
                    double testValue = i * 0.1; // test value
                    testLayers[i] = i;
                    testdata[i] = (testValue * 1000); // convert to mA
                }
                await UploadAllChCurrentToInfluxAsync(testLayers, testdata, DateTime.Now);
                await Task.Delay(1000, token); // sending with 1s interval
            }
        }
        public void InfluxTestStart()
        {
            var ok = CheckInfluxConnectionAsync().GetAwaiter().GetResult();
            if (!ok)
            {
                SendMessageEvent?.Invoke("InfluxDB connection is not established. Please check the configuration.\r\n");
                return;
            }

            InfluxTestTokens.Dispose();
            InfluxTestTokens = new CancellationTokenSource();
            SendMessageEvent?.Invoke("InfluxDB test started.\r\n");
            Task.Run(() => InfluxTestAsync(InfluxTestTokens.Token));
        }

        public void InfluxTestStop()
        {
            // stop the sending task
            SendMessageEvent?.Invoke("InfluxDB test stopped.\r\n");
            InfluxTestTokens.Cancel();
        }

        public async Task AllAcqSendFunction(CancellationToken token, TcpClient UserClient)
        {
            //var stream = UserClient.GetStream();
            byte[] cmdStatus = new byte[2] { 0x13, 0x00 }; // Status command
            byte[] cmdCurrent = new byte[2] { 0x12, 0x00 }; // Current command
            byte[] cmdTemp = new byte[2] { 0x11, 0x00 }; // Temperature command

            const int expectedStatusBytes = 20;
            const int expectedCurrentBytes = 192; // 48 channels * 4 bytes
            const int expectedTempBytes = 7680; // 40 layers * 48 channels * 4 bytes

            var bufStatus = new byte[expectedStatusBytes];
            var bufCurrent = new byte[expectedCurrentBytes];
            var bufTemp = new byte[expectedTempBytes];

            DateTime nextStatusAt = DateTime.UtcNow;
            DateTime nextCurrentAt = DateTime.UtcNow;
            DateTime nextTempAt = DateTime.UtcNow;

            while (!token.IsCancellationRequested)
            {
                if (!IsClientAlive(UserClient))
                {
                    bool ok = await WaitReconnectAsync(token);
                    if (!ok) break; 
                }

                NetworkStream? stream = null;
                try
                {
                    stream = UserClient.GetStream();
                }
                catch (Exception ex)
                {
                    SendMessageEvent?.Invoke("GetStream failed: " + ex.Message + "\r\n");
                    await Task.Delay(1000, token);
                    continue;
                }
                bool didAny = false;
                DateTime now = DateTime.UtcNow;
                try
                {
                    if (now >= nextStatusAt)
                    {
                        stream.Write(cmdStatus, 0, cmdStatus.Length);
                        int read = await ReadExactAsync(stream, bufStatus, expectedStatusBytes, 3000, token);
                        if (read == expectedStatusBytes)
                        {
                            int[] status = new int[40];
                            int[] layers = new int[40];
                            byte[] statuss = bufStatus[10..15];
                            for (int i = 0; i < 40; i++)
                            {
                                try { status[i] = GetBitFromByteArray(statuss, i); }
                                catch { status[i] = -1; }
                                layers[i] = i;
                                string status_str = status[i] == 1 ? "Connected" : (status[i] == 0 ? "not Connected" : "ERROR");
                                SendStatusEvent?.Invoke(new Tuple<int, string>(i, status_str));
                            }
                            await UploadAllChStatusToInfluxAsync(layers, status, DateTime.Now);
                        }
                        else
                        {
                            SendMessageEvent?.Invoke($"Status frame incomplete: {read}/{expectedStatusBytes} bytes\r\n");
                        }
                        nextStatusAt = now.AddMilliseconds(StatusSendWaitTimeMs);
                        didAny = true;
                    }
                    if (now >= nextCurrentAt)
                    {
                        stream.Write(cmdCurrent, 0, cmdCurrent.Length);
                        int read = await ReadExactAsync(stream, bufCurrent, expectedCurrentBytes, 3000, token);
                        if (read == expectedCurrentBytes)
                        {
                            int[] dec_word = new int[48];
                            for (int i = 0; i < 48; i++)
                                dec_word[i] = (bufCurrent[4 * i] << 24) + (bufCurrent[4 * i + 1] << 16) + (bufCurrent[4 * i + 2] << 8) + bufCurrent[4 * i + 3];

                            int[] channels = new int[40];
                            double[] values = new double[40];
                            for (int i = 0; i < 40; i++)
                            {
                                channels[i] = i;
                                double current_value = DecodeCurrent(dec_word, i);
                                values[i] = current_value;
                                SendCurrentEvent?.Invoke(new Tuple<int, string>(i, current_value.ToString(CultureInfo.InvariantCulture)));
                            }
                            await UploadAllChCurrentToInfluxAsync(channels, values, DateTime.Now);
                        }
                        else
                        {
                            SendMessageEvent?.Invoke($"Current frame incomplete: {read}/{expectedCurrentBytes} bytes\r\n");
                        }
                        nextCurrentAt = now.AddMilliseconds(CurrentSendWaitTimeMs);
                        didAny = true;
                    }
                    if (now >= nextTempAt)
                    {
                        stream.Write(cmdTemp, 0, cmdTemp.Length);
                        int read = await ReadExactAsync(stream, bufTemp, expectedTempBytes, 3000, token);
                        if (read == expectedTempBytes)
                        {
                            double[] temperatures = new double[40 * 48];
                            int[] channels = new int[40 * 48];
                            int[] layers = new int[40 * 48];
                            double[] maxlayer = new double[40];
                            double[] avglayer = new double[40];
                            double[] minlayer = new double[40];
                            for (int i = 0; i < 40; i++)
                            {
                                maxlayer[i] = -100.0;
                                avglayer[i] = 0.0;
                                minlayer[i] = 200.0;
                            }
                            for (int i = 0; i < 40 * 48; i++)
                            {
                                temperatures[i] = (bufTemp[4 * i] * 256 + bufTemp[4 * i + 1]) / 128.0;
                                channels[i] = bufTemp[4 * i + 2];
                                layers[i] = bufTemp[4 * i + 3];
                                if (temperatures[i] > maxlayer[layers[i]]) maxlayer[layers[i]] = temperatures[i];
                                if (temperatures[i] < minlayer[layers[i]]) minlayer[layers[i]] = temperatures[i];
                                avglayer[layers[i]] += temperatures[i];
                            }
                            for (int i = 0; i < 40; i++)
                            {
                                avglayer[i] /= 48.0;
                                SendTemperatureEvent?.Invoke(new Tuple<int, string, string, string>(i, maxlayer[i].ToString("F2", CultureInfo.InvariantCulture), avglayer[i].ToString("F2", CultureInfo.InvariantCulture), minlayer[i].ToString("F2", CultureInfo.InvariantCulture)));
                            }
                            await UploadAllChTemperaturetToInfluxAsync(layers, channels, temperatures, DateTime.Now);
                        }
                        else
                        {
                            SendMessageEvent?.Invoke($"Temperature frame incomplete: {read}/{expectedTempBytes} bytes\r\n");
                        }
                        nextTempAt = now.AddMilliseconds(TemperatureSendWaitTimeMs);
                        didAny = true;
                    }
                }
                catch (IOException ioex)
                {
                    SendMessageEvent?.Invoke("IO exception, will try reconnect: " + ioex.Message + "\r\n");
                    await WaitReconnectAsync(token);
                }
                catch (SocketException sex)
                {
                    SendMessageEvent?.Invoke("Socket exception, will try reconnect: " + sex.Message + "\r\n");
                    await WaitReconnectAsync(token);
                }
                catch (ObjectDisposedException)
                {
                    if (token.IsCancellationRequested) break;
                    SendMessageEvent?.Invoke("Stream disposed, reconnecting...\r\n");
                    await WaitReconnectAsync(token);
                }
                catch (Exception ex)
                {
                    SendMessageEvent?.Invoke("Unexpected error: " + ex.Message + "\r\n");
                    await Task.Delay(1000, token);
                }

                if (!didAny)
                {
                    var next = new[] { nextStatusAt, nextCurrentAt, nextTempAt }.Min();
                    var delay = next - now;
                    if (delay < TimeSpan.FromMilliseconds(5)) delay = TimeSpan.FromMilliseconds(5);
                    try
                    {
                        await Task.Delay(delay, token);
                    }
                    catch { }
                }
            }
        }
        public void StartAllAcqSend()
        {
            AllAcqSendTokens.Dispose();
            AllAcqSendTokens = new CancellationTokenSource();
            SendMessageEvent?.Invoke("All acquisition and send started.\r\n");
            try
            {
                Task AllAcqSendTask = Task.Factory.StartNew(() => this.AllAcqSendFunction(AllAcqSendTokens.Token, UserClient), AllAcqSendTokens.Token);
            }
            catch (AggregateException excption)
            {
                foreach (var v in excption.InnerExceptions)
                {
                    SendMessageEvent?.Invoke(excption.Message + " " + v.Message);
                    //MessageTextbox.AppendText(excption.Message + " " + v.Message);
                }
            }
        }
        public void StopAllAcqSend()
        {
            AllAcqSendTokens.Cancel();
            SendMessageEvent?.Invoke("All acquisition and send stopped.\r\n");
        }
    }
}





