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

            public NetworkPacket CreatePacket(PacketHeader header)
            {
                return NetworkPacket.Create(NetworkClient, header);
            }
        }

        private readonly Dictionary<PacketHeader, MethodInfo> _packetMethods = new Dictionary<PacketHeader, MethodInfo>();
        private int _nextCheckActivity = 0;

        public BuildServer()
        {
            foreach (var mi in GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                var attribute = mi.GetCustomAttribute<PacketAttribute>();
                if (attribute != null)
                {
                    _packetMethods.Add(attribute.Header, mi);

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

            if (userClient.FileTransmit != null &&
                userClient.CurrentTransmitDirection == FileTransmitDirection.Receive)
            {
                // we're currently receiving a file
                int toWrite = (int)Math.Min(length, userClient.FileTransmit.Remaining);
                userClient.FileTransmit.Write(buffer, toWrite);

                //Logger.Log(LogType.Debug, "Wrote {0} bytes - Remaining {1}", toWrite, userClient.FileTransmit.Remaining);

                if (userClient.FileTransmit.Remaining == 0)
                {
                    userClient.FileTransmit.EndReceive();

                    Logger.Log(LogType.Debug, "File complete: {0}", userClient.FileTransmit.RemotePath);

                    userClient.FileTransmit.Dispose();
                    userClient.FileTransmit = null;
                    userClient.CurrentTransmitDirection = FileTransmitDirection.None;

                    userClient.CreatePacket(PacketHeader.TransmitComplete).Send(); // notify transmit completed

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

                short header;
                int dataSize;

                switch (result = Utilities.ReadHeader(userClient.Buffer, out header, out dataSize))
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

                MethodInfo methodInfo;
                if (!_packetMethods.TryGetValue((PacketHeader)header, out methodInfo))
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

                try
                {
                    methodInfo.Invoke(this, new object[] { userClient });
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

                    if ((now - userClient.LastActivity).TotalSeconds >= (userClient.IsAuthorized ? 60 : 15))
                    {
                        Logger.Log(
                            LogType.Warning,
                            "Disconnecting {0}-{1} due to 15 seconds inactivity.",
                            client.IP,
                            client.SocketHandle.ToInt32());

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
                    //userClient.FileTransmit.Write()
                    //userClient.NetworkClient.Send()
                }
            }
        }
    }
}
