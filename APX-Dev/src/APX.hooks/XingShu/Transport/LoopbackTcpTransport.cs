using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace XingShu.Transport
{
    /// <summary>
    /// 星枢生产级环回 TCP 传输实现 (Loopback TCP Transport)
    /// 完全移除 Win32 P/Invoke 依赖，基于托管 BCL TcpClient 与 NetworkStream 实现。
    /// 遵循 64KiB 帧安全守卫，支持分包组包、单缓冲区多帧解析、EOF 检测与超时控制。
    /// </summary>
    public sealed class LoopbackTcpTransport : IXingShuTransport
    {
        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 25890;
        public const int MaxFrameBytes = 64 * 1024; // 65536 字节严格安全边界

        private readonly string _defaultHost;
        private readonly int _defaultPort;

        private TcpClient _tcpClient;
        private NetworkStream _stream;

        private readonly object _stateLock = new object();
        private readonly object _writeLock = new object();
        private readonly object _readLock = new object();

        private readonly byte[] _readBuffer = new byte[8192];
        private int _readPos;
        private int _readCount;
        private readonly MemoryStream _lineBuffer = new MemoryStream();

        public string Host => _defaultHost;
        public int Port => _defaultPort;

        public bool IsConnected
        {
            get
            {
                lock (_stateLock)
                {
                    if (_tcpClient == null) return false;
                    try
                    {
                        var socket = _tcpClient.Client;
                        return socket != null && socket.Connected;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        public LoopbackTcpTransport() : this(DefaultHost, DefaultPort)
        {
        }

        public LoopbackTcpTransport(string host, int port)
        {
            _defaultHost = string.IsNullOrEmpty(host) ? DefaultHost : host;
            _defaultPort = port > 0 ? port : DefaultPort;
        }

        private void ParseTarget(string target, out string host, out int port)
        {
            host = _defaultHost;
            port = _defaultPort;

            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            int colonIdx = target.LastIndexOf(':');
            if (colonIdx >= 0)
            {
                string hostPart = target.Substring(0, colonIdx).Trim();
                string portPart = target.Substring(colonIdx + 1).Trim();
                if (!string.IsNullOrEmpty(hostPart))
                {
                    host = hostPart;
                }
                int p;
                if (int.TryParse(portPart, out p))
                {
                    port = p;
                }
            }
            else
            {
                int p;
                if (int.TryParse(target.Trim(), out p))
                {
                    port = p;
                }
                else
                {
                    host = target.Trim();
                }
            }
        }

        /// <summary>
        /// 连接至目标环回 TCP 服务端
        /// </summary>
        public void Connect(string target, int timeoutMs)
        {
            Disconnect();

            string host;
            int port;
            ParseTarget(target, out host, out port);

            var client = new TcpClient();
            try
            {
                if (client.Client != null)
                {
                    client.Client.NoDelay = true;
                    client.Client.LingerState = new LingerOption(true, 0);
                }

                int connectTimeout = timeoutMs > 0 ? timeoutMs : 5000;

                IAsyncResult ar = client.BeginConnect(host, port, null, null);
                bool success = ar.AsyncWaitHandle.WaitOne(connectTimeout, false);
                if (!success)
                {
                    try { client.Dispose(); } catch { }
                    throw new TimeoutException(string.Format("Connection to {0}:{1} timed out after {2} ms.", host, port, connectTimeout));
                }

                client.EndConnect(ar);

                if (client.Client != null)
                {
                    client.Client.NoDelay = true;
                    client.Client.LingerState = new LingerOption(true, 0);
                }

                NetworkStream stream = client.GetStream();

                lock (_stateLock)
                {
                    _tcpClient = client;
                    _stream = stream;
                }

                lock (_readLock)
                {
                    _readPos = 0;
                    _readCount = 0;
                    _lineBuffer.SetLength(0);
                }
            }
            catch
            {
                try { client.Dispose(); } catch { }
                throw;
            }
        }

        /// <summary>
        /// 写入一帧数据
        /// </summary>
        public void WriteFrame(byte[] frame, int offset, int count)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            if (offset < 0 || count < 0 || offset + count > frame.Length)
                throw new ArgumentOutOfRangeException();

            NetworkStream stream;
            lock (_stateLock)
            {
                stream = _stream;
                if (stream == null || !IsConnected)
                {
                    throw new IOException("Transport is not connected.");
                }
            }

            lock (_writeLock)
            {
                stream.Write(frame, offset, count);
                stream.Flush();
            }
        }

        /// <summary>
        /// 便捷写入整帧数据
        /// </summary>
        public void WriteFrame(byte[] frame)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            WriteFrame(frame, 0, frame.Length);
        }

        /// <summary>
        /// 读取以 '\n' 分隔的单行 UTF-8 文本帧
        /// </summary>
        public string ReadLine(int timeoutMs)
        {
            lock (_readLock)
            {
                NetworkStream stream;
                lock (_stateLock)
                {
                    stream = _stream;
                    if (stream == null || !IsConnected)
                    {
                        throw new IOException("Transport is not connected.");
                    }
                }

                if (stream.CanTimeout)
                {
                    stream.ReadTimeout = timeoutMs > 0 ? timeoutMs : -1;
                }

                while (true)
                {
                    if (_readPos >= _readCount)
                    {
                        _readPos = 0;
                        _readCount = 0;

                        int bytesRead;
                        try
                        {
                            bytesRead = stream.Read(_readBuffer, 0, _readBuffer.Length);
                        }
                        catch (ObjectDisposedException)
                        {
                            // 套接字或流已被显式关闭/释放 (例如 Stop / Disconnect 调用)
                            return null;
                        }
                        catch (IOException ioEx)
                        {
                            var sockEx = ioEx.InnerException as SocketException;
                            if (sockEx != null && (sockEx.SocketErrorCode == SocketError.TimedOut || sockEx.NativeErrorCode == 10060))
                            {
                                throw new TimeoutException("The read operation timed out.");
                            }
                            if (sockEx != null && (sockEx.SocketErrorCode == SocketError.ConnectionAborted || sockEx.SocketErrorCode == SocketError.OperationAborted || sockEx.SocketErrorCode == SocketError.Interrupted))
                            {
                                return null;
                            }
                            throw;
                        }
                        catch (SocketException sockEx)
                        {
                            if (sockEx.SocketErrorCode == SocketError.TimedOut || sockEx.NativeErrorCode == 10060)
                            {
                                throw new TimeoutException("The read operation timed out.");
                            }
                            if (sockEx.SocketErrorCode == SocketError.ConnectionAborted || sockEx.SocketErrorCode == SocketError.OperationAborted || sockEx.SocketErrorCode == SocketError.Interrupted)
                            {
                                return null;
                            }
                            throw;
                        }

                        if (bytesRead <= 0)
                        {
                            // 净 EOF
                            if (_lineBuffer.Length == 0)
                            {
                                return null;
                            }
                            break;
                        }
                        _readCount = bytesRead;
                    }

                    int nlIndex = -1;
                    for (int i = _readPos; i < _readCount; i++)
                    {
                        if (_readBuffer[i] == (byte)'\n')
                        {
                            nlIndex = i;
                            break;
                        }
                    }

                    if (nlIndex >= 0)
                    {
                        int chunkLen = nlIndex - _readPos;
                        if (_lineBuffer.Length + chunkLen > MaxFrameBytes)
                        {
                            _lineBuffer.SetLength(0);
                            throw new InvalidDataException("Frame exceeds 64KB limit");
                        }

                        _lineBuffer.Write(_readBuffer, _readPos, chunkLen);
                        _readPos = nlIndex + 1;

                        byte[] rawBytes = _lineBuffer.ToArray();
                        _lineBuffer.SetLength(0);

                        int len = rawBytes.Length;
                        if (len > 0 && rawBytes[len - 1] == (byte)'\r')
                        {
                            len--;
                        }

                        return Encoding.UTF8.GetString(rawBytes, 0, len);
                    }
                    else
                    {
                        int chunkLen = _readCount - _readPos;
                        if (_lineBuffer.Length + chunkLen > MaxFrameBytes)
                        {
                            _lineBuffer.SetLength(0);
                            throw new InvalidDataException("Frame exceeds 64KB limit");
                        }

                        _lineBuffer.Write(_readBuffer, _readPos, chunkLen);
                        _readPos = _readCount;
                    }
                }

                if (_lineBuffer.Length == 0)
                {
                    return null;
                }

                byte[] eofBytes = _lineBuffer.ToArray();
                _lineBuffer.SetLength(0);

                int eofLen = eofBytes.Length;
                if (eofLen > 0 && eofBytes[eofLen - 1] == (byte)'\r')
                {
                    eofLen--;
                }

                return Encoding.UTF8.GetString(eofBytes, 0, eofLen);
            }
        }

        /// <summary>
        /// 阻塞读取单行 (无限等待真实数据、流尾 EOF 或主动断开)
        /// </summary>
        public string ReadLine()
        {
            return ReadLine(-1);
        }

        /// <summary>
        /// 断开当前连接并重置所有缓冲区状态
        /// </summary>
        public void Disconnect()
        {
            lock (_stateLock)
            {
                if (_stream != null)
                {
                    try { _stream.Close(); } catch { }
                    try { _stream.Dispose(); } catch { }
                    _stream = null;
                }

                if (_tcpClient != null)
                {
                    try { if (_tcpClient.Client != null) _tcpClient.Client.Close(); } catch { }
                    try { ((IDisposable)_tcpClient).Dispose(); } catch { }
                    _tcpClient = null;
                }
            }

            lock (_readLock)
            {
                _readPos = 0;
                _readCount = 0;
                _lineBuffer.SetLength(0);
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
