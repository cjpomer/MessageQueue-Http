using Microsoft.Extensions.Configuration;
using System;

namespace Anon.OnPremUploadDownlaod.ServiceBusReverseProxy
{
    internal static class ServiceBusProxyExt
    {
        internal static string GetForwardingHost(this IConfiguration config)
        {
            return config.GetValue("servicebus-reverseproxy:forwarding-host", "http://localhost");
        }
        internal static int GetForwardingPort(this IConfiguration config)
        {
            return config.GetValue("servicebus-reverseproxy:forwarding-port", 5000);
        }
        internal static TimeSpan GetForwardingTimeout(this IConfiguration config)
        {
            var time_ms = config.GetValue("servicebus-reverseproxy:forwarding-timeout-ms", 120000);
            return TimeSpan.FromMilliseconds(time_ms);
        }
    }
}
