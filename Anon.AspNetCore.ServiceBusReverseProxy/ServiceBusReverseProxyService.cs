using Anon.ServiceBus;
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Anon.AspNetCore.ServiceBusProtocolTransition
{
    public class ServiceBusReverseProxyService : IHostedService
    {
        private readonly IQueueClient queueClient;
        private readonly ILogger logger;
        private readonly IConfiguration config;
        private readonly HttpClient httpClient;

        public ServiceBusReverseProxyService(IQueueClient queueClient, ILogger<ServiceBusReverseProxyService> logger, IConfiguration config)
        {
            this.queueClient = queueClient;
            this.logger = logger;
            this.config = config;
            httpClient = new HttpClient();
            httpClient.Timeout = config.GetForwardingTimeout();
        }

        private async Task HandleMessage(Message message, CancellationToken token)
        {
            var request = JsonConvert.DeserializeObject<HttpRequest>(Encoding.UTF8.GetString(message.Body));

            var requestMessage = new HttpRequestMessage();
            foreach (var header in request.Headers) { requestMessage.Headers.Add(header.Item1, header.Item2); }
            requestMessage.Method = request.Method switch
            {
                "get" => HttpMethod.Get,
                "head" => HttpMethod.Head,
                "post" => HttpMethod.Post,
                "put" => HttpMethod.Put,
                "delete" => HttpMethod.Delete,
                //"connect" => HttpMethod.Connect,
                "options" => HttpMethod.Options,
                "trace" => HttpMethod.Trace,
                "patch" => HttpMethod.Patch,
                _ => throw new InvalidOperationException($"invalid http method {request.Method}")
            };
            var uriBuilder = new UriBuilder();
            uriBuilder.Host = config.GetForwardingHost();
            uriBuilder.Port = config.GetForwardingPort();
            uriBuilder.Path = request.Path;
            uriBuilder.Query = request.Query;
            requestMessage.RequestUri = uriBuilder.Uri;

            HttpResponseMessage response = null;
            string log = $"{DateTime.UtcNow.ToString("u")} host={config.GetForwardingHost()} port={config.GetForwardingPort()} path={request.Path} query={request.Query}";
            try
            {
                response = await httpClient.SendAsync(requestMessage);
            }
            catch (Exception ex)
            {
                logger.LogInformation($"{log} error={ex.GetType().Name}");
                logger.LogError(ex.ToString());
            }

            logger.LogInformation($"{log} status={response?.StatusCode.ToString() ?? "none"}");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            queueClient.RegisterMessageHandler(HandleMessage, args => Task.CompletedTask);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await queueClient.CloseAsync();
        }
    }
}
