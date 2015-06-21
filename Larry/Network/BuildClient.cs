using Larry.File;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Larry.Network
{
    public partial class BuildClient : NetworkClientBase
    {
        private readonly Dictionary<PacketHeader, MethodInfo> _packetMethods = new Dictionary<PacketHeader, MethodInfo>();

        private readonly MemoryStream _buffer = new MemoryStream(1024);

        private FileTransmission _currentFileTransmission = null;
        private readonly Queue<FileTransmission> _fileTransmissionQueue = new Queue<FileTransmission>();

        private bool _buildCompletedAndReceived = false;

        public bool IsFinished
        {
            get
            {
                return
                    _currentFileTransmission == null &&
                    _fileTransmissionQueue.Count == 0 &&
                    _buildCompletedAndReceived;
            }
        }

        private bool _isAuthorized = false;
        private int _nextSendPing = 0;

        private byte[] _readFileBuffer = new byte[65536]; // 65536

        public BuildClient()
        {
            foreach (var mi in GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                var attribute = mi.GetCustomAttribute<PacketAttribute>();
                if (attribute != null)
                {
                    _packetMethods.Add(attribute.Header, mi);

                    //Logger.Log(LogType.Debug, "Binding method '{0}' to packet '{1}'", mi.Name, attribute.Header);
                }
            }
        }

        public void AddFileTransmission(FileTransmission fileTransmission)
        {
            _fileTransmissionQueue.Enqueue(fileTransmission);
        }

        protected override void OnConnected()
        {
            Logger.Log(LogType.Debug, "Connected to Build Server");

            CreatePacket(PacketHeader.Authorize)
                .Write("kti42j61") // username
                .Write("j1i541jiu4h6iuh42unabdquqQGUT31huzcNBVqu") // password hash
                .Send();
        }

        protected override void OnDisconnected()
        {
            _isAuthorized = false;
            if (!string.IsNullOrEmpty(DisconnectMessage))
                Logger.Log(LogType.Debug, "Disconnected from Build Server: " + DisconnectMessage);
            else
                Logger.Log(LogType.Debug, "Disconnected from Build Server");
        }

        protected override void OnData(byte[] buffer, int length)
        {
            //Logger.Log(LogType.Debug, "Received {0} bytes", length);

            _buffer.Position = _buffer.Length;
            _buffer.Write(buffer, 0, length);

            ReadPacketResult result;
            do
            {
                _buffer.Position = 0;

                short header;
                int dataSize;

                switch (result = Utilities.ReadHeader(_buffer, out header, out dataSize))
                {
                    case ReadPacketResult.InvalidData:
                    case ReadPacketResult.DataSizeInvalid:
                    case ReadPacketResult.InvalidHeader:
                    case ReadPacketResult.UnexpectedHeaderAtThisPoint:
                        Logger.Log(
                            LogType.Warning,
                            "ReadHeader: {0}",
                            Enum.GetName(typeof(ReadPacketResult), result));
                        Utilities.DumpData(buffer, length);
                        Close();
                        return;
                    case ReadPacketResult.NeedMoreData:
                        return; // exit out of OnClientData and wait for more data...
                }

                // call packet methods

                MethodInfo methodInfo;
                if (!_packetMethods.TryGetValue((PacketHeader)header, out methodInfo))
                {
                    Logger.Log(
                           LogType.Warning,
                           "Unknown packet header: 0x{0}",
                           header.ToString("x4"));
                    Close();
                    return;
                }

                try
                {
                    methodInfo.Invoke(this, new object[] { });
                }
                catch (DataValidationException ex)
                {
                    Close(ex.Message);
                }

                if (!IsConnected)
                    return;

                _buffer.Delete(NetworkStandards.HeaderSize + dataSize);

                if (_buffer.Length == 0)
                    break;
            }
            while (result == ReadPacketResult.Succeeded);
        }

        public override void Process()
        {
            base.Process();

            if (!IsConnected)
                return;

            int tick = Environment.TickCount;

            if (_isAuthorized)
            {
                if (tick >= _nextSendPing &&
                    (_currentFileTransmission == null ||
                    !_currentFileTransmission.IsTransmitting))
                {
                    _nextSendPing = tick + 10000;
                    CreatePacket(PacketHeader.Ping).Send();
                }

                // check queue
                if (_currentFileTransmission != null)
                {
                    if (_currentFileTransmission.IsTransmitting)
                    {
                        // send chunk...
                        int toSend = (int)Math.Min(_currentFileTransmission.Remaining, _readFileBuffer.Length);
                        _currentFileTransmission.Read(_readFileBuffer, toSend);

                        if (Send(_readFileBuffer, toSend) != toSend)
                            throw new Exception("Fatal - NetworkClientBase.Send returned less than requested to send");
                        //Logger.Log(LogType.Debug, "Sent {0} bytes - Remaining {1}", toSend, _currentFileTransmission.Remaining);

                        if (_currentFileTransmission.Remaining == 0)
                        {
                            // file upload completed
                            Logger.Log(LogType.Debug, "Transmit completed.");

                            _currentFileTransmission.EndTransmit();
                            _currentFileTransmission.Dispose();
                            _currentFileTransmission = null;
                        }
                    }
                }
                else if (_fileTransmissionQueue.Count > 0)
                {
                    _currentFileTransmission = _fileTransmissionQueue.Dequeue();

                    /*Logger.Log(
                        LogType.Debug,
                        "Request transmit {0}",
                        _currentFileTransmission.LocalPath);*/

                    CreatePacket(PacketHeader.Store)
                        .Write(_currentFileTransmission.RemotePath)
                        .Write(_currentFileTransmission.FileSize)
                        .Write(_currentFileTransmission.FileDateUtc.Ticks)
                        .Send();
                }
            }
        }
    }
}
