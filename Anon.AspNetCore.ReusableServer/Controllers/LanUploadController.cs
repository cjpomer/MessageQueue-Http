using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Anon.AspNetCore.ReusableServer.Filters;
using Anon.AspNetCore.ReusableServer.Models;
using Anon.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly;

namespace Anon.AspNetCore.ReusableServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LanUploadController : ControllerBase
    {
        private readonly ITopicClient topic;
        private readonly CloudBlobContainer container;
        private readonly IConfiguration config;

        public LanUploadController(ITopicClient topic, CloudBlobContainer container, IConfiguration config)
        {
            this.topic = topic ?? throw new ArgumentNullException(nameof(topic));
            this.container = container ?? throw new ArgumentNullException(nameof(container));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        // POST api/lanupload
        [HttpPost]
        [AuthorizeEnterpriseIam]
        public async Task<IActionResult> PostAsync([FromBody] string path)
        {
            var file = new System.IO.FileInfo(path);
            long bytesPerMessage = config.GetBytesPerMessage();

            // 1. check file exists
            if (!file.Exists)
            {
                return NotFound();
            }

            // 2. calculate number of message queue messages to create
            long numMessages = file.Length % bytesPerMessage != 0 ? file.Length / bytesPerMessage + 1 : file.Length / bytesPerMessage;

            // 3. setup reusable objects to minimize `new` memory
            int messagesPerSend = config.GetMessagesPerSend();
            var builder = new HttpRequestBuilder()
                               .SetHost("/")
                               .SetMethod("post")
                               .SetPath("/api/filechunk");
            foreach (var header in HttpContext.Request.Headers) { builder.AddHeader(header.Key, header.Value); }

            var httpRequestArray = Enumerable.Range(0, messagesPerSend).Select(i => builder.Build()).ToArray();
            var messageArray = Enumerable.Range(0, messagesPerSend).Select(i => MessageFactory(HttpContext.Request.Headers)).ToArray();
            var messageList = new List<Message>(messagesPerSend);
            var idList = new List<string>(messagesPerSend);
            var fileChunk = new FileChunk { Path = path, Length = bytesPerMessage };
            var policy = Policy.Handle<Exception>().WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i)));

            // 4. create CloudBlockBlob
            var blobName = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileChunk.Path));
            var blob = container.GetBlockBlobReference(blobName);

            // 5. bin messages into bulk groups and send by bulk: outer loop defines bulk bin, inner loop populates bulk bin
            for (long i = 0; i < numMessages; i += messagesPerSend)
            {
                messageList.Clear();
                idList.Clear();
                for (long j = i; j < numMessages && j < messagesPerSend; j++)
                {
                    fileChunk.StartIndex = j * bytesPerMessage;
                    var request = httpRequestArray[j % messagesPerSend];
                    request.Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(fileChunk));

                    var message = messageArray[j % messagesPerSend];
                    message.Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));

                    messageList.Add(message);
                    idList.Add(j.ToString(Constants.IdFormat));
                }

                // 6. establish blocks in order
                await policy.ExecuteAsync(() => blob.PutBlockListAsync(idList));

                // 7. send messages in bulk to topic
                await policy.ExecuteAsync(() => topic.SendAsync(messageList));
            }

            return Ok();
        }

        private Message MessageFactory(IHeaderDictionary headers)
        {
            var m = new Message() { ContentType = "application/json" };
            foreach (var header in headers)
            {
                m.UserProperties[header.Key] = header.Value;
            }
            return m;
        }
    }
}
