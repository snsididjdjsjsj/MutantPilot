using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace MutantPilot.Common
{
	/// <summary>
	/// 旧策略客户端的本机协议，只连接 127.0.0.1:47000。当前实战规划器不会创建这个类型。
	/// 头: magic u32, version u16, tick u32, obsCount u16, flags u8，随后 obsCount 个 float。
	/// 回: action u8, cmd u8。
	/// 连接在后台线程里重试，游戏线程不会为了等端口卡住。
	/// </summary>
	public sealed class PilotLink : IDisposable
	{
		public const int Port = 47000;
		const uint Magic = 0x4D504C54;
		const ushort Version = 2;

		readonly object _gate = new();
		TcpClient _client;
		NetworkStream _stream;
		readonly byte[] _header = new byte[13];
		readonly byte[] _reply = new byte[2];
		int _last = PilotControls.Hover;
		volatile bool _connected;
		volatile bool _wanted;
		volatile bool _stop;
		Thread _thread;

		public bool Connected => _connected;

		public bool Wanted
		{
			get => _wanted;
			set
			{
				_wanted = value;
				if (!_wanted)
					Disconnect();
				EnsureThread();
			}
		}

		/// <summary>发出一帧。超时则重复上一个动作并保持连接。断开返回 false。</summary>
		public bool Exchange(uint tick, float[] obs, out int action)
		{
			lock (_gate)
			{
				action = _last;
				if (!_connected || _stream == null)
					return false;
				try
				{
					WriteHeader(tick, obs);
					ReadExact(_reply);
					action = _reply[0];
					_last = action;
					return true;
				}
				catch (IOException ex) when (IsTimeout(ex))
				{
					action = _last;
					return true;
				}
				catch
				{
					CloseLocked();
					return false;
				}
			}
		}

		public void Disconnect()
		{
			lock (_gate)
				CloseLocked();
		}

		public void Dispose()
		{
			_stop = true;
			_wanted = false;
			Disconnect();
		}

		void EnsureThread()
		{
			if (_thread != null)
				return;
			_thread = new Thread(Loop) { IsBackground = true, Name = "MutantPilot" };
			_thread.Start();
		}

		void Loop()
		{
			while (!_stop)
			{
				if (!_wanted || _connected)
				{
					Thread.Sleep(200);
					continue;
				}
				if (!TryConnect())
					Thread.Sleep(800);
			}
		}

		bool TryConnect()
		{
			TcpClient client = null;
			try
			{
				client = new TcpClient();
				var wait = client.BeginConnect("127.0.0.1", Port, null, null);
				if (!wait.AsyncWaitHandle.WaitOne(200) || !client.Connected)
				{
					client.Close();
					return false;
				}
				client.EndConnect(wait);
				if (_stop || !_wanted)
				{
					client.Close();
					return false;
				}
				client.NoDelay = true;
				client.ReceiveTimeout = 50;
				client.SendTimeout = 50;
				lock (_gate)
				{
					if (_stop || !_wanted)
					{
						client.Close();
						return false;
					}
					_client = client;
					_stream = client.GetStream();
					_connected = true;
				}
				return true;
			}
			catch
			{
				try { client?.Close(); } catch { }
				return false;
			}
		}

		void CloseLocked()
		{
			_connected = false;
			try { _stream?.Dispose(); } catch { }
			try { _client?.Close(); } catch { }
			_stream = null;
			_client = null;
		}

		void WriteHeader(uint tick, float[] obs)
		{
			int n = obs?.Length ?? 0;
			if (n > 4096)
				n = 4096;
			WriteU32(Magic, 0);
			WriteU16(Version, 4);
			WriteU32(tick, 6);
			WriteU16((ushort)n, 10);
			_header[12] = 1;
			_stream.Write(_header, 0, _header.Length);
			if (n > 0)
			{
				var raw = new byte[n * 4];
				Buffer.BlockCopy(obs, 0, raw, 0, raw.Length);
				_stream.Write(raw, 0, raw.Length);
			}
		}

		void WriteU16(ushort v, int at)
		{
			_header[at] = (byte)v;
			_header[at + 1] = (byte)(v >> 8);
		}

		void WriteU32(uint v, int at)
		{
			_header[at] = (byte)v;
			_header[at + 1] = (byte)(v >> 8);
			_header[at + 2] = (byte)(v >> 16);
			_header[at + 3] = (byte)(v >> 24);
		}

		void ReadExact(byte[] buf)
		{
			int got = 0;
			while (got < buf.Length)
			{
				int n = _stream.Read(buf, got, buf.Length - got);
				if (n == 0)
					throw new IOException("closed");
				got += n;
			}
		}

		static bool IsTimeout(IOException ex)
		{
			return ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut;
		}
	}
}
