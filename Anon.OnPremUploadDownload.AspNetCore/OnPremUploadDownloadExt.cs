using Microsoft.Extensions.Configuration;

namespace Anon.OnPremUploadDownload.AspNetCore
{
    internal static class OnPremUploadDownloadExt
    {
        public static long GetBytesPerMessage(this IConfiguration config)
        {
            return config.GetValue("OnPremUploadDownload:BytesPerMessage", 1024*1024); //1 MiB
        }

        public static int GetMessagesPerSend(this IConfiguration config)
        {
            return config.GetValue("OnPremUploadDownload:MessagesPerSend", 10);
        }
    }
}
