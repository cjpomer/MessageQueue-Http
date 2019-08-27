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
        private readonly CancellationTokenSource source = new CancellationTokenSource();

        private Func<Message, CancellationToken, Task> handler;
        private Func<ExceptionReceivedEventArgs, Task> exceptionHandler;

        public string QueueName => throw new NotImplementedException();

        public int PrefetchCount { get; set; }

        public ReceiveMode ReceiveMode => throw new NotImplementedException();

        public string ClientId => throw new NotImplementedException();

        public bool IsClosedOrClosing => throw new NotImplementedException();

        public string Path => throw new NotImplementedException();

        public TimeSpan OperationTimeout { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public ServiceBusConnection ServiceBusConnection => throw new NotImplementedException();

        public IList<ServiceBusPlugin> RegisteredPlugins => throw new NotImplementedException();

        public bool OwnsConnection => throw new NotImplementedException();

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


        public Task AbandonAsync(string lockToken, IDictionary<string, object> propertiesToModify = null)
        {
            throw new NotImplementedException();
        }

        public Task CancelScheduledMessageAsync(long sequenceNumber)
        {
            throw new NotImplementedException();
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

        public Task CompleteAsync(string lockToken)
        {
            throw new NotImplementedException();
        }

        public Task DeadLetterAsync(string lockToken, IDictionary<string, object> propertiesToModify = null)
        {
            throw new NotImplementedException();
        }

        public Task DeadLetterAsync(string lockToken, string deadLetterReason, string deadLetterErrorDescription = null)
        {
            throw new NotImplementedException();
        }

        public void RegisterMessageHandler(Func<Message, CancellationToken, Task> handler, Func<ExceptionReceivedEventArgs, Task> exceptionReceivedHandler)
        {
            lock (this)
            {
                this.handler = handler;
                this.exceptionHandler = exceptionReceivedHandler;
            }
        }

        public void RegisterMessageHandler(Func<Message, CancellationToken, Task> handler, MessageHandlerOptions messageHandlerOptions)
        {
            lock (this)
            {
                this.handler = handler;
            }
        }

        public void RegisterPlugin(ServiceBusPlugin serviceBusPlugin)
        {
            throw new NotImplementedException();
        }

        public void RegisterSessionHandler(Func<IMessageSession, Message, CancellationToken, Task> handler, Func<ExceptionReceivedEventArgs, Task> exceptionReceivedHandler)
        {
            throw new NotImplementedException();
        }

        public void RegisterSessionHandler(Func<IMessageSession, Message, CancellationToken, Task> handler, SessionHandlerOptions sessionHandlerOptions)
        {
            throw new NotImplementedException();
        }

        public Task<long> ScheduleMessageAsync(Message message, DateTimeOffset scheduleEnqueueTimeUtc)
        {
            throw new NotImplementedException();
        }

        public Task SendAsync(Message message)
        {
            throw new NotImplementedException();
        }

        public Task SendAsync(IList<Message> messageList)
        {
            throw new NotImplementedException();
        }

        public void UnregisterPlugin(string serviceBusPluginName)
        {
            throw new NotImplementedException();
        }
    }
}