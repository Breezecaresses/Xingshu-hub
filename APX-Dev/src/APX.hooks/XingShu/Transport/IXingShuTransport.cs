using System;
using System.IO;

#pragma warning disable 0436
namespace System.IO
{
    /// <summary>
    /// 数据格式无效或数据帧超出安全限制异常 (兼容 Unity BCL 环境)
    /// </summary>
    public class InvalidDataException : SystemException
    {
        public InvalidDataException() : base("Invalid data") { }
        public InvalidDataException(string message) : base(message) { }
        public InvalidDataException(string message, Exception innerException) : base(message, innerException) { }
    }
}
#pragma warning restore 0436


namespace XingShu.Transport
{
    /// <summary>
    /// 星枢通讯层抽象传输接口 (Transport Abstraction)
    /// </summary>
    public interface IXingShuTransport : IDisposable
    {
        /// <summary>
        /// 当前底层传输通道是否已处于连接就绪状态
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 连接至指定端点
        /// </summary>
        /// <param name="target">连接目标端点 (例如 "127.0.0.1:25890")</param>
        /// <param name="timeoutMs">连接超时时间 (毫秒)</param>
        void Connect(string target, int timeoutMs);

        /// <summary>
        /// 写入一帧数据
        /// </summary>
        /// <param name="frame">数据缓冲区</param>
        /// <param name="offset">起始偏移</param>
        /// <param name="count">写入字节数</param>
        void WriteFrame(byte[] frame, int offset, int count);

        /// <summary>
        /// 读取以 '\n' 分隔的单行文本帧 (UTF-8 编码，自动去除行尾 '\r')
        /// </summary>
        /// <param name="timeoutMs">读取超时时间 (毫秒)，<= 0 表示无限等待</param>
        /// <returns>读取到的行文本；若到达流尾 (clean EOF) 则返回 null</returns>
        string ReadLine(int timeoutMs);

        /// <summary>
        /// 阻塞读取以 '\n' 分隔的单行文本帧 (无限等待真实数据、流尾 EOF 或主动断开)
        /// </summary>
        /// <returns>读取到的行文本；若到达流尾 (clean EOF) 则返回 null</returns>
        string ReadLine();

        /// <summary>
        /// 断开当前传输连接并释放套接字流
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 释放传输对象及所有底层资源
        /// </summary>
        new void Dispose();
    }
}
