﻿using System;
using System.Net.Sockets;
using System.Threading;

namespace Larry.Network
{
    public class NetworkClientBase : IDisposable, IClient
    {
        private Socket _socket = null;
        private byte[] _buffer = new byte[65536];

        private string _disconnectMessage = null;

        protected string DisconnectMessage { get { return _disconnectMessage; } }
        public bool IsConnected { get { return _socket != null; } }

        public void Dispose()
        {
            Close();
            OnDispose();
        }

        public bool Connect(string host, int port)
        {
            if (_socket != null)
                return false;

            _disconnectMessage = null;

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.Connect(host, port);
            }
            catch
            {
                Close();
                return false;
            }

            OnConnected();
            return true;
        }

        public void Close(string reason)
        {
            if (_socket != null)
            {
                _disconnectMessage = reason;
                OnDisconnected();

                try { _socket.Dispose(); }
                catch { }
                _socket = null;
            }
        }

        public void Close()
        {
            Close(null);
        }

        public virtual void Process(CancellationToken cancellationToken)
        {
            if (_socket != null)
            {
                bool poll;
                try { poll = _socket.Poll(1, SelectMode.SelectRead); }
                catch { poll = false; Close(); }

                if (poll)
                {
                    int len;

                    try { len = _socket.Receive(_buffer); }
                    catch { len = 0; }

                    if (len > 0)
                    {
                        OnData(_buffer, 0, len, cancellationToken);
                    }
                    else
                    {
                        Close();
                    }
                }
            }
        }

        public int Send(byte[] data, int offset, int length)
            => _socket.Send(data, offset, length, SocketFlags.None);

        public NetworkPacket CreatePacket<T>(int requestId, T header) where T : IConvertible
        {
            return NetworkPacket.Create(this, requestId, header);
        }

        // overridable

        protected virtual void OnDispose()
        {
        }

        protected virtual void OnConnected()
        {
        }

        protected virtual void OnDisconnected()
        {
        }

        protected virtual void OnData(byte[] buffer, int offset, int length, CancellationToken cancellationToken)
        {
        }
    }
}
