using Anon.OnPremUploadDownload.Http;
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

namespace Anon.OnPremUploadDownlaod.ServiceBusReverseProxy
{
    public class ServiceBusReverseProxyService : IHostedService
    {
        private readonly ISubscriptionClient subscriptionClient;
        private readonly ILogger logger;
        private readonly IConfiguration config;
        private readonly HttpClient httpClient;

        public ServiceBusReverseProxyService(ISubscriptionClient subscriptionClient, ILogger<ServiceBusReverseProxyService> logger, IConfiguration config)
        {
            this.subscriptionClient = subscriptionClient;
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
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    await subscriptionClient.CompleteAsync(message.SystemProperties.LockToken);
                    log = $"{log} complete=true";
                }
                else
                {
                    await subscriptionClient.DeadLetterAsync(message.SystemProperties.LockToken);
                    log = $"{log} dead-letter=true";
                }
            }
            catch (Exception ex)
            {
                log = $"{log} error={ex.GetType().Name} error-correlation={message.SystemProperties.LockToken}";
                logger.LogError($"error-correlation={message.SystemProperties.LockToken} {ex.ToString()}");
            }
            finally { logger.LogInformation($"{log} status={response?.StatusCode.ToString() ?? "none"}"); }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            subscriptionClient.RegisterMessageHandler(HandleMessage, new MessageHandlerOptions(HandleError) { AutoComplete = false, MaxConcurrentCalls = config.GetMaxConcurrentCalls() });
            return Task.CompletedTask;
        }

        private Task HandleError(ExceptionReceivedEventArgs arg)
        {
            logger.LogError(arg.Exception.ToString());
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await subscriptionClient.CloseAsync();
        }
    }
}
