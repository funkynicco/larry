using Larry.File;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Larry.Network
{
    public partial class BuildServer : NetworkServerBase
    {
        class UserClient : IDisposable
        {
            public Client NetworkClient { get; private set; }
            public DateTime LastActivity { get; set; }
            public bool IsAuthorized { get; set; }
            public MemoryStream Buffer { get; private set; }

            public FileTransmission FileTransmit { get; set; }
            public FileTransmitDirection CurrentTransmitDirection { get; set; }

            public UserClient(Client networkClient)
            {
                NetworkClient = networkClient;
                LastActivity = DateTime.UtcNow;
                Buffer = new MemoryStream(1024);
                CurrentTransmitDirection = FileTransmitDirection.None;
            }

            public void Dispose()
            {
                if (FileTransmit != null)
                {
                    FileTransmit.Dispose();
                    FileTransmit = null;
                }
            }

            public void Disconnect()
            {
                NetworkClient.Disconnect();
            }

            public void Disconnect(string reason)
            {
                NetworkClient.Disconnect(reason);
            }

            public NetworkPacket CreatePacket(int requestId, PacketHeader header)
            {
                return NetworkPacket.Create(NetworkClient, requestId, header);
            }
        }

        class PacketMethod
        {
            public MethodInfo Method { get; private set; }

            public PacketHeader Header { get; private set; }

            public bool SkipAuthorization { get; private set; }

            public PacketMethod(MethodInfo method, PacketHeader header, bool skipAuthorization)
            {
                Method = method;
                Header = header;
                SkipAuthorization = skipAuthorization;
            }
        }

        private readonly Dictionary<PacketHeader, PacketMethod> _packetMethods = new Dictionary<PacketHeader, PacketMethod>();
        private int _nextCheckActivity = 0;

        public BuildServer()
        {
            foreach (var mi in GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                var attribute = mi.GetCustomAttribute<PacketAttribute>();
                if (attribute != null)
                {
                    var skipAuthorization = mi.GetCustomAttribute<SkipAuthorizationAttribute>() != null;

                    _packetMethods.Add(attribute.Header, new PacketMethod(mi, attribute.Header, skipAuthorization));

                    //Logger.Log(LogType.Debug, "Binding method '{0}' to packet '{1}'", mi.Name, attribute.Header);
                }
            }
        }

        protected override void OnClientConnected(Client client)
        {
            client.Tag = new UserClient(client);

            Logger.Log(LogType.Debug, "[{0}-{1}] Client connected", client.IP, client.SocketHandle.ToInt32());
        }

        protected override void OnClientDisconnected(Client client)
        {
            if (!string.IsNullOrEmpty(client.DisconnectReason))
                Logger.Log(LogType.Warning, "[{0}-{1}] Client kicked: {2}", client.IP, client.SocketHandle.ToInt32(), client.DisconnectReason);
            else
                Logger.Log(LogType.Debug, "[{0}-{1}] Client disconnected", client.IP, client.SocketHandle.ToInt32());
            (client.Tag as UserClient).Dispose();
        }

        protected override void OnClientData(Client client, byte[] buffer, int length)
        {
            var userClient = client.Tag as UserClient;

            userClient.LastActivity = DateTime.UtcNow;

            //Utilities.DumpData(buffer, length);

            if (userClient.FileTransmit != null &&
                userClient.CurrentTransmitDirection == FileTransmitDirection.Receive)
            {
                userClient.LastActivity = DateTime.UtcNow;
                if (userClient.Buffer.Length > 0)
                {
                    OnClientData(client, userClient.Buffer.GetBuffer(), (int)userClient.Buffer.Length);
                    userClient.Buffer.Position = 0;
                    userClient.Buffer.SetLength(0);
                }

                // we're currently receiving a file
                int toWrite = (int)Math.Min(length, userClient.FileTransmit.Remaining);
                userClient.FileTransmit.Write(buffer, 0, toWrite);

                //Logger.Log(LogType.Debug, "Wrote {0} bytes - Remaining {1}", toWrite, userClient.FileTransmit.Remaining);

                if (userClient.FileTransmit.Remaining == 0)
                {
                    userClient.FileTransmit.EndReceive();

                    Logger.Log(LogType.Debug, "File complete: {0}", userClient.FileTransmit.RemotePath);
                    if (userClient.FileTransmit.IsFileCorrupted)
                        Logger.Log(LogType.Error, "ERR FILE CORRUPTED => {0}", userClient.FileTransmit.RemotePath);

                    userClient.FileTransmit.Dispose();
                    userClient.FileTransmit = null;
                    userClient.CurrentTransmitDirection = FileTransmitDirection.None;

                    userClient.CreatePacket(0, PacketHeader.TransmitComplete).Send(); // notify transmit completed

                    if ((length - toWrite) > 0)
                    {
                        var newBuffer = new byte[length - toWrite];
                        Buffer.BlockCopy(buffer, toWrite, newBuffer, 0, length - toWrite);
                        OnClientData(client, newBuffer, newBuffer.Length); // read the remaining data
                    }
                }

                return;
            }

            //Logger.Log(LogType.Debug, "[{0}-{1}] Data: {2} bytes", client.IP, client.SocketHandle.ToInt32(), length);

            userClient.Buffer.Position = userClient.Buffer.Length;
            userClient.Buffer.Write(buffer, 0, length);

            ReadPacketResult result;
            do
            {
                userClient.Buffer.Position = 0;

                int requestId;
                short header;
                int dataSize;

                switch (result = Utilities.ReadHeader(userClient.Buffer, out requestId, out header, out dataSize))
                {
                    case ReadPacketResult.InvalidData:
                    case ReadPacketResult.DataSizeInvalid:
                    case ReadPacketResult.InvalidHeader:
                    case ReadPacketResult.UnexpectedHeaderAtThisPoint:
                        Logger.Log(
                            LogType.Warning,
                            "[{0}-{1}] ReadHeader: {2}",
                            client.IP,
                            client.SocketHandle.ToInt32(),
                            Enum.GetName(typeof(ReadPacketResult), result));
                        Utilities.DumpData(buffer, length);
                        client.Disconnect();
                        return;
                    case ReadPacketResult.NeedMoreData:
                        return; // exit out of OnClientData and wait for more data...
                }

                // call packet methods
                PacketMethod packetMethod;
                if (!_packetMethods.TryGetValue((PacketHeader)header, out packetMethod))
                {
                    Logger.Log(
                           LogType.Warning,
                           "[{0}-{1}] Unknown packet header: 0x{2}",
                           client.IP,
                           client.SocketHandle.ToInt32(),
                           header.ToString("x4"));
                    client.Disconnect();
                    return;
                }

                if (!packetMethod.SkipAuthorization &&
                    !userClient.IsAuthorized)
                {
                    Logger.Log(
                           LogType.Warning,
                           "[{0}-{1}] Unauthorized packet: 0x{2}",
                           client.IP,
                           client.SocketHandle.ToInt32(),
                           header.ToString("x4"));
                    client.Disconnect();
                    return;
                }

                try
                {
                    packetMethod.Method.Invoke(this, new object[] { userClient, requestId });
                }
                catch (DataValidationException ex)
                {
                    client.Disconnect(ex.Message);
                }

                if (client.IsDisconnect)
                    return;

                userClient.Buffer.Delete(NetworkStandards.HeaderSize + dataSize);

                if (userClient.Buffer.Length == 0)
                    break;
            }
            while (result == ReadPacketResult.Succeeded);
        }

        public override void Process()
        {
            base.Process();

            int tick = Environment.TickCount;
            if (tick >= _nextCheckActivity)
            {
                _nextCheckActivity = tick + 1000;

                var now = DateTime.UtcNow;

                foreach (var client in Clients)
                {
                    var userClient = client.Tag as UserClient;

                    int inactivityPeriodMax = userClient.IsAuthorized ? 60 : 15;

                    if ((now - userClient.LastActivity).TotalSeconds >= inactivityPeriodMax)
                    {
                        Logger.Log(
                            LogType.Warning,
                            "Disconnecting {0}-{1} due to {2} seconds inactivity.",
                            client.IP,
                            client.SocketHandle.ToInt32(),
                            inactivityPeriodMax);

                        client.Disconnect();
                    }
                }
            }

            foreach (var client in Clients)
            {
                var userClient = client.Tag as UserClient;

                if (userClient.FileTransmit != null &&
                    userClient.CurrentTransmitDirection == FileTransmitDirection.Send &&
                    userClient.FileTransmit.IsTransmitting)
                {
                    var buffer = new byte[1048576];
                    int toSend = (int)Math.Min(userClient.FileTransmit.Remaining, buffer.Length);

                    int numberOfBytesRead = 0;
                    while (numberOfBytesRead < toSend)
                        numberOfBytesRead += userClient.FileTransmit.Read(buffer, 0, toSend - numberOfBytesRead);

                    int numberOfBytesSent = 0;
                    while (numberOfBytesSent < toSend)
                        numberOfBytesSent += userClient.NetworkClient.Send(buffer, 0, toSend - numberOfBytesSent);

                    userClient.LastActivity = DateTime.UtcNow;

                    if (userClient.FileTransmit.Remaining == 0)
                    {
                        Logger.Log(LogType.Debug, "Transmit completed of {0}", userClient.FileTransmit.RemotePath);

                        userClient.FileTransmit.EndTransmit();
                        userClient.FileTransmit.Dispose();
                        userClient.FileTransmit = null;
                        userClient.CurrentTransmitDirection = FileTransmitDirection.None;
                    }
                }
            }
        }
    }
}
