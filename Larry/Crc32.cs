using System;
using System.IO;

namespace Larry
{
    public class Crc32
    {
        private readonly uint[] Table = new uint[256];
        private const uint Polynomial = 0xedb88320;
        private const uint Seed = 0xffffffff;

        private uint _currentSeed = Seed;

        public uint Result
        {
            get { return _currentSeed; }
        }

        public Crc32()
        {
            for (int i = 0; i < 256; ++i)
            {
                uint dw = (uint)i;
                for (int j = 0; j < 8; ++j)
                {
                    if ((dw & 1) != 0)
                        dw = (dw >> 1) ^ Polynomial;
                    else
                        dw = dw >> 1;
                }
                Table[i] = dw;
            }
        }

        public void ResetSeed()
        {
            _currentSeed = Seed;
        }

        public void SetSeed(uint seed)
        {
            _currentSeed = seed;
        }

        public uint ComputeFile(string filename)
        {
            var buffer = new byte[1048576];
            using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                while (fs.Position < fs.Length)
                {
                    int read = fs.Read(buffer, 0, (int)Math.Min(fs.Length - fs.Position, buffer.Length));
                    for (int i = 0; i < read; ++i)
                        _currentSeed = (_currentSeed >> 8) ^ Table[buffer[i] ^ _currentSeed & 0xff];
                }
            }

            return _currentSeed;
        }

        public uint ComputeChunk(byte[] data, int offset, int length)
        {
            for (int i = offset; i < offset + length; ++i)
                _currentSeed = (_currentSeed >> 8) ^ Table[data[i] ^ _currentSeed & 0xff];

            return _currentSeed;
        }
    }
}
