using System;
using System.Text;
using WebsocketClientLite.CustomException;
using Protocol = WebsocketClientLite.VerifiedProtocol.Protocol;

namespace WebsocketClientLite.Helper;

internal static class ProtocolPayload
{
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void ValidateText(byte[] bytes)
    {
        try { StrictUtf8.GetCharCount(bytes); }
        catch (DecoderFallbackException ex)
        {
            throw new WebsocketClientLiteException("Protocol error: invalid UTF-8 text.", ex);
        }
    }

    internal static void ValidateClose(byte[] bytes)
    {
        ushort code = bytes.Length >= 2 ? (ushort)((bytes[0] << 8) | bytes[1]) : (ushort)0;
        bool utf8 = true;
        if (bytes.Length >= 2)
        {
            try { StrictUtf8.GetCharCount(bytes, 2, bytes.Length - 2); }
            catch (DecoderFallbackException) { utf8 = false; }
        }
        if (!Protocol.CloseValid((uint)bytes.Length, code, utf8))
            throw new WebsocketClientLiteException("Protocol error: invalid Close payload.");
    }
}
