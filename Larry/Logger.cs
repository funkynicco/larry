using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Larry
{
    public static class Logger
    {
        private static readonly string _filename = "";
        private static long _count = 0;

        static Logger()
        {
            var date = DateTime.Now;

            var path = Environment.OSVersion.Platform == PlatformID.Unix ?
                "logs" :
                "logs";

            _filename = Path.Combine(path, string.Format("{0:0000}-{1:00}-{2:00}", date.Year, date.Month, date.Day));
            Directory.CreateDirectory(_filename);

            _filename = Path.Combine(_filename, string.Format("{0:00}.{1:00}.{2:00}.txt", date.Hour, date.Minute, date.Second));
            _filename = Path.Combine(Environment.CurrentDirectory, _filename);
        }

        private static void LogFile(LogType type, string text)
        {
            var sb = new StringBuilder(256);

            if (Interlocked.Increment(ref _count) > 1)
                sb.Append("\r\n");

            var date = DateTime.Now;
            sb.AppendFormat("[{0:00}:{1:00}:{2:00}][{3}] ", date.Hour, date.Minute, date.Second, Enum.GetName(typeof(LogType), type));
            sb.Append(text);

            System.IO.File.AppendAllText(_filename, sb.ToString());
        }

        public static void Log(LogType type, string text)
        {
            if (Program.EnableDebugMessages ||
                type != LogType.Debug)
            {
                if (Program.ShowTimesInMessages)
                {
                    var date = DateTime.Now;
                    Console.Write("[{0:00}:{1:00}:{2:00}]", date.Hour, date.Minute, date.Second);
                }

                if (type != LogType.Normal)
                {
                    var color = Console.ForegroundColor;
                    switch (type)
                    {
                        case LogType.Debug:
                            Console.ForegroundColor = ConsoleColor.Blue;
                            break;
                        case LogType.Security:
                        case LogType.Warning:
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            break;
                        case LogType.Error:
                            Console.ForegroundColor = ConsoleColor.Red;
                            break;
                    }

                    Console.Write("[{0}] ", Enum.GetName(typeof(LogType), type));

                    if (type != LogType.Error) // reset color
                        Console.ForegroundColor = color;

                    Console.WriteLine(text);

                    Console.ForegroundColor = color;
                }
                else
                    Console.WriteLine(text);
            }

            LogFile(type, text);
        }

        public static void Log(LogType type, string format, params object[] args)
        {
            Log(type, string.Format(format, args));
        }
    }

    public enum LogType : byte
    {
        Normal = 0,
        Warning = 1,
        Error = 2,
        Debug = 3,
        Security = 4
    }
}
