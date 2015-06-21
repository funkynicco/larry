using System;

namespace Larry
{
    public static class Logger
    {
        public static void Log(LogType type, string text)
        {
            var date = DateTime.Now;
            Console.Write("[{0:00}:{1:00}:{2:00}]", date.Hour, date.Minute, date.Second);

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
