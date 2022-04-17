using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IWebsocketClientLite.PCL;
using WebsocketClientLite.PCL.CustomException;
using WebsocketClientLite.PCL.Model;
using WebsocketClientLite.PCL.Service;

namespace WebsocketClientLite.PCL.Helper
{
    internal static class DataframeParsing
    {
        internal static async Task<Dataframe> CreateDataframe(TcpConnectionService tcpConnection, CancellationToken ct)
        {
            var dataframe = new Dataframe(tcpConnection, ct);

            var byteArray = await dataframe.GetNextBytes(1);
            Debug.WriteLine($"First byte: {byteArray[0]}");

            if (byteArray is null)
            {
                return null;
            }

            var bits = new BitArray(byteArray);

            return dataframe with
            {
                FIN = bits[7],
                RSV1 = bits[6],
                RSV2 = bits[5],
                RSV3 = bits[4],
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
                // When encoded on the wire, the most significant bit is the leftmost in the ABNF
                // https://datatracker.ietf.org/doc/html/rfc6455#section-5.2
                var opcodeBits = new BitArray(new[] { bits[0], bits[1], bits[2], bits[3]});

                var opcode = new byte[1];
                opcodeBits.CopyTo(opcode, 0);

                Debug.WriteLine($"Opcode: {(OpcodeKind)opcode[0]}");

                return opcode[0];
            }
        }

        internal static async Task<Dataframe> PayloadBitLenght(this Task<Dataframe> dataframeTask)
        {
            var dataframe = await dataframeTask;

            if (dataframe is null)
            {
                return null;
            }

            var bytes = (await dataframe.GetNextBytes(1));

            var bits = new BitArray(bytes);

            var @byte = bytes[0];

            if (@byte <= (byte)PayloadBitLengthKind.Bits8)
            {
                return dataframe with 
                {
                    MASK = bits[7],
                    Length = @byte, 
                    PayloadBitLength = PayloadBitLengthKind.Bits8 
                };
            }
            if (@byte == (byte)PayloadBitLengthKind.Bits16)
            {
                return dataframe with 
                {
                    MASK = bits[7],
                    PayloadBitLength = PayloadBitLengthKind.Bits16 
                };
            }
            if (@byte >= (byte)PayloadBitLengthKind.Bits64)
            {
                return dataframe with 
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

        internal static async Task<Dataframe> PayloadLenght(this Task<Dataframe> dataframeTask)
        {
            var dataframe = await dataframeTask;

            if (dataframe is null)
            {
                return null;
            }

            switch (dataframe.PayloadBitLength)
            {
                case PayloadBitLengthKind.Bits8:
                    return dataframe;
                case PayloadBitLengthKind.Bits16:
                    {
                        var bytes = (await dataframe.GetNextBytes(2)).Reverse().ToArray();
                        return dataframe with { Length = BitConverter.ToUInt16(bytes, 0) };
                    }

                case PayloadBitLengthKind.Bits64:
                    {
                        var bytes = (await dataframe.GetNextBytes(8)).Reverse().ToArray();
                        return dataframe with { Length = BitConverter.ToUInt64(bytes, 0) };
                    }
                default:
                    throw new WebsocketClientLiteException("Unspecfied payload lenght.");
            }
        }

        internal static async Task<Dataframe> GetPayload(this Task<Dataframe> dataframeTask)
        {
            var dataframe = await dataframeTask;

            if (dataframe is null)
            {
                return null;
            }

            if (dataframe.Length > 0)
            {
                var memoryStream = new MemoryStream();

                await memoryStream.WriteAsync(await dataframe.GetNextBytes(dataframe.Length));

                if (dataframe.MASK)
                {

                    return dataframe with
                    {
                        MaskingBytes = (await dataframe.GetNextBytes(4)).Reverse().ToArray(),
                        DataStream = memoryStream,
                    };
                }
                else
                {
                    return dataframe with
                    {
                        DataStream = memoryStream
                    };
                }
            }

            return dataframe;
        }
    }
}
