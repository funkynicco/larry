namespace Larry.Network
{
    public interface IClient
    {
        int Send(byte[] data, int offset, int length);
    }
}
