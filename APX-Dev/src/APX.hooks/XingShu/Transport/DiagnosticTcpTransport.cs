#if XINGSHU_SPECTATOR_PROBE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using XingShu.Bridge;

namespace XingShu.Transport
{
    /// <summary>
    /// Lane 2: Diagnostic Managed Loopback TCP Client (Project XingShu HS3.2.1)
    /// 
    /// Purpose:
    /// Validates managed BCL TCP Socket capability under HybridCLR / Unity runtime without ANY native Win32 P/Invoke stubs.
    /// Strictly guarded by compile constant XINGSHU_SPECTATOR_PROBE.
    /// In Release configuration, zero code or symbols from this class are compiled.
    /// 
    /// Target: 127.0.0.1:25890 (Fixed test port)
    /// Protocol: HELLO / HELLO_ACK handshake only (no spectator snapshots in this round).
    /// </summary>
    public static class DiagnosticTcpTransport
    {
        private const string TargetHost = "127.0.0.1";
        private const int TargetPort = 25890;

        private static readonly object _syncLock = new object();
        private static readonly object _fileLock = new object();
        private static Thread _workerThread;
        private static volatile bool _isRunning;
        private static TcpClient _currentClient;

        public static bool IsRunning => _isRunning;

        private static string LogFilePath
        {
            get
            {
                try
                {
                    return Path.Combine(Application.dataPath, "../xingshu_tcp_live_poc.log");
                }
                catch
                {
                    return "xingshu_tcp_live_poc.log";
                }
            }
        }

        public static void StartAsync()
        {
            lock (_syncLock)
            {
                if (_isRunning) return;
                _isRunning = true;
                _workerThread = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "XingShu-Diagnostic-TCP"
                };
                _workerThread.Start();
            }
        }

        public static void Stop()
        {
            lock (_syncLock)
            {
                _isRunning = false;
                try
                {
                    if (_currentClient != null)
                    {
                        _currentClient.Dispose();
                        _currentClient = null;
                    }
                }
                catch { }
            }
        }

        private static void EnsureLogDirectory(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch { }
        }

        private static void LogMessage(string message)
        {
            try
            {
                Debug.Log(message);
            }
            catch { }

            try
            {
                string path = LogFilePath;
                EnsureLogDirectory(path);
                lock (_fileLock)
                {
                    File.AppendAllText(path, message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    Debug.LogWarning($"[DiagnosticTcpTransport] File log write error: {ex.Message}");
                }
                catch { }
            }
        }

        private static void LogFailure(string step, Exception ex)
        {
            string failMsg = $"[TCP_POC_FAILED] Step {step}: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}";
            try
            {
                Debug.Log(failMsg);
            }
            catch { }

            try
            {
                string path = LogFilePath;
                EnsureLogDirectory(path);
                lock (_fileLock)
                {
                    File.AppendAllText(path, failMsg + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        private static void WorkerLoop()
        {
            const int maxRetries = 10;
            int retryCount = 0;

            while (_isRunning)
            {
                string currentStep = "1_CTOR";
                TcpClient client = null;
                NetworkStream stream = null;

                try
                {
                    // Step 1: new TcpClient()
                    currentStep = "1_CTOR";
                    LogMessage("[TCP_POC_STEP_1_CTOR] new TcpClient()");
                    client = new TcpClient();
                    if (client.Client != null)
                    {
                        client.Client.NoDelay = true;
                        client.Client.SendTimeout = 5000;
                        client.Client.ReceiveTimeout = 5000;
                    }

                    // Step 2: client.Connect("127.0.0.1", 25890)
                    currentStep = "2_CONNECT";
                    LogMessage("[TCP_POC_STEP_2_CONNECT] client.Connect(\"127.0.0.1\", 25890)");
                    client.Connect(TargetHost, TargetPort);

                    // Step 3: client.GetStream()
                    currentStep = "3_STREAM";
                    LogMessage("[TCP_POC_STEP_3_STREAM] client.GetStream()");
                    stream = client.GetStream();
                    if (stream.CanTimeout)
                    {
                        stream.ReadTimeout = 5000;
                        stream.WriteTimeout = 5000;
                    }

                    // Step 4: Write HELLO JSON + \n to stream
                    currentStep = "4_WRITE_HELLO";
                    LogMessage("[TCP_POC_STEP_4_WRITE_HELLO] Write HELLO JSON + \\n to stream");
                    int pid = 0;
                    try { pid = Process.GetCurrentProcess().Id; } catch { }
                    string gv = "1.3.8";
                    try { if (!string.IsNullOrEmpty(Application.version)) gv = Application.version; } catch { }

                    var helloPayload = new HelloPayload
                    {
                        component = "XingShu_Diagnostic_TCP",
                        runtimeVersion = "HS3.2.1-TCP-POC",
                        gamePid = pid,
                        gameVersion = gv,
                        capabilities = new List<string> { "status", "events", "SPECTATOR", "TCP_POC" }
                    };
                    var helloEnv = BridgeEnvelope.Create(BridgeProtocol.TypeHello, helloPayload);
                    string helloJson = BridgeEnvelope.Serialize(helloEnv);
                    byte[] helloBytes = Encoding.UTF8.GetBytes(helloJson + "\n");
                    stream.Write(helloBytes, 0, helloBytes.Length);
                    stream.Flush();

                    // Step 5: Read line from stream
                    currentStep = "5_READ_HELLO_ACK";
                    LogMessage("[TCP_POC_STEP_5_READ_HELLO_ACK] Read line from stream");
                    var reader = new StreamReader(stream, Encoding.UTF8);
                    string ackLine = reader.ReadLine();
                    if (ackLine == null)
                    {
                        throw new IOException("Stream returned EOF while reading HELLO_ACK response.");
                    }

                    // Step 6: Validate HELLO_ACK status == "ACCEPTED"
                    currentStep = "6_VERIFIED";
                    LogMessage("[TCP_POC_STEP_6_VERIFIED] Validate HELLO_ACK status == \"ACCEPTED\"");
                    BridgeEnvelope ackEnv = BridgeJson.DeserializeEnvelope(ackLine);
                    if (ackEnv == null)
                    {
                        throw new InvalidOperationException("Failed to deserialize HELLO_ACK response into BridgeEnvelope.");
                    }
                    if (!string.Equals(ackEnv.messageType, BridgeProtocol.TypeHelloAck, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Unexpected messageType in response: expected '{BridgeProtocol.TypeHelloAck}', got '{ackEnv.messageType}'.");
                    }

                    string ackStatus = null;
                    if (ackEnv.payload is Dictionary<string, object> dict && dict.TryGetValue("status", out var s) && s != null)
                    {
                        ackStatus = s.ToString();
                    }
                    else if (ackEnv.payload is HelloAckPayload helloAck)
                    {
                        ackStatus = helloAck.status;
                    }

                    if (!string.Equals(ackStatus, "ACCEPTED", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"HELLO_ACK validation failed: status expected 'ACCEPTED', got '{ackStatus}'.");
                    }

                    lock (_syncLock)
                    {
                        _currentClient = client;
                    }

                    // Keep alive until component teardown / Stop()
                    while (_isRunning && client.Client != null && client.Client.Connected)
                    {
                        Thread.Sleep(200);
                    }
                    break;
                }
                catch (SocketException ex) when (currentStep == "2_CONNECT" && retryCount < maxRetries && _isRunning)
                {
                    retryCount++;
                    LogMessage($"[TCP_POC_STEP_2_CONNECT] Connection attempt {retryCount}/{maxRetries} failed: {ex.Message}. Retrying in 500ms...");
                    try { stream?.Dispose(); } catch { }
                    try { client?.Dispose(); } catch { }
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    LogFailure(currentStep, ex);
                    try { stream?.Dispose(); } catch { }
                    try { client?.Dispose(); } catch { }
                    break;
                }
            }
        }
    }
}
#endif
