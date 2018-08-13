using Larry.File;
using Larry.Network;
using Larry.Scripts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Larry
{
    class Program
    {
        public static bool EnableDebugMessages { get; private set; }
        public static bool ShowTimesInMessages { get; private set; }
        const int SleepTime = 30;

        struct FileSendObject
        {
            public string LocalPath { get; private set; }
            public string RemotePath { get; private set; }

            public FileSendObject(string localPath, string remotePath)
            {
                LocalPath = localPath;
                RemotePath = remotePath;
            }
        }

        static void SubMain(string[] args)
        {
            Console.WriteLine($"Larry Build {BuildVersion.Version} ({BuildVersion.Date} UTC)");

            using (var directoryChanger = new DirectoryChanger()) // DirectoryChanger automatically resets the working directory upon Dispose
            {
                int port = 11929;
                bool isServer = false;
                string address = string.Empty;
                string username = string.Empty;
                string password = string.Empty;
                int paramIndex = 0;
                var filesToSend = new List<FileSendObject>();
                ShowTimesInMessages = true;
#if DEBUG
                EnableDebugMessages = true;
#else // DEBUG
                EnableDebugMessages = false;
#endif // DEBUG

                for (int i = 0; i < args.Length; ++i)
                {
                    if (args[i] == "--server")
                    {
                        isServer = true;
                    }
                    else if (args[i] == "--script")
                    {
                        if (i + 1 >= args.Length)
                            throw new ArgumentNotProvidedException("Script filename");

                        using (var scriptRunner = new ScriptRunner())
                        {
                            scriptRunner.Run(args[i + 1]);
                        }

                        return;
                    }
                    else if (args[i] == "-p")
                    {
                        if (i + 1 >= args.Length)
                            throw new ArgumentNotProvidedException("Port");

                        port = int.Parse(args[++i]);
                    }
                    else if (args[i] == "-d" ||
                        args[i] == "--directory")
                    {
                        if (i + 1 >= args.Length)
                            throw new ArgumentNotProvidedException("Directory");

                        directoryChanger.Change(args[++i]);
                    }
                    else if (args[i] == "--debug")
                    {
                        EnableDebugMessages = true;
                    }
                    else if (args[i] == "--no-time")
                    {
                        ShowTimesInMessages = false;
                    }
                    else
                    {
                        switch (paramIndex++)
                        {
                            case 0:
                                address = args[i];
                                break;
                            default:
                                {
                                    var match = Regex.Match(args[i], "^([^=]+)=([^=]+)$");
                                    if (!match.Success)
                                        throw new MessageException("One or more files to send in parameters were invalid.");

                                    if (!System.IO.File.Exists(match.Groups[1].Value))
                                        throw new MessageException("Local file not found: {0}", match.Groups[1].Value);

                                    filesToSend.Add(new FileSendObject(match.Groups[1].Value, match.Groups[2].Value));
                                }
                                break;
                        }
                    }
                }

                Logger.Log(LogType.Normal, "Platform: " + Environment.OSVersion.Platform);

                if (isServer)
                {
                    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                        Console.Title = "Build Server";

                    using (var server = new BuildServer())
                    {
                        if (server.Start(port))
                        {
                            Logger.Log(LogType.Normal, "Build Server running on port {0}", port);

                            while (true)
                            {
                                // ...
                                if (Console.KeyAvailable)
                                {
                                    var key = Console.ReadKey(true);
                                    if (key.Key == ConsoleKey.Escape)
                                        break;
                                }

                                server.Process();
                                Thread.Sleep(SleepTime);
                            }
                        }
                        else
                            Logger.Log(LogType.Error, "Failed to start server. Make sure that port {0} is not in use.", port);
                    }

                    return;
                }

                if (string.IsNullOrEmpty(address))
                    throw new MessageException("<address> was not provided.");

                if (filesToSend.Count == 0)
                    throw new MessageException("There are no files to send.");

                // run client
                using (var client = new BuildClient(false))
                {
                    if (client.Connect(address, port))
                    {
                        //client.AddFileTransmission(FileTransmission.CreateFromFile("output\\myos.bin", "isodir\\boot\\myos.bin")); // path is normalized in linux
                        //client.AddFileTransmission(FileTransmission.CreateFromFile("src\\grub.cfg", "isodir\\boot\\grub\\grub.cfg"));

                        foreach (var file in filesToSend)
                        {
                            client.AddFileTransmission(FileTransmission.CreateFromFile(
                                file.LocalPath,
                                file.RemotePath));
                        }

                        using (var source = new CancellationTokenSource())
                        {
                            while (!client.IsFinished)
                            {
                                client.Process(source.Token);
                                Thread.Sleep(SleepTime);
                            }
                        }
                    }
                    else
                        Logger.Log(LogType.Error, "Failed to connect to {0}:{1}", address, port);
                }
            }
        }

        static void Main(string[] args)
        {
            string originalConsoleTitle = null;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                originalConsoleTitle = Console.Title;

            //DataLogger.SetBinaryFilename(@"C:\cygwin64\home\Nicco\os\data_logs\2015-06-23\21.12.08.bin");

            /*var data = new byte[] { 1, 3, 3, 7 };
            DataLogger.SetBinaryFilename(@"C:\cygwin64\home\Nicco\os\data_logs\2015-06-23\21.12.08.bin");
            DataLogger.Log(data, data.Length);

            data = new byte[] { 9, 8, 7, 6 };
            DataLogger.Log(data, data.Length);

            data = new byte[] { 13, 53, 198, 75, 21, 142, 33 };
            DataLogger.Log(data, data.Length);

            return;*/
            try
            {
                SubMain(args);
            }
            catch (ArgumentNotProvidedException ex)
            {
                Logger.Log(LogType.Error, "Argument was not provided: {0}", ex.Argument);
            }
            catch (MessageException msg)
            {
                Logger.Log(LogType.Error, msg.Message);
            }

            if (!string.IsNullOrWhiteSpace(originalConsoleTitle))
                Console.Title = originalConsoleTitle;
        }

        class ArgumentNotProvidedException : Exception
        {
            public string Argument { get; private set; }

            public ArgumentNotProvidedException(string argument)
            {
                Argument = argument;
            }
        }

        class MessageException : Exception
        {
            public MessageException(string msg) :
                base(msg)
            {
            }

            public MessageException(string format, params object[] args) :
                this(string.Format(format, args))
            {
            }
        }
    }
}
