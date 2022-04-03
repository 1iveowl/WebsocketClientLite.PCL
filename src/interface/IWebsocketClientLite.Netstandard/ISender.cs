using System.IO;
using System.Threading.Tasks;

namespace IWebsocketClientLite.PCL
{
    public interface ISender
    {
        Task SendTextAsync(string message);
        Task SendTextAsync(string[] messageList);
        Task SendTextAsync(string message, FrameType frameType);
    }
}
