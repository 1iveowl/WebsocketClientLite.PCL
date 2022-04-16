using System.IO;

namespace IWebsocketClientLite.PCL
{
    public interface IDataframe
    {
        string Message { get; }

        byte[] Binary { get; }

        PayloadBitLengthKind PayloadBitLength { get;}

        ulong Length { get;}
    }
}
