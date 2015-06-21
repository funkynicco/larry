using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Larry
{
    public static class Extensions
    {
        #region MemoryStream Read and Write
        public static unsafe short ReadInt16(this MemoryStream stream)
        {
            return (short)(
                stream.GetBuffer()[stream.Position++] |
                stream.GetBuffer()[stream.Position++] << 8);
        }

        public static unsafe int ReadInt32(this MemoryStream stream)
        {
            var buf = stream.GetBuffer();
            return
                buf[stream.Position++] |
                buf[stream.Position++] << 8 |
                buf[stream.Position++] << 16 |
                buf[stream.Position++] << 24;
        }

        public static unsafe long ReadInt64(this MemoryStream stream)
        {
            var buf = stream.GetBuffer();
            return
                buf[stream.Position++] |
                buf[stream.Position++] << 8 |
                buf[stream.Position++] << 16 |
                buf[stream.Position++] << 24 |
                buf[stream.Position++] << 32 |
                buf[stream.Position++] << 40 |
                buf[stream.Position++] << 48 |
                buf[stream.Position++] << 56;
        }

        public static byte[] ReadBytes(this MemoryStream stream, int length)
        {
            var bytes = new byte[length];
            stream.Read(bytes, 0, length);
            return bytes;
        }

        public static unsafe void Write(this MemoryStream stream, short value)
        {
            byte* ptr = (byte*)&value;

            for (int i = 0; i < 2; ++i)
                stream.WriteByte(*ptr++);
        }

        public static unsafe void Write(this MemoryStream stream, int value)
        {
            byte* ptr = (byte*)&value;

            for (int i = 0; i < 4; ++i)
                stream.WriteByte(*ptr++);
        }

        public static unsafe void Write(this MemoryStream stream, long value)
        {
            byte* ptr = (byte*)&value;

            for (int i = 0; i < 8; ++i)
                stream.WriteByte(*ptr++);
        }

        public static void Write(this MemoryStream stream, byte[] data)
        {
            stream.Write(data, 0, data.Length);
        }

        public static string ReadPrefixString(this MemoryStream stream)
        {
            return Encoding.GetEncoding(1252).GetString(stream.ReadBytes(stream.ReadInt32()));
        }

        public static void WritePrefixString(this MemoryStream stream, string text)
        {
            stream.Write(text.Length);
            stream.Write(Encoding.GetEncoding(1252).GetBytes(text));
        }

        public static void Delete(this MemoryStream stream, int length)
        {
            var buffer = stream.GetBuffer();

            Buffer.BlockCopy(buffer, length, buffer, 0, length);
            stream.SetLength(stream.Length - length);
        }
        #endregion

        #region .NET 4.5 stuff
        public static T GetCustomAttribute<T>(this MemberInfo member) where T : Attribute
        {
            return (T)member.GetCustomAttributes(typeof(T), false).FirstOrDefault();
        }
        #endregion
    }
}
