using Microsoft.Extensions.Configuration;

namespace Anon.AspNetCore.ServiceBusProtocolTransition
{
    internal static class QueueClientExt
    {
        internal static int GetNumTasks(this IConfiguration config)
        {
            return config.GetValue("queue-client:num-tasks", 1);
        }
    }
}
