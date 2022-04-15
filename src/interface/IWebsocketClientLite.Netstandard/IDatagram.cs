using System.IO;

namespace IWebsocketClientLite.PCL
{
    public interface IDatagram
    {
        string Message { get; }

        byte[] Binary { get; }

        PayloadBitLengthKind PayloadBitLength { get;}

        ulong Length { get;}
    }
}
