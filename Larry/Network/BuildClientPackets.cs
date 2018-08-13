using Larry.File;
using System;
using System.IO;

namespace Larry.Network
{
    public partial class BuildClient
    {
        [Packet(PacketHeader.Authorize)]
        void OnAuthorize(int requestId)
        {
            _nextSendPing = Environment.TickCount + 10000;
            _isAuthorized = true;

            // if client received this then we are good to store the file
            Logger.Log(LogType.Debug, "Successfully authorized with build server.");
        }

        [Packet(PacketHeader.Store)]
        void OnStoreFile(int requestId)
        {
            byte result = (byte)_buffer.ReadByte();
            if (result != 0)
            {
                Logger.Log(LogType.Warning, "Transmit denied. Code: {0}", result);

                _currentFileTransmission.Dispose();
                _currentFileTransmission = null;
                return;
            }

            Logger.Log(LogType.Normal, "Begin transmit of {0} ({1} bytes)", _currentFileTransmission.LocalPath, _currentFileTransmission.FileSize);
            _currentFileTransmission.BeginTransmit();
        }

        [Packet(PacketHeader.TransmitComplete)]
        void OnTransmitCompleted(int requestId)
        {
            //Program.QueryShutdown = true;
            if (IsScriptClient)
                return;

            if (_fileTransmissionQueue.Count == 0 &&
                _currentFileTransmission == null)
            {
                // send build
                CreatePacket(0, PacketHeader.DoBuild)
                    .Send();
            }
        }

        [Packet(PacketHeader.BuildResultFile)]
        void OnBuildResultFile(int requestId)
        {
            long fileSize = _buffer.ReadInt64();
            var remoteChecksum = (uint)_buffer.ReadInt32();

            //Logger.Log(LogType.Debug, "OnBuildResultFile - {0} bytes", fileSize);

            _currentTransmitDirection = FileTransmitDirection.Receive;
            _currentFileTransmission = FileTransmission.BeginReceive(
                "myos.iso",
                "myos.iso",
                DateTime.UtcNow,
                fileSize,
                Path.GetTempFileName(),
                remoteChecksum);

            _currentFileTransmission.FileReceived += (sender) => _buildCompletedAndReceived = true;

            Logger.Log(LogType.Normal, "Receiving myos.iso - {0} bytes, checksum: {1}", fileSize, remoteChecksum.ToString("X8"));

            throw new FileTransmitBeginException();
        }

        private void RecursiveReadFileListFolder(MemoryStream buffer, FileMetadata parent)
        {
            var count = buffer.ReadInt32();
            for (int i = 0; i < count; ++i)
            {
                var isFolder = buffer.ReadByte() != 0;
                var name = buffer.ReadPrefixString();
                var size = buffer.ReadInt64();

                var ticks = buffer.ReadInt64();
                var lastModifiedUtc = new DateTime(ticks, DateTimeKind.Utc);

                var node = new FileMetadata(parent, isFolder, name, size, lastModifiedUtc);
                node.Next = parent.Children;
                parent.Children = node;

                if (isFolder)
                    RecursiveReadFileListFolder(buffer, node);
            }
        }

        [Packet(PacketHeader.FileList)]
        void OnFileList(int requestId)
        {
            if (!_requests.TryGetValue(requestId, out RequestData request))
                return;

            var root = FileMetadata.CreateRoot();
            RecursiveReadFileListFolder(_buffer, root);
            request.Complete(root);
        }

        [Packet(PacketHeader.TransferFileRequestResponse)]
        void OnTransferFileRequestResponse(int requestId)
        {
            if (!_requests.TryGetValue(requestId, out RequestData request))
                return;

            var result = _buffer.ReadByte();

            request.Complete(result == 0);
        }

        [Packet(PacketHeader.RequestFileResponse)]
        void OnRequestFileResponse(int requestId)
        {
            if (!_requests.TryGetValue(requestId, out RequestData request))
                return;

            var result = new FileRequestResult(_buffer.ReadByte() != 0);
            if (result.Succeeded)
            {
                result.Size = _buffer.ReadInt64();
                result.LastWriteTimeUtc = new DateTime(_buffer.ReadInt64(), DateTimeKind.Utc);
                result.Crc = (uint)_buffer.ReadInt32();
            }

            request.Complete(result);
        }
    }
}
