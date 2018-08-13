using Larry.File;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;

namespace Larry.Network
{
    public partial class BuildClient : NetworkClientBase, IRequestDataHost
    {
        private readonly Dictionary<PacketHeader, MethodInfo> _packetMethods = new Dictionary<PacketHeader, MethodInfo>();

        private readonly MemoryStream _buffer = new MemoryStream(1024);

        private FileTransmission _currentFileTransmission = null;
        private FileTransmitDirection _currentTransmitDirection = FileTransmitDirection.None;
        private readonly Queue<FileTransmission> _fileTransmissionQueue = new Queue<FileTransmission>();

        private bool _buildCompletedAndReceived = false;
        public bool IsScriptClient { get; } = false;

        private int _nextRequestId = 0;
        private readonly Dictionary<int, RequestData> _requests = new Dictionary<int, RequestData>();

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

        private byte[] _fileTransmitBuffer = new byte[1048576];

        public BuildClient(bool scriptClient)
        {
            IsScriptClient = scriptClient;

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
            Logger.Log(LogType.Normal, "Connected to Build Server");

            CreatePacket(0, PacketHeader.Authorize)
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

        protected override void OnData(byte[] buffer, int offset, int length, CancellationToken cancellationToken)
        {
            //Logger.Log(LogType.Debug, "Received {0} bytes", length);
            if (length != 0)
                DataLogger.Log(buffer, offset, length);

            if (_currentFileTransmission != null &&
                _currentTransmitDirection == FileTransmitDirection.Receive)
            {
                if (_buffer.Length > 0)
                {
                    var len = _buffer.Length;
                    _buffer.Position = 0;
                    _buffer.SetLength(0);
                    OnData(_buffer.GetBuffer(), 0, (int)len, cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                        return;
                }

                if (length == 0)
                    return;

                //Logger.Log(LogType.Debug, "Remaining: {0}", _currentFileTransmission.Remaining);

                // we're currently receiving a file
                int toWrite = (int)Math.Min(length, _currentFileTransmission.Remaining);
                _currentFileTransmission.Write(buffer, offset, toWrite);

                //Logger.Log(LogType.Debug, "Wrote {0} bytes - Remaining {1}", toWrite, userClient.FileTransmit.Remaining);

                if (_currentFileTransmission.Remaining == 0)
                {
                    _currentFileTransmission.EndReceive();

                    Logger.Log(LogType.Normal, "File complete: {0}", _currentFileTransmission.RemotePath);

                    if (_currentFileTransmission.IsFileCorrupted)
                        Logger.Log(LogType.Error, "ERR FILE CORRUPTED => {0}", _currentFileTransmission.RemotePath);

                    _currentFileTransmission.Dispose();
                    _currentFileTransmission = null;
                    _currentTransmitDirection = FileTransmitDirection.None;

                    //CreatePacket(PacketHeader.TransmitComplete).Send(); // notify transmit completed

                    if ((length - toWrite) > 0)
                    {
                        var newBuffer = new byte[length - toWrite];
                        Buffer.BlockCopy(buffer, offset + toWrite, newBuffer, 0, length - toWrite);
                        OnData(newBuffer, 0, newBuffer.Length, cancellationToken); // read the remaining data
                    }
                }

                return;
            }

            _buffer.Position = _buffer.Length;
            _buffer.Write(buffer, offset, length);

            ReadPacketResult result;
            do
            {
                _buffer.Position = 0;

                int requestId;
                short header;
                int dataSize;

                switch (result = Utilities.ReadHeader(_buffer, out requestId, out header, out dataSize))
                {
                    case ReadPacketResult.InvalidData:
                    case ReadPacketResult.DataSizeInvalid:
                    case ReadPacketResult.InvalidHeader:
                    case ReadPacketResult.UnexpectedHeaderAtThisPoint:
                        Logger.Log(
                            LogType.Warning,
                            "ReadHeader: {0}",
                            Enum.GetName(typeof(ReadPacketResult), result));
                        Utilities.DumpData(_buffer.GetBuffer(), (int)_buffer.Length);
                        //Close();
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
                    methodInfo.Invoke(this, new object[] { requestId });
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException is DataValidationException)
                    {
                        Close(ex.Message);
                    }
                    else if (ex.InnerException is FileTransmitBeginException)
                    {
                        _buffer.Delete(NetworkStandards.HeaderSize + dataSize);
                        OnData(new byte[] { }, 0, 0, cancellationToken);
                        return;
                    }
                }

                if (!IsConnected)
                    return;

                _buffer.Delete(NetworkStandards.HeaderSize + dataSize);

                if (cancellationToken.IsCancellationRequested)
                    return;

                if (_currentTransmitDirection == FileTransmitDirection.Receive)
                {
                    OnData(new byte[] { }, 0, 0, cancellationToken);
                    return;
                }

                if (_buffer.Length == 0)
                    break;
            }
            while (result == ReadPacketResult.Succeeded);
        }

        public override void Process(CancellationToken cancellationToken)
        {
            base.Process(cancellationToken);

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
                    CreatePacket(0, PacketHeader.Ping).Send();
                }

                // check queue
                if (_currentFileTransmission != null &&
                    _currentTransmitDirection == FileTransmitDirection.Send)
                {
                    if (_currentFileTransmission.IsTransmitting)
                    {
                        // send chunk...
                        int toSend = (int)Math.Min(_currentFileTransmission.Remaining, _fileTransmitBuffer.Length);

                        int numberOfBytesRead = 0;
                        while (numberOfBytesRead < toSend)
                            numberOfBytesRead += _currentFileTransmission.Read(_fileTransmitBuffer, 0, toSend - numberOfBytesRead);

                        int numberOfBytesSent = 0;
                        while (numberOfBytesSent < toSend)
                            numberOfBytesSent += Send(_fileTransmitBuffer, 0, toSend - numberOfBytesSent);

                        if (_currentFileTransmission.Remaining == 0)
                        {
                            // file upload completed
                            Logger.Log(LogType.Normal, "Transmit completed.");

                            _currentFileTransmission.EndTransmit();
                            _currentFileTransmission.Dispose();
                            _currentFileTransmission = null;
                            _currentTransmitDirection = FileTransmitDirection.None;
                        }
                    }
                }
                else if (_fileTransmissionQueue.Count > 0)
                {
                    _currentFileTransmission = _fileTransmissionQueue.Dequeue();
                    _currentTransmitDirection = FileTransmitDirection.Send;

                    /*Logger.Log(
                        LogType.Debug,
                        "Request transmit {0}",
                        _currentFileTransmission.LocalPath);*/

                    CreatePacket(0, PacketHeader.Store)
                        .Write(_currentFileTransmission.RemotePath)
                        .Write(_currentFileTransmission.FileSize)
                        .Write(_currentFileTransmission.FileDateUtc.Ticks)
                        .Write((int)_currentFileTransmission.RemoteChecksum)
                        .Send();
                }
            }
        }

        public bool WaitForAuthorize(TimeSpan timeSpan)
        {
            using (var source = new CancellationTokenSource())
            {
                var end = DateTime.UtcNow + timeSpan;
                while (!_isAuthorized &&
                    DateTime.UtcNow < end)
                {
                    Process(source.Token);
                    Thread.Sleep(100);
                }
            }

            return _isAuthorized;
        }

        public void WaitForAuthorize()
            => WaitForAuthorize(TimeSpan.FromDays(1));

        private RequestData CreateRequest()
        {
            var request = new RequestData(this, Interlocked.Increment(ref _nextRequestId));
            _requests[request.Id] = request;
            return request;
        }

        public void RemoveRequest(RequestData request)
            => _requests.Remove(request.Id);

        private bool WaitForRequest(RequestData request, TimeSpan timeSpan)
        {
            var end = DateTime.UtcNow + timeSpan;
            while (!request.IsFinished &&
                DateTime.UtcNow < end)
            {
                Process(request.Token);
                Thread.Sleep(100);
            }

            return request.IsFinished;
        }

        private void WaitForRequest(RequestData request)
            => WaitForRequest(request, TimeSpan.FromDays(1));

        public FileMetadata GetFileList(string directory, TimeSpan timeout)
        {
            using (var request = CreateRequest())
            {
                CreatePacket(request.Id, PacketHeader.RequestFileList)
                    .Write(directory)
                    .Send();

                if (!WaitForRequest(request, timeout))
                    throw new RequestTimeoutException();

                return request.Data as FileMetadata;
            }
        }

        public FileMetadata GetFileList(string directory)
            => GetFileList(directory, TimeSpan.FromSeconds(5));

        public void TransferFile(string localFilename, string remoteFilename, FileTransmitDirection direction, TimeSpan timeout)
        {
            if (direction == FileTransmitDirection.Send)
            {
                var fi = new FileInfo(localFilename);
                var succeeded = false;

                var crc = new Crc32().ComputeFile(fi.FullName);

                using (var request = CreateRequest())
                {
                    CreatePacket(request.Id, PacketHeader.TransferFileRequest)
                        .Write((byte)1) // send
                        .Write(remoteFilename)
                        .Write(fi.Length)
                        .Write(fi.LastWriteTimeUtc.Ticks)
                        .Write(crc)
                        .Send();

                    if (!WaitForRequest(request, timeout))
                        throw new RequestTimeoutException();

                    succeeded = (bool)request.Data;
                }

                if (!succeeded)
                    throw new InvalidOperationException("Failed to request to transfer file...");

                using (var stream = System.IO.File.Open(localFilename, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var pos = 0L;
                    var buffer = new byte[65536];

                    while (pos < fi.Length)
                    {
                        var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, fi.Length - pos));
                        pos += read;

                        var sendPos = 0;
                        while (sendPos < read)
                        {
                            var sent = Send(buffer, sendPos, read - sendPos);
                            if (sent <= 0)
                                throw new Exception("TransferFile Send error. Return value: " + sent);

                            sendPos += sent;
                        }
                    }
                }
            }
            else // receive
            {
                FileRequestResult result;

                using (var request = CreateRequest())
                {
                    CreatePacket(request.Id, PacketHeader.TransferFileRequest)
                        .Write((byte)0) // receive
                        .Write(remoteFilename)
                        .Send();

                    if (!WaitForRequest(request, timeout))
                        throw new RequestTimeoutException();

                    result = (FileRequestResult)request.Data;
                }

                if (!result.Succeeded)
                    throw new InvalidOperationException("Failed to request to receive file...");

                // receive data...
                _currentTransmitDirection = FileTransmitDirection.Receive;
                _currentFileTransmission = FileTransmission.BeginReceive(
                    localFilename,
                    remoteFilename,
                    result.LastWriteTimeUtc,
                    result.Size,
                    Path.GetTempFileName(),
                    result.Crc);
            }
        }

        public class RequestTimeoutException : Exception
        {
            public RequestTimeoutException() :
                base("The request timed out.")
            {
            }
        }

        public class FileRequestResult
        {
            public bool Succeeded { get; set; }

            public long Size { get; set; }

            public DateTime LastWriteTimeUtc { get; set; }

            public uint Crc { get; set; }

            public FileRequestResult(bool succeeded)
            {
                Succeeded = succeeded;
            }
        }
    }
}
