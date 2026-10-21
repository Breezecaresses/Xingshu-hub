using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using XingShuRuntime.Core;

namespace XingShuRuntime.DataService;

/// <summary>
/// 本地轻量只读 HTTP 数据服务。
/// 严格规则：
/// 1. 仅监听 127.0.0.1:32100，禁止 0.0.0.0，禁止公网开放。
/// 2. 基于纯 TCP Socket 实现，彻底杜绝 Windows http.sys / URL ACL 权限异常，100% 兼容 Unity/Mono 运行环境。
/// 3. 运行于独立后台线程，纯读不可变 MatchDataSnapshot，绝对不触碰 Unity 引擎对象。
/// 4. 仅支持 GET，其余返回 405；未匹配路由返回 404。
/// 5. 卸载或销毁时立即释放监听套接字与端口。
/// </summary>
public sealed class LocalDataHttpServer : IDisposable
{
	private const int Port = 32100;
	private const string ServerUrl = "http://127.0.0.1:32100";

	private readonly Func<MatchDataSnapshot> _snapshotProvider;
	private Socket _listenerSocket;
	private Thread _workerThread;
	private volatile bool _isRunning;
	private bool _disposed;

	public bool IsRunning => _isRunning;

	public LocalDataHttpServer(Func<MatchDataSnapshot> snapshotProvider)
	{
		_snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
	}

	public bool Start()
	{
		if (_isRunning)
		{
			return true;
		}

		try
		{
			_listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			_listenerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
			_listenerSocket.Bind(new IPEndPoint(IPAddress.Loopback, Port));
			_listenerSocket.Listen(16);

			_isRunning = true;
			_workerThread = new Thread(WorkerLoop)
			{
				IsBackground = true,
				Name = "XINGSHU_DataService_SocketServer"
			};
			_workerThread.Start();

			RuntimeCompatibilityHost.LogLifecycle($"[DataService] HTTP server started successfully on {ServerUrl}/");
			return true;
		}
		catch (SocketException sex)
		{
			RuntimeCompatibilityHost.LogLifecycle($"[DataService] HTTP listener failed\nHost: 127.0.0.1\nPort: {Port}\nErrorCode: {sex.ErrorCode}\nReason: {sex.Message}");
			_isRunning = false;
			CleanupSocket();
			return false;
		}
		catch (Exception ex)
		{
			RuntimeCompatibilityHost.LogLifecycle($"[DataService] HTTP listener start exception: {ex.GetType().Name} - {ex.Message}");
			_isRunning = false;
			CleanupSocket();
			return false;
		}
	}

	public void Stop()
	{
		if (!_isRunning)
		{
			return;
		}

		_isRunning = false;
		CleanupSocket();

		if (_workerThread != null && _workerThread.IsAlive)
		{
			try
			{
				_workerThread.Join(500);
			}
			catch
			{
			}
			_workerThread = null;
		}

		RuntimeCompatibilityHost.LogLifecycle($"[DataService] HTTP server stopped and port {Port} released.");
	}

	private void CleanupSocket()
	{
		if (_listenerSocket != null)
		{
			try
			{
				_listenerSocket.Close();
			}
			catch
			{
			}
			_listenerSocket = null;
		}
	}

	private void WorkerLoop()
	{
		while (_isRunning && _listenerSocket != null)
		{
			try
			{
				// 200ms 超时轮询，使停止信号能被即时响应
				if (_listenerSocket.Poll(200000, SelectMode.SelectRead))
				{
					Socket client = _listenerSocket.Accept();
					if (client != null)
					{
						ThreadPool.QueueUserWorkItem(ProcessClientSafe, client);
					}
				}
			}
			catch (SocketException)
			{
				if (!_isRunning) break;
			}
			catch (Exception)
			{
				if (!_isRunning) break;
			}
		}
	}

	private void ProcessClientSafe(object state)
	{
		Socket client = state as Socket;
		if (client == null) return;

		try
		{
			client.ReceiveTimeout = 2000;
			client.SendTimeout = 2000;

			byte[] buffer = new byte[4096];
			int received = client.Receive(buffer, 0, buffer.Length, SocketFlags.None);
			if (received <= 0)
			{
				CloseClient(client);
				return;
			}

			string rawRequest = Encoding.ASCII.GetString(buffer, 0, received);
			using (StringReader reader = new StringReader(rawRequest))
			{
				string requestLine = reader.ReadLine();
				if (string.IsNullOrEmpty(requestLine))
				{
					CloseClient(client);
					return;
				}

				string[] parts = requestLine.Split(new char[] { ' ' });
				if (parts.Length < 2)
				{
					SendResponse(client, 400, "Bad Request", SimpleJsonWriter.WriteEnvelopeError("BAD_REQUEST", "Malformed HTTP request line."));
					return;
				}

				string method = parts[0].Trim().ToUpperInvariant();
				string rawUrl = parts[1].Trim();

				// 剥离 Query 参数并规范化路径
				string path = rawUrl.Split(new char[] { '?' })[0].TrimEnd(new char[] { '/' });
				if (string.IsNullOrEmpty(path))
				{
					path = "/";
				}

				MatchDataSnapshot snapshot = _snapshotProvider();
				DataServiceResponse response = DispatchRequest(snapshot, method, path);
				SendResponse(client, response.StatusCode, response.StatusText, response.Body);
			}
		}
		catch
		{
			try
			{
				SendResponse(client, 500, "Internal Server Error", SimpleJsonWriter.WriteEnvelopeError("INTERNAL_ERROR", "Internal data service error."));
			}
			catch
			{
			}
		}
		finally
		{
			CloseClient(client);
		}
	}

	public static DataServiceResponse DispatchRequest(MatchDataSnapshot snapshot, string method, string path)
	{
		if (snapshot == null)
		{
			snapshot = MatchDataSnapshot.CreateNoMatch();
		}

		if (string.IsNullOrEmpty(method))
		{
			method = "GET";
		}

		method = method.Trim().ToUpperInvariant();
		if (method != "GET")
		{
			return new DataServiceResponse(405, "Method Not Allowed", SimpleJsonWriter.WriteEnvelopeError("METHOD_NOT_ALLOWED", "Only GET method is supported."));
		}

		if (string.IsNullOrEmpty(path))
		{
			path = "/";
		}
		path = path.TrimEnd(new char[] { '/' });
		if (string.IsNullOrEmpty(path))
		{
			path = "/";
		}

		string nowIso = snapshot.SnapshotTime;

		// 1. GET /api/v1/status
		if (string.Equals(path, "/api/v1/status", StringComparison.OrdinalIgnoreCase))
		{
			StringBuilder sb = new StringBuilder(256);
			SimpleJsonWriter.WriteStatus(snapshot, sb);
			string json = SimpleJsonWriter.WriteEnvelopeSuccess(sb.ToString(), "runtime-live", true, nowIso);
			return new DataServiceResponse(200, "OK", json);
		}

		// 2. GET /api/v1/players
		if (string.Equals(path, "/api/v1/players", StringComparison.OrdinalIgnoreCase))
		{
			StringBuilder sb = new StringBuilder(512);
			SimpleJsonWriter.WritePlayers(snapshot.Players, sb);
			string json = SimpleJsonWriter.WriteEnvelopeSuccess(sb.ToString(), "runtime-live", true, nowIso);
			return new DataServiceResponse(200, "OK", json);
		}

		// 3. GET /api/v1/stats
		if (string.Equals(path, "/api/v1/stats", StringComparison.OrdinalIgnoreCase))
		{
			if (!snapshot.InMatch)
			{
				return new DataServiceResponse(404, "Not Found", SimpleJsonWriter.WriteEnvelopeError("NO_ACTIVE_MATCH", "No active match."));
			}

			StringBuilder sb = new StringBuilder(1024);
			SimpleJsonWriter.WriteStats(snapshot.Stats, sb);
			string json = SimpleJsonWriter.WriteEnvelopeSuccess(sb.ToString(), "match-ledger", true, nowIso);
			return new DataServiceResponse(200, "OK", json);
		}

		// 4. GET /api/v1/stats/damage
		if (string.Equals(path, "/api/v1/stats/damage", StringComparison.OrdinalIgnoreCase))
		{
			if (!snapshot.InMatch)
			{
				return new DataServiceResponse(404, "Not Found", SimpleJsonWriter.WriteEnvelopeError("NO_ACTIVE_MATCH", "No active match."));
			}

			StringBuilder sb = new StringBuilder(512);
			SimpleJsonWriter.WriteDamageRankings(snapshot.Stats.DamageRankings, sb);
			string json = SimpleJsonWriter.WriteEnvelopeSuccess(sb.ToString(), "match-ledger", true, nowIso);
			return new DataServiceResponse(200, "OK", json);
		}

		// 5. GET /api/v1/stats/knockdowns
		if (string.Equals(path, "/api/v1/stats/knockdowns", StringComparison.OrdinalIgnoreCase))
		{
			if (!snapshot.InMatch)
			{
				return new DataServiceResponse(404, "Not Found", SimpleJsonWriter.WriteEnvelopeError("NO_ACTIVE_MATCH", "No active match."));
			}

			StringBuilder sb = new StringBuilder(512);
			SimpleJsonWriter.WriteKnockdownRankings(snapshot.Stats.KnockdownRankings, sb);
			string json = SimpleJsonWriter.WriteEnvelopeSuccess(sb.ToString(), "match-ledger", true, nowIso);
			return new DataServiceResponse(200, "OK", json);
		}

		// 6. GET /api/v1/players/{id} 与 /api/v1/players/{id}/hand
		const string playersPrefix = "/api/v1/players/";
		if (path.StartsWith(playersPrefix, StringComparison.OrdinalIgnoreCase))
		{
			string subPath = path.Substring(playersPrefix.Length);
			string[] segments = subPath.Split(new char[] { '/' });

			if (segments.Length >= 1 && long.TryParse(segments[0], out long playerId))
			{
				if (!snapshot.InMatch)
				{
					return new DataServiceResponse(404, "Not Found", SimpleJsonWriter.WriteEnvelopeError("NO_ACTIVE_MATCH", "No active match."));
				}

				PlayerSnapshot player = snapshot.FindPlayer(playerId);
				if (player == null)
				{
					return new DataServiceResponse(404, "Not Found", SimpleJsonWriter.WriteEnvelopeError("PLAYER_NOT_FOUND", $"Player {playerId} not found in current match."));
				}

				if (segments.Length == 1)
				{
					// GET /api/v1/players/{id}
					StringBuilder sb = new StringBuilder(512);
					SimpleJsonWriter.WritePlayerDetail(player, sb);
					string json = SimpleJsonWriter.WriteEnvelopeSuccess(sb.ToString(), "runtime-live", true, nowIso);
					return new DataServiceResponse(200, "OK", json);
				}

				if (segments.Length == 2 && string.Equals(segments[1], "hand", StringComparison.OrdinalIgnoreCase))
				{
					// GET /api/v1/players/{id}/hand
					HandSnapshot hand = player.Hand;
					StringBuilder sb = new StringBuilder(512);
					SimpleJsonWriter.WriteHand(hand, sb);
					string json = SimpleJsonWriter.WriteEnvelopeSuccess(sb.ToString(), hand.Source, hand.Authoritative, hand.UpdatedAt);
					return new DataServiceResponse(200, "OK", json);
				}
			}
		}

		// 未匹配路由 -> 404
		return new DataServiceResponse(404, "Not Found", SimpleJsonWriter.WriteEnvelopeError("NOT_FOUND", $"Unknown endpoint: {path}"));
	}

	public sealed class DataServiceResponse
	{
		public int StatusCode { get; }
		public string StatusText { get; }
		public string Body { get; }

		public DataServiceResponse(int statusCode, string statusText, string body)
		{
			StatusCode = statusCode;
			StatusText = statusText;
			Body = body;
		}
	}

	private static void SendResponse(Socket client, int statusCode, string statusText, string body)
	{
		try
		{
			byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
			string headers = $"HTTP/1.1 {statusCode} {statusText}\r\n" +
			                 "Content-Type: application/json; charset=utf-8\r\n" +
			                 $"Content-Length: {bodyBytes.Length}\r\n" +
			                 "Access-Control-Allow-Origin: *\r\n" +
			                 "Cache-Control: no-cache, no-store, must-revalidate\r\n" +
			                 "Connection: close\r\n\r\n";

			byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
			client.Send(headerBytes);
			client.Send(bodyBytes);
		}
		catch
		{
		}
	}

	private static void CloseClient(Socket client)
	{
		try
		{
			client.Shutdown(SocketShutdown.Both);
		}
		catch
		{
		}
		try
		{
			client.Close();
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Stop();
	}
}
