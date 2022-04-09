using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IWebsocketClientLite.PCL
{
    public interface ISender
    {
        Task SendTextAsync(string message, CancellationToken ct = default);
        Task SendTextAsync(string[] messageList, CancellationToken ct = default);
        Task SendTextAsync(string message, FrameType frameType, CancellationToken ct = default);
    }
}
