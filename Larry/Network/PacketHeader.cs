﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Larry.Network
{
    internal enum PacketHeader : short
    {
        /// <summary>
        /// Keep connection alive.
        /// </summary>
        Ping = 1,
        /// <summary>
        /// Send username and password
        /// </summary>
        Authorize,
        /// <summary>
        /// Store a file.
        /// </summary>
        Store,
        /// <summary>
        /// The transmission was completed.
        /// </summary>
        TransmitComplete,
        DoBuild,
        BuildResultFile,

        RequestFileList,
        FileList,

        TransferFileRequest,
        TransferFileRequestResponse,
        RequestFileResponse
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

    /// <summary>
    /// Skips the authorization of a packet method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal class SkipAuthorizationAttribute : Attribute
    {        
    }
}
