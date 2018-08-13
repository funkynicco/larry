using System;
using System.Globalization;
using System.IO;

namespace Larry.Network
{
    public class NetworkPacket
    {
        private readonly IClient _client;
        private readonly MemoryStream _stream;

        private NetworkPacket(IClient client, int requestId, short header)
        {
            _client = client;
            _stream = new MemoryStream(1024);

            Write(NetworkStandards.HeaderPrefix);
            Write(requestId);
            Write(header);
            Write(0); // length
        }

        public NetworkPacket Write(byte value)
        {
            _stream.WriteByte(value);
            return this;
        }

        public NetworkPacket Write(short value)
        {
            _stream.Write(value);
            return this;
        }

        public NetworkPacket Write(int value)
        {
            _stream.Write(value);
            return this;
        }

        public NetworkPacket Write(long value)
        {
            _stream.Write(value);
            return this;
        }

        public NetworkPacket Write(string value)
        {
            _stream.WritePrefixString(value);
            return this;
        }

        public bool Send()
        {
            _stream.Position = 7;
            Write((int)_stream.Length - NetworkStandards.HeaderSize);
            _stream.Position = _stream.Length;

            int toSend = (int)_stream.Length;
            return _client.Send(_stream.GetBuffer(), 0, toSend) == toSend;
        }

        // static

        public static NetworkPacket Create<T>(IClient client, int requestId, T header) where T : IConvertible
            => new NetworkPacket(client, requestId, header.ToInt16(CultureInfo.InvariantCulture));
    }
}
