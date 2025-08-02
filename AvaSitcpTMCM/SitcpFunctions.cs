using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvaSitcpTMCM
{
    internal class SitcpFunctions
    {
        private TcpClient ServerClient = new TcpClient();
        private TcpClient UserClient = new TcpClient();
        private CancellationTokenSource DataAcqTokens = new CancellationTokenSource();
        //     private CancellationTokenSource TcpConnectTokens = new CancellationTokenSource();
        private CancellationTokenSource TemperatureMonitoringTokens = new CancellationTokenSource();
        private CancellationTokenSource TcpReceiveTokens = new CancellationTokenSource();
        private CancellationTokenSource CheckerTokens = new CancellationTokenSource();
        private CancellationTokenSource BwWriteTokens = new CancellationTokenSource();
        private CancellationTokenSource DataAcqMultiThreadTokens = new CancellationTokenSource();
        private CancellationTokenSource AutoAcqTokens = new CancellationTokenSource();
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

        private string WriteFileFolderDic;
        private string CaliFileFolderDic;
        private string FileName;
        private StringBuilder exceptionReport = new StringBuilder();
        int SumBytes = 0;
        delegate void SetTextCallback(string text);
        public event Action<string>? SendMessageEvent;
        public event Action<Tuple<int, string>>? SendCurrentEvent;
        public bool IsSendEnabled { get; set; } = false;

        public event Action<bool>? IsStartTCPReadFromUserToFileEnabled;
        public event Action<bool>? IsStopTCPReadFromUserToFileEnabled;
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
        public void UserConnect(string ip, int port)
        {
            try
            {
                if (UserClient.Connected)
                {
                    UserClient.Close();
                }
                UserClient.Connect(ip, port);
                SendMessageEvent?.Invoke("IP: " + ip + " Port: " + port + " connected successfully\r\n");
            }
            catch (Exception ex)
            {
                SendMessageEvent?.Invoke("Failed to connect to " + ip + ":" + port + ". Error: " + ex.Message + "\r\n");
            }
        }

        public void ServerConnect(string ip, int port)
        {
            try
            {
                if (ServerClient.Connected)
                {
                    ServerClient.Close();
                }
                ServerClient.Connect(ip, port);
                SendMessageEvent?.Invoke("IP: " + ip + " Port: " + port + " connected successfully\r\n");
            }
            catch (Exception ex)
            {
                SendMessageEvent?.Invoke("Failed to connect to " + ip + ":" + port + ". Error: " + ex.Message + "\r\n");
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
                        CmdBytes[i] = hexBytes[hexBytes.Length - 1 - i];
                    }
                    NetworkStream stream = UserClient.GetStream();
                    stream.Write(CmdBytes, 0, CmdBytes.Length);
                    SendMessageEvent?.Invoke(HexString + " sent successfully\r\n");
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
            //Current_Refresh Command is 0x1100
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

        public void Current_Refresh()
        {
            byte[] CmdBytes = new byte[2];
            //Current_Refresh Command is 0x1200
            CmdBytes[0] = 0x12;
            CmdBytes[1] = 0x00;
            NetworkStream stream = UserClient.GetStream();
            stream.Write(CmdBytes, 0, CmdBytes.Length);
            UserClient.ReceiveBufferSize = 192;//48*4
            UserClient.ReceiveTimeout = 1000;
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
            if (stream.DataAvailable)
            {
                byte[] TcpReceiveData = new byte[UserClient.ReceiveBufferSize];
                int TcpReceiveLength = stream.Read(TcpReceiveData, 0, TcpReceiveData.Length);
                //MessageTextbox.AppendText("Received Data Bytes: " + TcpReceiveLength + "\r\n");
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
            }
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



    }
}