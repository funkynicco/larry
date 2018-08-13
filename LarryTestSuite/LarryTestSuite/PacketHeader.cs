using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Larry
{
    internal enum PacketHeader : short
    {
        /// <summary>
        /// Keep connection alive.
        /// </summary>
        Ping = 0x0001,
        /// <summary>
        /// Send username and password
        /// </summary>
        Authorize = 0x0002,
        /// <summary>
        /// Store a file.
        /// </summary>
        Store = 0x0003,
        /// <summary>
        /// The transmission was completed.
        /// </summary>
        TransmitComplete = 0x0004,
        DoBuild = 0x0008,
        BuildResultFile = 0x0010
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal class PacketAttribute : Attribute
    {
        public PacketHeader Header { get; private set; }

        public PacketAttribute(PacketHeader header)
        {
            Header = header;
        }
    }
}
