using Larry.File;
using Larry.Network;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Larry
{
    class Program
    {
        const int SleepTime = 30;

        static void SubMain(string[] args)
        {
            Logger.Log(LogType.Normal, "Platform: " + Environment.OSVersion.Platform);

            using (var ms = new MemoryStream(1024))
            {
                ms.Write(1337);
                ms.Write(911);
                ms.Position = 0;
                Debug.Assert(ms.ReadInt32() == 1337);
                Debug.Assert(ms.ReadInt32() == 911);
            }

            using (var directoryChanger = new DirectoryChanger()) // DirectoryChanger automatically resets the working directory upon Dispose
            {
                int port = 11929;
                bool isServer = false;
                string address = string.Empty;
                string username = string.Empty;
                string password = string.Empty;
                int paramIndex = 0;

                for (int i = 0; i < args.Length; ++i)
                {
                    if (args[i] == "--server")
                    {
                        isServer = true;
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
                    else
                    {
                        switch (paramIndex++)
                        {
                            case 0:
                                address = args[i];
                                break;
                            /*case 1:
                                localPath = args[i];
                                break;
                            case 2:
                                remotePath = args[i];
                                break;*/
                            default:
                                throw new MessageException("Too many arguments provided.");
                        }
                    }
                }

                if (isServer)
                {
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

                // run client
                using (var client = new BuildClient())
                {
                    if (client.Connect(address, port))
                    {
                        client.AddFileTransmission(FileTransmission.CreateFromFile("output\\myos.bin", "isodir\\boot\\myos.bin")); // path is normalized in linux
                        client.AddFileTransmission(FileTransmission.CreateFromFile("grub.cfg", "isodir\\boot\\grub\\grub.cfg"));

                        while (!client.IsFinished)
                        {
                            client.Process();
                            Thread.Sleep(SleepTime);
                        }
                    }
                    else
                        Logger.Log(LogType.Error, "Failed to connect to {0}:{1}", address, port);
                }
            }
        }

        static void Main(string[] args)
        {
            var title = Console.Title;

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

            Console.Title = title;
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
