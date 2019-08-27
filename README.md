# Anon.AspNetCore.ServiceBusProtocolTransition
Proof of concept to examine transitioning from a message queue to an HTTP server

#### Prerequisite
Install latest preview of netcore 3 SDK (preview 8)
Enable preview versions in Tools -> Options -> Environment -> Preview Features.  Required to enable preview SDK and runtime.
Include Prerelease nuget to target preview aspnet core

#### What
After years of experience writing MVC code in various versions of ASP.NET, I have become a bit enamored with the simple approach of routing to controllers. I have moved to aspnet core, and spent some time writing [custom middleware](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/?view=aspnetcore-3.0) and [filters](https://docs.microsoft.com/en-us/aspnet/core/mvc/controllers/filters?view=aspnetcore-3.0).  In both cases I found the experience very well considered and implemented. It comes as no surprise as there have been very smart people working on it for quite some time now.

For the last few years, we have spent some time developing cloud-based apps to manage data.  One issue we have addressed several times is the hybrid nature of our users and their data.  They all live on-prem, and most of their data does too. Given that we employ armies of analsysts to fortify our network, sometimes expatriating data from our WAN to our cloud requires [Argo](https://www.imdb.com/title/tt1024648/ "great movie")-like planning.  Often though, the simplicity-to-performance analysis makes it possible to simply use HTTP-based SDK's from our favorite cloud vendor and we can accept the coefficient of friction that is "modern" enterprise cybersecurity. (In a nutshell, only HTTP packets gets through, but they get sliced and diced and analyzed so much that compounding latencies add up to significant performance degradations.)

With our apps in the cloud, we use HTTP-based message queues to communicate to services on-prem that either upload or download data.  We have written enough services to handle this very workflow that we are interested in a reusable service.  As I considered the design of such a reusable service, I began to realize that I would appreciate a lot of the things I love in aspnet core.  That began a chain of thoughts and [hacky internet research](https://www.google.com/search?rlz=1C1GCEB_enUS833US833&q=message+queue+vs+rest&spell=1&sa=X&ved=0ahUKEwiM_bC1sY7kAhUHjq0KHY2fCyQQBQguKAA&biw=1920&bih=969) about the nature of message queues vs. HTTP requests.  It turns out that many of the semantics are very similar, and often it comes down to whether you want to push or to pull. Push/pull is another [hot topic](https://angular.io/guide/rx-library).

So. If I consider writing code that handles messages pulled from a queue/topic semantically-similar to a controller handling an HTTP request, and I already wanted middleware and filters, a reusable message queue solution might actually BE aspnet core.  So I took a look.  At first, there was an initial excitement because they are currently abstracting some of the framework out of the pure HTTP-processing pipeline.  My first thought would be to process message queue messages rather than HTTP requests.  They have created the [Generic Host](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-3.0) that allows us to consume logging and dependency injection in an application that doesn't otherwise process HTTP.  Unfortunately, middleware and filters are still tightly coupled to HTTP processing, and the amount of work it would take to de-couple and extract them out is not feasible and the resulting product wouldn't be compatible with aspnet-core at that point anyway.  Perhaps that will be future work the in which the framework developers invest.

#### Protocol Transitioning
The next best thing is a form of protocol transitioning.  Generally that term is used to describe transitioning between authentication protocols.  I need to transition between a message queue protocol and HTTP.  That leaves two options that I can think of.
1. Message queue reverse proxy (the topic of this article)
2. Custom implementation of `Microsoft.AspNetCore.Hosting.Server.IServer` (the topic of a future article)

A message queue reverse proxy would pull messages out of a queue, wrap them up in an HTTP request, and forward the HTTP request on an aspnet core server.  Sounds fun, right?  Let's start with the server itself.  Now that we know about the `GenericHost`, we will of course use it.

#### `Program.cs`
```
using Microsoft.Azure.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anon.AspNetCore.ServiceBusProtocolTransition
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
            .ConfigureServices((hostContext, services) =>
            {
                services.AddLogging()
                    .AddScoped<IQueueClient, TestQueueClient>()
                    .AddHostedService<ServiceBusReverseProxyService>();
            });
    }
}
```
This is the entry point.  It should look familiar to anyone that's peeked at a `Program.cs` file genearated by aspnet core. The main difference is that it does not lead to conventional methods in `Startup.cs` being invoked - that happens when you call `WebHost.CreateDefaultBuilder(args).UseStartup<Startup>()`. Note the difference between `Host` and `WebHost`. Also recall that `Startup` conventionally registers services and builds up middleware.  Since middleware is still coupled to HTTP processing, and this is the Generic Host, `Startup` is out.  So we just register our services in `Program.cs`.

#### `TestQueueClient`
```
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using Newtonsoft.Json;
using Anon.ServiceBus;
using Microsoft.Extensions.Configuration;

namespace Anon.AspNetCore.ServiceBusProtocolTransition
{
    internal class TestQueueClient : IQueueClient
    {
        /** bunch of IQueueClient stuff **/
        
		public TestQueueClient(IConfiguration config)
        {
            Task.Run(() => Parallel.For(0, config.GetNumTasks(), async i =>
            {
                Message message;
                int j = 0;
                var rng = new Random();
                do
                {
                    await Task.Delay(rng.Next(config.GetForwardingTimeout().Milliseconds));
                    Func<Message, CancellationToken, Task> h;
                    lock (this)
                    {
                        h = this.handler;
                    }
                    if (h != null)
                    {
                        var request = new HttpRequestBuilder()
                            .AddQuery("task", i.ToString())
                            .AddQuery("n", j.ToString())
                            .SetHost("/")
                            .SetMethod("get")
                            .SetPath("/api/values")
                            .Build();
                        message = new Message(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request)));
                        await h(message, CancellationToken.None);
                        j++;
                    }
                }
                while (!source.IsCancellationRequested);
            }));
        }
        public Task CloseAsync()
        {
            lock (this)
            {
                this.handler = null;
                this.exceptionHandler = null;
            }
            source.Cancel();
            return Task.CompletedTask;
        }
        public void RegisterMessageHandler(Func<Message, CancellationToken, Task> handler, MessageHandlerOptions messageHandlerOptions)
        {
            lock (this)
            {
                this.handler = handler;
            }
        }
	}
}
```
Since this is a proof of concept, I'm not going to go throught the bother of setting up a ServiceBus Queue and instead I'm going to mock up a `TestQueueClient`.  It does not do much. Almost all of the implementation simply throws `NotImplementedExcetion`. 

The meat of the logic here in the ctor uses `Parallel.For` to create N `Task`s where N = prefetch count.  You can ignore the prefetch count for now, that's for tuning I want to do later, when I need to actually upload large files and want to divide and conquer the upload request into chunks.

Each task creates a `Microsoft.Azure.ServiceBus.Message`, passing the `Message` to the registered message handler (a `Func<Message, CancellationToken, Task>` that is passed to the `IQueueClient` via the `RegisterMessageHandler` method). Handling a message takes a pseudo-random amount of time that does not exceed 1 second.  We also pass a task identifier, `i`, and a message identifier, `j`, by appending them to the query string. 

The `Message` contains a json-serialized `Anon.ServiceBus.HttpRequest`.  At this point we are simply pretending that someone, somewhere put an HTTP request into a service bus.  That way we don't have to agree on another protocol for the message queue message.

#### `HttpRequest`
The `HttpRequest` is designed to model a very basic HTTP requests per the RFC spec. It is not a sophisticated model and contains no logic that would help assure valid HTTP requests. 
```
namespace Anon.ServiceBus
{
	public struct HttpRequest
	{
		public byte[] Body { get; set; }
		public (string, string)[] Headers { get; set; }
		public string Host { get; set; }
		public string Method { get; set; }
		public string Path { get; set; }
		public string Query { get; set; }
	}
}
```

Some small helpful logic is supplied by the `HttpRequestBuilder`, which could serve as a place to invest future effort to assure valid HTTP requests.

#### So Why Didn't You Use Framework Classes for Modeling HTTP Requests?
The .NET frameworks are chalk-full of HTTP-handling code.  Most recently, netcore brought the `HttpClient`, which has an associated `HttpRequestMessage` that would seemingly model an HTTP request expertly. The new client will call `HttpWebRequest` if run on [.NET Framework or Mono](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpclient?view=netframework-4.8 "In the Remarks section"). The `HttpWebRequest` also fully models a proper HTTP request.  In both cases (and at least one other I found in aspnet core source) the actual serialization that is done to put the request on the wire is hidden from consumers, and serializing to json would have required custom code anyway. So for now I did what seems easiest.

#### `ServiceBusReverseProxyService`
The meat of the logic occurs in `HandleMessage(Message message, CancellationToken token)` but let's take a look at how it starts up. We inject the `IQueueClient`, a nice aspnet core `ILogger`, and the aspnet core `IConfiguration`. `StartAsync` simply registers a message handler and `StopAsync` just call through to the `IQueueClient`
```
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
    //...

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
```

Let's take a look at the message handler.
```
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
        uriBuilder.Scheme = "https";
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
```
First we unwrap the request, de-encoding the bytes and then deserializing the json. Then we map the `Anon.ServiceBus.HttpRequest` to a `System.Net.Http.HttpRequestMessage` and send it through the `HttpClient`. There is just enough logging to be able to see when something goes wrong, other than that exceptions are ignored.

#### Conclusion - What Have We Wrought?
We have examined the concept of forwarding messages from a message queue to an HTTP server and proven the concept using ASP.NET Core.  We have developed a generic host that reads messages from a mock implementation of the Azure ServiceBus `IQueueClient` and forwards the message on to an ASP.NET Core web server running on the same localhost. We have shown that a custom controller can then respond to the request.
#### What's next?
1. Examine an alternate approach to forwarding messages by implementing an ASP.NET Core `IServer` that listens to a message queue rather than binding to a network port and listenting for HTTP connections
2. Given a request sent over a message queue, implement a set of ASP.NET Core controllers that upload specified files efficiently  