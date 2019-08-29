using Microsoft.Extensions.Configuration;

namespace Anon.AspNetCore.ReusableServer
{
    internal static class LanControllerExt
    {
        public static long GetBytesPerMessage(this IConfiguration config)
        {
            return config.GetValue("LanController:BytesPerMessage", 1024*1024); //1 MiB
        }

        public static int GetMessagesPerSend(this IConfiguration config)
        {
            return config.GetValue("LanController:MessagesPerSend", 10);
        }
    }
}
