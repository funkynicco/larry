using Larry.File;
using Larry.Network;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Larry.Scripts
{
    public class ScriptRunnerDispatcher
    {
        private readonly BuildClient _buildClient;

        private string _localDirectory = null;
        private string _remoteDirectory = null;

        private readonly List<Regex> _localIgnore = new List<Regex>();
        private readonly List<Regex> _remoteIgnore = new List<Regex>();

        public ScriptRunnerDispatcher(BuildClient buildClient)
        {
            _buildClient = buildClient;
        }

        [ScriptCommand("set-local-dir")]
        private void SetLocalDirectory(string directory)
        {
            if (!Directory.Exists(directory))
                throw new ArgumentException("Local directory not found: " + directory);

            _localDirectory = directory;
        }

        [ScriptCommand("set-remote-dir")]
        private void SetRemoteDirectory(string directory)
            => _remoteDirectory = directory;

        [ScriptCommand("add-local-ignore")]
        private void AddLocalIgnore(string regex)
            => _localIgnore.Add(new Regex(regex, RegexOptions.IgnoreCase));

        [ScriptCommand("clear-local-ignore")]
        private void ClearLocalIgnore()
            => _localIgnore.Clear();

        [ScriptCommand("add-remote-ignore")]
        private void AddRemoteIgnore(string regex)
            => _remoteIgnore.Add(new Regex(regex, RegexOptions.IgnoreCase));

        [ScriptCommand("clear-remote-ignore")]
        private void ClearRemoteIgnore()
            => _remoteIgnore.Clear();

        [ScriptCommand("connect")]
        private void Connect(string address)
        {
            if (_buildClient.IsConnected)
                throw new Exception("Build client is already connected server or is currently connecting.");

            var match = Regex.Match(address, @"^([a-z0-9\.]*?):(\d+)$", RegexOptions.IgnoreCase);
            if (!match.Success)
                throw new ArgumentException("The address in connect needs to be in a valid ip:port or dns:port format.");

            var dns = match.Groups[1].Value;
            var port = int.Parse(match.Groups[2].Value);
            if (!_buildClient.Connect(dns, port))
                throw new Exception($"Could not connect to '{address}'");

            if (!_buildClient.WaitForAuthorize(TimeSpan.FromSeconds(5)))
                throw new Exception("Authorization timed out.");
        }

        private bool CheckSkipFile(string filename, IEnumerable<Regex> regices)
        {
            foreach (var regex in regices)
            {
                if (regex.IsMatch(filename))
                    return true;
            }

            return false;
        }

        [ScriptCommand("sync")]
        private void Sync()
        {
            var remoteFileList = _buildClient.GetFileList(_remoteDirectory).MakeReadableFileList();
            var localFileList = FileMetadata.GenerateFileList(_localDirectory).MakeReadableFileList();

            var filesToTransfer = new List<Tuple<string, FileTransmitDirection>>();

            foreach (var remoteFile in remoteFileList)
            {
                if (CheckSkipFile(remoteFile.Key, _remoteIgnore))
                {
                    Logger.Log(LogType.Debug, $"[Sync] Skipping remote file => {remoteFile.Key}");
                    continue;
                }

                FileMetadata localFileMetadata;

                if (!localFileList.TryGetValue(remoteFile.Key, out localFileMetadata))
                {
                    Logger.Log(LogType.Warning, $"Local file missing '{remoteFile.Key}'");
                    filesToTransfer.Add(new Tuple<string, FileTransmitDirection>(remoteFile.Key, FileTransmitDirection.Receive));
                    continue;
                }

                var delta = Utilities.GetDateTimeDeltaSeconds(
                    localFileMetadata.LastModifiedUtc,
                    remoteFile.Value.LastModifiedUtc);

                if (delta > 0)
                {
                    Logger.Log(LogType.Warning, $"Local file date old '{remoteFile.Key}' (L: {localFileMetadata.LastModifiedUtc}, R: {remoteFile.Value.LastModifiedUtc})");
                    filesToTransfer.Add(new Tuple<string, FileTransmitDirection>(remoteFile.Key, FileTransmitDirection.Receive));
                }
                else if (delta < 0)
                {
                    Logger.Log(LogType.Warning, $"Remote file date old '{remoteFile.Key}' (L: {localFileMetadata.LastModifiedUtc}, R: {remoteFile.Value.LastModifiedUtc})");
                    filesToTransfer.Add(new Tuple<string, FileTransmitDirection>(remoteFile.Key, FileTransmitDirection.Send));
                }
            }

            foreach (var localFile in localFileList)
            {
                if (CheckSkipFile(localFile.Key, _localIgnore))
                {
                    Logger.Log(LogType.Debug, $"[Sync] Skipping local file => {localFile.Key}");
                    continue;
                }

                FileMetadata remoteFileMetadata;
                if (!remoteFileList.TryGetValue(localFile.Key, out remoteFileMetadata))
                {
                    Logger.Log(LogType.Warning, $"Remote file missing '{localFile.Key}'");
                    filesToTransfer.Add(new Tuple<string, FileTransmitDirection>(localFile.Key, FileTransmitDirection.Send));
                    continue;
                }
            }

            foreach (var tuple in filesToTransfer)
            {
                Logger.Log(LogType.Normal, $"{(tuple.Item2 == FileTransmitDirection.Send ? "Sending" : "Receiving")} {tuple.Item1} ...");
                _buildClient.TransferFile(
                    Path.Combine(_localDirectory, tuple.Item1),
                    Path.Combine(_remoteDirectory, tuple.Item1),
                    tuple.Item2,
                    TimeSpan.FromSeconds(10));
            }
        }

        [ScriptCommand("exec-local")]
        private void ExecuteLocal(IScriptCommand command)
        {
        }

        [ScriptCommand("exec-remote")]
        private void ExecuteRemote(IScriptCommand command)
        {
        }

        [ScriptCommand("delay")]
        private void Delay(int milliseconds)
        {
            var end = DateTime.UtcNow.AddMilliseconds(milliseconds);

            using (var source = new CancellationTokenSource())
            {
                while (DateTime.UtcNow < end)
                {
                    _buildClient.Process(source.Token);
                    Thread.Sleep(25);
                }
            }
        }

        [ScriptCommand("print")]
        private void Print(string message)
            => Logger.Log(LogType.Normal, message);

        [ScriptCommand("close")]
        private void Close()
            => _buildClient.Close("Script requested close");
    }
}
