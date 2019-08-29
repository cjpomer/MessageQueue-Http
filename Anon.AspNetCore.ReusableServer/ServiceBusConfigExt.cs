using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Configuration;

namespace Anon.AspNetCore.ReusableServer
{
    public static class ServiceBusConfigExt
    {
        public static ServiceBusConnectionStringBuilder GetServiceBusConnectionStringBuilder(this IConfiguration config)
        {
            return new ServiceBusConnectionStringBuilder(
                config.GetValue("ServiceBus:Endpoint", string.Empty),
                config.GetValue("ServiceBus:EntityPath", string.Empty),
                config.GetValue("ServiceBus:SharedAccessKeyName", string.Empty),
                config.GetValue("ServiceBus:SharedAccessKey", string.Empty));
        }
    }
}
