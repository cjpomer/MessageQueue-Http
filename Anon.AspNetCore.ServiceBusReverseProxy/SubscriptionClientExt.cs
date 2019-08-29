using Microsoft.Extensions.Configuration;

namespace Anon.AspNetCore.ServiceBusProtocolTransition
{
    internal static class SubscriptionClientExt
    {
        internal static int GetMaxConcurrentCalls(this IConfiguration config)
        {
            return config.GetValue("subscription-client:max-concurrent-calls", 1);
        }
        internal static string GetServiceBusConnectionString(this IConfiguration config)
        {
            return config.GetValue("subscription-client:servicebus-connection-string", string.Empty);
        }
        internal static string GetTopicName(this IConfiguration config)
        {
            return config.GetValue("subscription-client:topic-name", string.Empty);
        }
        internal static string GetSubscriptionName(this IConfiguration config)
        {
            return config.GetValue("subscription-client:subscription-name", string.Empty);
        }
    }
}
