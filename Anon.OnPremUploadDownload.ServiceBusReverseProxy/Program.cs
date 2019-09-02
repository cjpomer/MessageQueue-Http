using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anon.OnPremUploadDownlaod.ServiceBusReverseProxy
{
    class Program
    {
        static void Main(string[] args)
        {
            CreateHostBuilder(args)
                .Build()
                .Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
            .ConfigureServices((host, services) =>
            {
                services.AddLogging()
                    .AddScoped<ISubscriptionClient, SubscriptionClient>(isp =>
                        new SubscriptionClient(host.Configuration.GetServiceBusConnectionString(), host.Configuration.GetTopicName(), host.Configuration.GetSubscriptionName()))
                    .AddHostedService<ServiceBusReverseProxyService>();
            });
    }
}
