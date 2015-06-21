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

        private FileTransmission(
            string localPath,
            string remotePath,
            DateTime fileDateUtc,
            long fileSize)
        {
            LocalPath = localPath;
            RemotePath = remotePath;
            FileDateUtc = fileDateUtc;
            _remainingData = FileSize = fileSize;
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
        }

        public void Read(byte[] data, int length)
        {
            if (length > _remainingData)
                throw new DataValidationException(
                    "(FileTransmission.Read) Too much data requested: {0} bytes // {1} bytes overflow",
                    length,
                    length - _remainingData);

            int x;
            if ((x = _stream.Read(data, 0, length)) != length)
                throw new DataValidationException(
                    "(FileTransmission.Read) _stream.Read returned less than requested! Requested: {0}, Read: {1}",
                    length,
                    x);
            _remainingData -= length;
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
            string temporaryFile)
        {
            var transmission = new FileTransmission(localPath, remotePath, fileDateUtc, fileSize);
            transmission._temporaryFile = temporaryFile;
            transmission._stream = new FileStream(temporaryFile, FileMode.Create, FileAccess.Write);
            transmission._remainingData = fileSize;
            return transmission;
        }

        public static FileTransmission CreateFromFile(string localPath, string remotePath)
        {
            var fileInfo = new FileInfo(localPath);
            return new FileTransmission(localPath, remotePath, fileInfo.LastWriteTimeUtc, fileInfo.Length);
        }
    }
}
