using Larry.File;
using System;
using System.Diagnostics;
using System.IO;

namespace Larry.Network
{
    public partial class BuildServer
    {
        [Packet(PacketHeader.Ping)]
        void OnPing(UserClient client)
        {
            // dont do anything ...
        }

        [Packet(PacketHeader.Authorize)]
        void OnAuthorize(UserClient client)
        {
            var username = client.Buffer.ReadPrefixString();
            var password_hash = client.Buffer.ReadPrefixString();

            if (username != "kti42j61" ||
                password_hash != "j1i541jiu4h6iuh42unabdquqQGUT31huzcNBVqu") // coconutjuice159873
                throw new DataValidationException("Username or password mismatch");

            client.IsAuthorized = true;

            client.CreatePacket(PacketHeader.Authorize).Send(); // just send the header back indicating that the authorize was succeeded
        }

        [Packet(PacketHeader.Store)]
        void OnStoreFile(UserClient client)
        {
            var remotePath = client.Buffer.ReadPrefixString();

            if (Environment.OSVersion.Platform == PlatformID.Unix)
                remotePath = remotePath.Replace('\\', '/');
            else
                remotePath = remotePath.Replace('/', '\\');

            var fileSize = client.Buffer.ReadInt64();
            var ticks = client.Buffer.ReadInt64();
            //Logger.Log(LogType.Debug, "Ticks: {0}", ticks);
            var fileDateUtc = DateTime.UtcNow; //DateTime.SpecifyKind(new DateTime(ticks), DateTimeKind.Utc);

            if (Path.IsPathRooted(remotePath) ||
                remotePath.Contains(".."))
            {
                client.CreatePacket(PacketHeader.Store)
                    .Write((byte)1) // 1 is FAIL - The remote path contains invalid characters
                    .Send();
                return;
            }

            var localPath = Path.Combine(Environment.CurrentDirectory, remotePath.Replace("/", "\\"));

            if (!localPath.ToLower().StartsWith(Environment.CurrentDirectory.ToLower()))
            {
                Logger.Log(LogType.Warning, "Invalid path location received: {0}", localPath);
                client.CreatePacket(PacketHeader.Store)
                    .Write((byte)2) // 2 is FAIL - The remote path is not within the directory specified by server
                    .Send();
                return;
            }

            client.FileTransmit = FileTransmission.BeginReceive(localPath, remotePath, fileDateUtc, fileSize, Path.GetTempFileName());

            Logger.Log(LogType.Debug, "Begin transmit {0} ({1} bytes)", remotePath, fileSize);

            client.CreatePacket(PacketHeader.Store)
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
        void OnDoBuild(UserClient client)
        {
            var result = ReadProcessData("grub-mkrescue", "-o myos.iso isodir");
            Logger.Log(LogType.Debug, "\n" + result);
        }
    }
}
