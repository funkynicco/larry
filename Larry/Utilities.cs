using Larry.Network;
using System.IO;
using System.Text;

namespace Larry
{
    public static class Utilities
    {
        public static ReadPacketResult ReadHeader(MemoryStream stream, out short header, out int dataSize)
        {
            /*
            [1b:Prefix]
            [2b:Header]
            [4b:Length]
            ----------------
            [?b:DATA]
            */

            header = 0;
            dataSize = 0;

            if (stream.Length < NetworkStandards.HeaderSize)
            {
                Logger.Log(LogType.Debug,
                    "Stream Length {0} less than minimum header size: {1} -- Need more data",
                    stream.Length,
                    NetworkStandards.HeaderSize);
                return ReadPacketResult.NeedMoreData;
            }

            var prefix = (byte)stream.ReadByte();
            if (prefix != NetworkStandards.HeaderPrefix)
                return ReadPacketResult.InvalidHeader;

            header = stream.ReadInt16();

            dataSize = stream.ReadInt32();
            if (dataSize < 0 ||
                dataSize > NetworkStandards.MaxPacketLength)
                return ReadPacketResult.DataSizeInvalid;

            if ((stream.Length - stream.Position) < dataSize)
            {
                Logger.Log(LogType.Debug,
                    "The header indicates {0} bytes in data but only {1} was received -- Need more data",
                    dataSize,
                    stream.Length - stream.Position);
                return ReadPacketResult.NeedMoreData;
            }

            return ReadPacketResult.Succeeded;
        }

        public static void DumpData(byte[] data, int offset, int length)
        {
            var sb = new StringBuilder(length * 3 + 32);
            sb.AppendLine();

            for (int i = 0; i < length; ++i)
            {
                if (i > 0)
                {
                    if (i % 16 == 0)
                        sb.AppendLine();
                    else
                        sb.Append(' ');
                }

                sb.Append(data[offset + i].ToString("x2"));
            }

            Logger.Log(LogType.Debug, sb.ToString());
        }

        public static void DumpData(byte[] data, int length)
        {
            DumpData(data, 0, length);
        }
    }
}
