using Larry.Network;
using System;
using System.IO;

namespace Larry.File
{
    public class FileTransmission : IDisposable
    {
        public string LocalPath { get; private set; }
        public string RemotePath { get; private set; }
        public DateTime FileDateUtc { get; private set; }
        public long FileSize { get; private set; }

        private FileStream _stream = null;
        private long _remainingData = 0;
        private string _temporaryFile = null;

        public long Remaining { get { return _remainingData; } }

        public bool IsTransmitting { get { return _stream != null; } }

        private readonly uint _remoteChecksum;
        private readonly Crc32 _localCrc = new Crc32();

        public uint RemoteChecksum { get { return _remoteChecksum; } }
        public uint LocalChecksum { get { return _localCrc.Result; } }

        /// <summary>
        /// Gets a value indicating whether the local file matches the remote file based on the checksum.
        /// </summary>
        public bool IsFileCorrupted
        {
            get { return _localCrc.Result != _remoteChecksum; }
        }

        private FileTransmission(
            string localPath,
            string remotePath,
            DateTime fileDateUtc,
            long fileSize,
            uint senderChecksum)
        {
            LocalPath = localPath;
            RemotePath = remotePath;
            FileDateUtc = fileDateUtc;
            _remainingData = FileSize = fileSize;
            _remoteChecksum = senderChecksum;
        }

        public void Dispose()
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
        }

        public void Write(byte[] data, int length)
        {
            if (length > _remainingData)
                throw new DataValidationException(
                    "(FileTransmission.Write) Too much data received: {0} bytes // {1} bytes overflow",
                    length,
                    length - _remainingData);

            _stream.Write(data, 0, length);
            _remainingData -= length;

            _localCrc.ComputeChunk(data, 0, length);
        }

        public int Read(byte[] data, int length)
        {
            if (length > _remainingData)
                throw new DataValidationException(
                    "(FileTransmission.Read) Too much data requested: {0} bytes // {1} bytes overflow",
                    length,
                    length - _remainingData);

            int numberOfBytesRead = _stream.Read(data, 0, length);
            if (numberOfBytesRead == 0)
                throw new DataValidationException(
                    "(FileTransmission.Read) End of file! Requested: {0}",
                    length);
            _remainingData -= numberOfBytesRead;
            return numberOfBytesRead;
        }

        public void BeginTransmit()
        {
            if (_stream != null)
                throw new InvalidOperationException("Cannot begin transmit on an open stream.");

            _remainingData = FileSize;
            _stream = new FileStream(LocalPath, FileMode.Open, FileAccess.Read);
        }

        public void EndTransmit()
        {
            if (_remainingData > 0)
                throw new InvalidOperationException("_remainingData is not 0");

            _stream.Dispose();
            _stream = null;
        }

        public void EndReceive()
        {
            if (_remainingData > 0)
                throw new InvalidOperationException("_remainingData is not 0");

            _stream.Dispose();
            _stream = null;

            if (!string.IsNullOrEmpty(_temporaryFile))
            {
                var path = Path.GetDirectoryName(LocalPath);
                if (path.Length > 0)
                    Directory.CreateDirectory(path); // make sure directory exists

                System.IO.File.Copy(_temporaryFile, LocalPath, true);
                new FileInfo(LocalPath).LastWriteTimeUtc = FileDateUtc;
            }
        }

        public static FileTransmission BeginReceive(
            string localPath,
            string remotePath,
            DateTime fileDateUtc,
            long fileSize,
            string temporaryFile,
            uint remoteChecksum)
        {
            //var crc = new Crc32();
            //var serverChecksum = crc.ComputeFile(localPath);

            var transmission = new FileTransmission(localPath, remotePath, fileDateUtc, fileSize, remoteChecksum);
            transmission._temporaryFile = temporaryFile;
            transmission._stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write);
            transmission._remainingData = fileSize;
            return transmission;
        }

        public static FileTransmission CreateFromFile(string localPath, string remotePath)
        {
            var fileInfo = new FileInfo(localPath);

            // calculate checksum ...
            var crc = new Crc32();
            var localChecksum = crc.ComputeFile(localPath);

            return new FileTransmission(localPath, remotePath, fileInfo.LastWriteTimeUtc, fileInfo.Length, localChecksum);
        }
    }
}
