using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Service;
using static WebsocketClientLite.PCL.Helper.WebsocketMasking;

namespace WebsocketClientLite.PCL.Helper
{
    internal static class DatagramParsing
    {
        internal static async Task<Datagram> CreateDatagram(TcpConnectionService tcpConnection, CancellationToken ct)
        {
            var datagram = new Datagram(tcpConnection, ct);

            var byteArray = (await datagram.GetNextBytes(1));
            var bits = new BitArray(byteArray);

            return datagram with
            {
                FIN = bits[0],
                RSV1 = bits[1],
                RSV2 = bits[2],
                RSV3 = bits[3],
                Opcode = (OpcodeKind)GetOpcode(),
                Fragment = byteArray[0] switch
                {
                    (byte)FragmentKind.First => FragmentKind.First,
                    (byte)FragmentKind.Last => FragmentKind.Last,
                    _ => FragmentKind.None
                }
            };

            byte GetOpcode()
            {
                var opcodeBits = new BitArray(8);

                // When encoded on the wire, the most significant bit is the leftmost in the ABNF
                // https://datatracker.ietf.org/doc/html/rfc6455#section-5.2
                opcodeBits[0] = bits[7];
                opcodeBits[1] = bits[6];
                opcodeBits[2] = bits[5];
                opcodeBits[3] = bits[4];

                var opcode = new byte[1];
                opcodeBits.CopyTo(opcode, 0);

                return opcode[0];
            }
        }

        internal static async Task<Datagram> PayloadBitLenght(this Task<Datagram> datagramTask)
        {
            var datagram = await datagramTask;

            var bytes = (await datagram.GetNextBytes(1));

            var bits = new BitArray(bytes);

            var @byte = bytes[0];

            if (@byte <= 125)
            {
                return datagram with 
                {
                    MASK = bits[7],
                    Length = @byte, 
                    PayloadBitLength = PayloadBitLengthKind.Bits8 
                };
            }
            if (@byte == 126)
            {
                return datagram with 
                {
                    MASK = bits[7],
                    PayloadBitLength = PayloadBitLengthKind.Bits16 
                };
            }
            if (@byte >= 127)
            {
                return datagram with 
                {
                    MASK = bits[7],
                    PayloadBitLength = PayloadBitLengthKind.Bits64 
                };
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        internal static async Task<Datagram> PayloadLenght(this Task<Datagram> datagramTask)
        {
            var datagram = await datagramTask;

            switch (datagram.PayloadBitLength)
            {
                case PayloadBitLengthKind.Bits8:
                    return datagram;
                case PayloadBitLengthKind.Bits16:
                    {
                        var bytes = (await datagram.GetNextBytes(2)).Reverse().ToArray();
                        return datagram with { Length = BitConverter.ToUInt16(bytes, 0) };
                    }

                case PayloadBitLengthKind.Bits64:
                    {
                        var bytes = (await datagram.GetNextBytes(8)).Reverse().ToArray();
                        return datagram with { Length = BitConverter.ToUInt64(bytes, 0) };
                    }
                default:
                    throw new WebsocketClientLiteException("Unspecfied payload lenght.");
            }
        }

        internal static async Task<Datagram> GetPayload(this Task<Datagram> datagramTask)
        {
            var datagram = await datagramTask;
            var memoryStream = new MemoryStream();

            await memoryStream.WriteAsync(await datagram.GetNextBytes(datagram.Length));     

            if (datagram.MASK)
            {

                return datagram with
                {
                    MaskingBytes = (await datagram.GetNextBytes(4)).Reverse().ToArray(),
                    DataStream = memoryStream,
                };
            }
            else
            {
                return datagram with
                {
                    DataStream = memoryStream
                };
            }
        }
    }
}
