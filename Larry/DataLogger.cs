using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Larry
{
    public static class DataLogger
    {
        struct DataIndexEntry
        {
            public long Offset;
            public long Length;
        }

        private static readonly string _textFileName = "";
        private static string _binaryFileName = "";
        private static long _count = 0;

        static DataLogger()
        {
            var date = DateTime.Now;

            var path = Environment.OSVersion.Platform == PlatformID.Unix ?
                "data_logs" :
                "data_logs";

            // text
            _textFileName = Path.Combine(path, string.Format("{0:0000}-{1:00}-{2:00}", date.Year, date.Month, date.Day));
            Directory.CreateDirectory(_textFileName);

            _textFileName = Path.Combine(_textFileName, string.Format("{0:00}.{1:00}.{2:00}.txt", date.Hour, date.Minute, date.Second));
            _textFileName = Path.Combine(Environment.CurrentDirectory, _textFileName);

            // binary file
            _binaryFileName = Path.Combine(path, string.Format("{0:0000}-{1:00}-{2:00}", date.Year, date.Month, date.Day));
            _binaryFileName = Path.Combine(_binaryFileName, string.Format("{0:00}.{1:00}.{2:00}.bin", date.Hour, date.Minute, date.Second));
            _binaryFileName = Path.Combine(Environment.CurrentDirectory, _binaryFileName);
        }

        public static void SetBinaryFilename(string filename, bool appendToExistingFile = false)
        {
            _binaryFileName = filename;
            if (!appendToExistingFile &&
                System.IO.File.Exists(filename))
                System.IO.File.Delete(filename);
        }

        public static void Log(byte[] data, int length)
        {
            var sb = new StringBuilder(1024);
            if (Interlocked.Increment(ref _count) > 1)
                sb.Append("\r\n\r\n");

            sb.AppendFormat("/* Segment length: {0} bytes */\r\n", length);
            for (int i = 0; i < length; ++i)
            {
                if (i > 0)
                {
                    if (i % 32 == 0)
                        sb.Append("\r\n");
                    else
                        sb.Append(' ');
                }

                sb.Append(data[i].ToString("x2"));
            }

            System.IO.File.AppendAllText(_textFileName, sb.ToString());

            using (var stream = new FileStream(_binaryFileName, FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                if (stream.Length == 0)
                {
                    stream.Write((long)8); // dataIndexOffset
                    stream.Write(0); // numberOfIndices
                    stream.Position = 0;
                }

                // data index
                var dataIndexOffset = stream.ReadInt64();
                stream.Position = dataIndexOffset;

                var numberOfIndices = stream.ReadInt32();
                var indices = new List<DataIndexEntry>(numberOfIndices);

                for (int i = 0; i < numberOfIndices; ++i)
                {
                    indices.Add(new DataIndexEntry()
                    {
                        Offset = stream.ReadInt64(),
                        Length = stream.ReadInt64()
                    });
                }

                stream.Position = dataIndexOffset;
                stream.Write(data, 0, length);
                indices.Add(new DataIndexEntry()
                {
                    Offset = dataIndexOffset,
                    Length = length
                });

                dataIndexOffset = stream.Position;
                stream.Write(indices.Count);
                foreach (var index in indices)
                {
                    stream.Write(index.Offset);
                    stream.Write(index.Length);
                }

                stream.Position = 0;
                stream.Write(dataIndexOffset);
            }
        }
    }
}
