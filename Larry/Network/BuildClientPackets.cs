using System;

namespace Larry.Network
{
    public partial class BuildClient
    {
        [Packet(PacketHeader.Authorize)]
        void OnAuthorize()
        {
            _nextSendPing = Environment.TickCount + 10000;
            _isAuthorized = true;

            // if client received this then we are good to store the file
            Logger.Log(LogType.Debug, "Successfully authorized with build server.");
        }

        [Packet(PacketHeader.Store)]
        void OnStoreFile()
        {
            byte result = (byte)_buffer.ReadByte();
            if (result != 0)
            {
                Logger.Log(LogType.Warning, "Transmit denied. Code: {0}", result);

                _currentFileTransmission.Dispose();
                _currentFileTransmission = null;
                return;
            }

            Logger.Log(LogType.Debug, "Begin transmit of {0} ({1} bytes)", _currentFileTransmission.LocalPath, _currentFileTransmission.FileSize);
            _currentFileTransmission.BeginTransmit();
        }

        [Packet(PacketHeader.TransmitComplete)]
        void OnTransmitCompleted()
        {
            //Program.QueryShutdown = true;

            if (_fileTransmissionQueue.Count == 0 &&
                _currentFileTransmission == null)
            {
                // send build
                CreatePacket(PacketHeader.DoBuild)
                    .Send();
            }
        }
    }
}
