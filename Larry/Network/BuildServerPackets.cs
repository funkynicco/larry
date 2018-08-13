using Larry.File;
using System;
using System.Diagnostics;
using System.IO;

namespace Larry.Network
{
    public partial class BuildServer
    {
        [SkipAuthorization]
        [Packet(PacketHeader.Ping)]
        void OnPing(UserClient client, int requestId)
        {
            // dont do anything ...
        }

        [SkipAuthorization]
        [Packet(PacketHeader.Authorize)]
        void OnAuthorize(UserClient client, int requestId)
        {
            var username = client.Buffer.ReadPrefixString();
            var password_hash = client.Buffer.ReadPrefixString();

            if (username != "kti42j61" ||
                password_hash != "j1i541jiu4h6iuh42unabdquqQGUT31huzcNBVqu") // coconutjuice159873
                throw new DataValidationException("Username or password mismatch");

            client.IsAuthorized = true;

            client.CreatePacket(requestId, PacketHeader.Authorize).Send(); // just send the header back indicating that the authorize was succeeded
        }

        [Packet(PacketHeader.Store)]
        void OnStoreFile(UserClient client, int requestId)
        {
            var remotePath = Utilities.GetPlatformPath(client.Buffer.ReadPrefixString());
            var fileSize = client.Buffer.ReadInt64();
            var ticks = client.Buffer.ReadInt64();

            var remoteChecksum = (uint)client.Buffer.ReadInt32();

            //Logger.Log(LogType.Debug, "Ticks: {0}", ticks);
            var fileDateUtc = DateTime.UtcNow; //DateTime.SpecifyKind(new DateTime(ticks), DateTimeKind.Utc);

            if (Path.IsPathRooted(remotePath) ||
                remotePath.Contains(".."))
            {
                client.CreatePacket(requestId, PacketHeader.Store)
                    .Write((byte)1) // 1 is FAIL - The remote path contains invalid characters
                    .Send();
                return;
            }

            var localPath =
                Environment.OSVersion.Platform == PlatformID.Unix ?
                Path.Combine(Environment.CurrentDirectory, remotePath.Replace("\\", "/")) :
                Path.Combine(Environment.CurrentDirectory, remotePath.Replace("/", "\\"));

            if (!localPath.ToLower().StartsWith(Environment.CurrentDirectory.ToLower()))
            {
                Logger.Log(LogType.Warning, "Invalid path location received: {0}", localPath);
                client.CreatePacket(requestId, PacketHeader.Store)
                    .Write((byte)2) // 2 is FAIL - The remote path is not within the directory specified by server
                    .Send();
                return;
            }

            client.FileTransmit = FileTransmission.BeginReceive(localPath, remotePath, fileDateUtc, fileSize, Path.GetTempFileName(), remoteChecksum);
            client.CurrentTransmitDirection = FileTransmitDirection.Receive;

            Logger.Log(LogType.Debug, "Begin transmit {0} ({1} bytes)", remotePath, fileSize);

            client.CreatePacket(requestId, PacketHeader.Store)
                .Write((byte)0) // 0 is OK - send the data
                .Send();
        }

        private string ReadProcessData(string processName, string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(processName, arguments);
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.Start();
                return process.StandardOutput.ReadToEnd();
            }
        }

        private string ReadProcessData(string processName, string argumentsFormat, params object[] args)
        {
            return ReadProcessData(processName, string.Format(argumentsFormat, args));
        }

        [Packet(PacketHeader.DoBuild)]
        void OnDoBuild(UserClient client, int requestId)
        {
            if (System.IO.File.Exists("myos.iso"))
                System.IO.File.Delete("myos.iso");

            var result = ReadProcessData("grub-mkrescue", "-o myos.iso isodir");
            Logger.Log(LogType.Debug, "\n" + result);

            if (System.IO.File.Exists("myos.iso"))
            {
                client.FileTransmit = FileTransmission.CreateFromFile("myos.iso", "myos.iso");
                client.CurrentTransmitDirection = FileTransmitDirection.Send;

                client.CreatePacket(requestId, PacketHeader.BuildResultFile)
                    .Write(client.FileTransmit.FileSize)
                    .Write(client.FileTransmit.RemoteChecksum)
                    .Send();

                client.FileTransmit.BeginTransmit(); // TODO: make this..
            }
            else
            {
                Logger.Log(LogType.Warning, "Failed to create iso");
                client.CreatePacket(requestId, PacketHeader.BuildResultFile)
                    .Write((long)0)
                    .Send();
            }
        }

        private void RecursiveBuildFileList(NetworkPacket packet, string path)
        {
            var directories = Directory.GetDirectories(path);
            var files = Directory.GetFiles(path);

            packet.Write(directories.Length + files.Length);
            foreach (var directory in directories)
            {
                packet.Write((byte)1); // is directory
                packet.Write(Path.GetFileName(directory));
                packet.Write((long)0); // size
                packet.Write((long)0); // last modified

                RecursiveBuildFileList(packet, directory);
            }

            foreach (var filename in files)
            {
                var fi = new FileInfo(filename);
                packet.Write((byte)0); // is file
                packet.Write(Path.GetFileName(filename));
                packet.Write(fi.Length); // size
                packet.Write(fi.LastWriteTimeUtc.Ticks); // last modified
            }
        }

        [Packet(PacketHeader.RequestFileList)]
        void OnRequestFileList(UserClient client, int requestId)
        {
            var directory = Utilities.GetPlatformPath(client.Buffer.ReadPrefixString());
            var folderFound = Directory.Exists(directory);

            var packet = client.CreatePacket(requestId, PacketHeader.FileList);

            if (!folderFound)
            {
                packet.Write(0); // count
                Logger.Log(LogType.Warning, $"(OnRequestFileList) Directory not found: '{directory}'");
            }
            else
                RecursiveBuildFileList(packet, directory);

            packet.Send();
        }

        void HandleRequestLocalFile(UserClient client, int requestId)
        {
            var filename = Utilities.GetPlatformPath(client.Buffer.ReadPrefixString());

            if (!System.IO.File.Exists(filename))
            {
                client.CreatePacket(requestId, PacketHeader.TransferFileRequestResponse)
                    .Write((byte)0) // succeeded
                    .Send();

                return;
            }

            var fi = new FileInfo(filename);
            var crc = new Crc32().ComputeFile(fi.FullName);

            client.CreatePacket(requestId, PacketHeader.RequestFileResponse)
                .Write((byte)1) // succeeded
                .Write(fi.Length)
                .Write(fi.LastWriteTimeUtc.Ticks)
                .Write(crc)
                .Send();

            using (var stream = System.IO.File.Open(fi.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
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
                        var sent = client.NetworkClient.Send(buffer, sendPos, read - sendPos);
                        if (sent <= 0)
                            throw new Exception("TransferFile Send error. Return value: " + sent);

                        sendPos += sent;
                    }
                }
            }
        }

        [Packet(PacketHeader.TransferFileRequest)]
        void OnTransferFileRequest(UserClient client, int requestId)
        {
            var direction = client.Buffer.ReadByte() == 1 ? FileTransmitDirection.Send : FileTransmitDirection.Receive;
            // when FileTransmitDirection.Send then the client is sending

            if (direction != FileTransmitDirection.Send)
            {
                HandleRequestLocalFile(client, requestId);
                return;
            }

            var fileName = Utilities.GetPlatformPath(client.Buffer.ReadPrefixString());
            var fileSize = client.Buffer.ReadInt64();
            var lastWriteTimeUtc = new DateTime(client.Buffer.ReadInt64(), DateTimeKind.Utc);
            var crc = (uint)client.Buffer.ReadInt32();

            var temporaryFileName = Path.GetTempFileName();

            client.FileTransmit = FileTransmission.BeginReceive(
                fileName,
                fileName,
                lastWriteTimeUtc,
                fileSize,
                temporaryFileName,
                crc);
            client.CurrentTransmitDirection = FileTransmitDirection.Receive;

            Logger.Log(LogType.Normal, $"Receiving {fileName}");

            client.CreatePacket(requestId, PacketHeader.TransferFileRequestResponse)
                .Write((byte)0) // ok (no error code)
                .Send();
        }
    }
}
