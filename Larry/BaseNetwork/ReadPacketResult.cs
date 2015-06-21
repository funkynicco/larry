namespace Larry.Network
{
    public enum ReadPacketResult
    {
        Succeeded,

        // these faults in packet causes disconnect
        InvalidData,
        DataSizeInvalid,
        InvalidHeader,
        UnexpectedHeaderAtThisPoint,

        // this stops the processing and waits for more data
        NeedMoreData
    }
}
