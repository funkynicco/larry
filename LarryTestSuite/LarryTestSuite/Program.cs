using Larry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LarryTestSuite
{
    class Program
    {
        struct DataIndexEntry
        {
            public long Offset;
            public long Length;

            public override string ToString()
            {
                return string.Format("Offset: {0}, Length: {1}", Offset, Length);
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("GRUB MakeRescue test");
            File.Copy(@"C:\cygwin64\home\Nicco\os\DONTDELETE_myos.iso", "myos.iso");
            return;

            SubMain(args);
            Console.WriteLine("Press [ESC] to exit.");
            while (true)
            {
                if (Console.ReadKey(true).Key == ConsoleKey.Escape)
                    break;
            }
        }

        static void SubMain(string[] args)
        {
            using (var stream = new FileStream(
                            @"C:\cygwin64\home\Nicco\os\data_logs\2015-06-23\21.12.08.bin",
                            FileMode.OpenOrCreate,
                            FileAccess.ReadWrite))
            {
                var session = new Session();

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

                var buffer = new byte[4096];
                foreach (var index in indices)
                {
                    if (index.Length > buffer.Length)
                        buffer = new byte[index.Length];

                    stream.Position = index.Offset;
                    stream.Read(buffer, 0, (int)index.Length);
                    OnData(session, buffer, (int)index.Length);
                }
            }
        }

        static void OnData(Session session, byte[] buffer, int length)
        {
            session.Buffer.Position = session.Buffer.Length;
            if (length > 0)
            {
                session.Buffer.Write(buffer, 0, length);
                Console.WriteLine("OnData {0} bytes", length);
            }

            if (session.RemainingFileSize > 0)
            {
                int toWrite = (int)Math.Min(65536, Math.Min(session.RemainingFileSize, session.Buffer.Length));
                session.RemainingFileSize -= toWrite;
                session.Buffer.Delete(toWrite);

                Console.WriteLine("SESS_READ {0} bytes, {1} bytes remaining", toWrite, session.RemainingFileSize);

                if (session.Buffer.Length > 0)
                    OnData(session, new byte[] { }, 0);

                return;
            }

            ReadPacketResult result;
            do
            {
                session.Buffer.Position = 0;

                short header;
                int dataSize;

                switch (result = Utilities.ReadHeader(session.Buffer, out header, out dataSize))
                {
                    case ReadPacketResult.InvalidData:
                    case ReadPacketResult.DataSizeInvalid:
                    case ReadPacketResult.InvalidHeader:
                    case ReadPacketResult.UnexpectedHeaderAtThisPoint:
                        Console.WriteLine(
                            "ERR_ReadHeader: {0}",
                            Enum.GetName(typeof(ReadPacketResult), result));
                        //Utilities.DumpData(session.Buffer.GetBuffer(), (int)session.Buffer.Length);
                        Console.ReadKey(true);
                        Environment.Exit(0);
                        return;
                    case ReadPacketResult.NeedMoreData:
                        return; // exit out of OnClientData and wait for more data...
                }

                Console.WriteLine("[header: 0x{0}]", header.ToString("x4"));

                if (header == (short)PacketHeader.BuildResultFile)
                {
                    session.RemainingFileSize = session.Buffer.ReadInt64();
                    session.Buffer.Delete(NetworkStandards.HeaderSize + dataSize);

                    OnData(session, new byte[] { }, 0);
                    return;
                }

                session.Buffer.Delete(NetworkStandards.HeaderSize + dataSize);
                if (session.Buffer.Length == 0)
                    break;
            }
            while (result == ReadPacketResult.Succeeded);
        }
    }

    class Session
    {
        public MemoryStream Buffer { get; private set; }
        public long RemainingFileSize { get; set; }

        public Session()
        {
            Buffer = new MemoryStream(4096);
        }
    }
}
