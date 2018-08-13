﻿namespace Larry.Network
{
    public static class NetworkStandards
    {
        public const byte HeaderPrefix = 0xd9;
        public const int HeaderSize = 11; // see Utilities.cs for header structure
        public const int MaxPacketLength = 1048576; // 1 mb
    }
}
