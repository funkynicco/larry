using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

public static class Extensions
{
    #region Stream Read and Write
    public static unsafe short ReadInt16(this Stream stream)
    {
        return (short)(
            stream.ReadByte() |
            stream.ReadByte() << 8);
    }

    public static unsafe int ReadInt32(this Stream stream)
    {
        return
            stream.ReadByte() |
            stream.ReadByte() << 8 |
            stream.ReadByte() << 16 |
            stream.ReadByte() << 24;
    }

    public static unsafe long ReadInt64(this Stream stream)
    {
        return
            stream.ReadByte() |
            stream.ReadByte() << 8 |
            stream.ReadByte() << 16 |
            stream.ReadByte() << 24 |
            stream.ReadByte() << 32 |
            stream.ReadByte() << 40 |
            stream.ReadByte() << 48 |
            stream.ReadByte() << 56;
    }

    public static byte[] ReadBytes(this Stream stream, int length)
    {
        var bytes = new byte[length];
        stream.Read(bytes, 0, length);
        return bytes;
    }

    public static unsafe void Write(this Stream stream, short value)
    {
        byte* ptr = (byte*)&value;

        for (int i = 0; i < 2; ++i)
            stream.WriteByte(*ptr++);
    }

    public static unsafe void Write(this Stream stream, int value)
    {
        byte* ptr = (byte*)&value;

        for (int i = 0; i < 4; ++i)
            stream.WriteByte(*ptr++);
    }

    public static unsafe void Write(this Stream stream, long value)
    {
        byte* ptr = (byte*)&value;

        for (int i = 0; i < 8; ++i)
            stream.WriteByte(*ptr++);
    }

    public static void Write(this Stream stream, byte[] data)
    {
        stream.Write(data, 0, data.Length);
    }

    public static string ReadPrefixString(this Stream stream)
    {
        return Encoding.GetEncoding(1252).GetString(stream.ReadBytes(stream.ReadInt32()));
    }

    public static void WritePrefixString(this Stream stream, string text)
    {
        stream.Write(text.Length);
        stream.Write(Encoding.GetEncoding(1252).GetBytes(text));
    }

    public static void Delete(this MemoryStream stream, int length)
    {
        if (length == stream.Length)
        {
            stream.SetLength(0);
            return;
        }

        var buffer = stream.GetBuffer();

        Buffer.BlockCopy(buffer, length, buffer, 0, (int)(stream.Length - length));
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