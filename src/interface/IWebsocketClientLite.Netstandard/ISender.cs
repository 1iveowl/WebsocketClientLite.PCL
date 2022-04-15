using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IWebsocketClientLite.PCL
{
    public interface ISender
    {
        Task SendTextAsync(string message, CancellationToken ct = default);
        Task SendTextAsync(string[] messageList, CancellationToken ct = default);
        Task SendTextAsync(string message, OpcodeKind opcode, FragmentKind fragment = FragmentKind.None, CancellationToken ct = default);
        Task SendPingWithText(string message, CancellationToken ct = default);
        Task SendPing(string message, CancellationToken ct = default);
        Task SendPong(string message, CancellationToken ct = default);
    }
}
