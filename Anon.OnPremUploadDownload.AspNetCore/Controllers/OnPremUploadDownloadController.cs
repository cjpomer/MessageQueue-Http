using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Anon.OnPremUploadDownload.AspNetCore.Filters;
using Anon.OnPremUploadDownload.AspNetCore.Models;
using Anon.OnPremUploadDownload.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Polly;
using System.Security;
using System.IO;
using Polly.Retry;

namespace Anon.OnPremUploadDownload.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OnPremUploadDownloadController : ControllerBase
    {
        private readonly ITopicClient topic;
        private readonly CloudBlobContainer container;
        private readonly IHttpMessageFactory messageFactory;
        private readonly IConfiguration config;
        private readonly AsyncRetryPolicy policy;
        private readonly Polly.Retry.RetryPolicy policyFile;

        public OnPremUploadDownloadController(ITopicClient topic, CloudBlobContainer container, IHttpMessageFactory messageFactory, IConfiguration config)
        {
            this.topic = topic ?? throw new ArgumentNullException(nameof(topic));
            this.container = container ?? throw new ArgumentNullException(nameof(container));
            this.messageFactory = messageFactory ?? throw new ArgumentNullException(nameof(messageFactory));
            this.config = config ?? throw new ArgumentNullException(nameof(config));

            // asyncronously handle transient exceptions by waiting 0.5 seconds, 2 seconds, 4 seconds, etc., before retrying, and finally throwing the exception
            policy = Policy.Handle<Exception>().WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(0.5 * Math.Pow(2, i)));

            // syncronously handle transient file IO exceptions (excluding security exceptions) by waiting 0.5 seconds, 2 seconds, 4 seconds, etc., before retrying, and finally throwing the exception
            policyFile = Policy.Handle<Exception>(ex => !(ex is SecurityException || ex is UnauthorizedAccessException))
                .WaitAndRetry(3, i => TimeSpan.FromSeconds(0.5 * Math.Pow(2, i)));

        }

        // GET api/onpremuploaddownload
        [HttpGet]
        [AuthorizeEnterpriseIam]
        public async Task<IActionResult> GetAsync(string path)
        {
            long bytesPerMessage = config.GetBytesPerMessage();

            // 1. Check the file exists
            var fileName = Uri.UnescapeDataString(path);
            var partial = $"{fileName}.{Constants.PARTIAL}";
            try
            {
                FileInfo file = null;
                policyFile.Execute(() => file = new FileInfo(fileName));

                if (file.Exists)
                {
                    return Conflict();
                }

                policyFile.Execute(() => Directory.CreateDirectory(partial));
            }
            catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return NotFound();
            }

            // 2. Check the blob exists
            var blob = container.GetBlockBlobReference(path);
            if (!await policy.ExecuteAsync(blob.ExistsAsync))
            {
                return NotFound();
            }

            // 2. calculate number of message queue messages to create
            await policy.ExecuteAsync(blob.FetchAttributesAsync);
            var blobLength = blob.Properties.Length;
            long numMessages = blobLength % bytesPerMessage != 0 ? blobLength / bytesPerMessage + 1 : blobLength / bytesPerMessage;

            // 3. setup reusable objects to minimize `new` memory
            int messagesPerSend = config.GetMessagesPerSend();
            var builder = new HttpRequestBuilder()
                               .SetHost("/")
                               .SetMethod("get")
                               .SetPath("/api/filechunk");
            foreach (var header in HttpContext.Request.Headers) { builder.AddHeader(header.Key, header.Value); }

            var httpRequestArray = Enumerable.Range(0, messagesPerSend).Select(i => builder.Build()).ToArray();
            var messageArray = Enumerable.Range(0, messagesPerSend).Select(i => messageFactory.CreateMessage(HttpContext.Request.Headers)).ToArray();
            var messageList = new List<Message>(messagesPerSend);

            // 4. bin messages into bulk groups and send by bulk: outer loop defines bulk bin, inner loop populates bulk bin
            for (long i = 0; i < numMessages; i += messagesPerSend)
            {
                messageList.Clear();
                for (long j = i; j < numMessages && j < messagesPerSend; j++)
                {
                    var request = httpRequestArray[j % messagesPerSend];
                    request.Query = $"path={path}&length={bytesPerMessage}&startindex = {j * bytesPerMessage}";

                    var message = messageArray[j % messagesPerSend];
                    message.Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));

                    messageList.Add(message);
                }

                // 5. send messages in bulk to topic
                await policy.ExecuteAsync(() => topic.SendAsync(messageList));
            }

            // 6. send a message to a controller that can stitch chunks together
            var stitchRequest = builder.SetPath("/api/filestitch")
                .AddQuery("path", path)
                .AddQuery("length", blobLength.ToString())
                .AddQuery("bytesPerChunk", bytesPerMessage.ToString())
                .Build();
            var stitchMessage = messageFactory.CreateMessage(HttpContext.Request.Headers, stitchRequest);
            await policy.ExecuteAsync(async () => await topic.SendAsync(stitchMessage));

            return Ok();
        }

        // POST api/onpremuploaddownload
        [HttpPost]
        [AuthorizeEnterpriseIam]
        public async Task<IActionResult> PostAsync([FromBody] string path)
        {
            long bytesPerMessage = config.GetBytesPerMessage();

            // 1. check file exists
            FileInfo file = null;
            long fileLength = 0;
            try
            {
                policyFile.Execute(() =>
                {
                    file = new FileInfo(path);
                    fileLength = file.Length;
                });
                if (!file.Exists)
                {
                    return NotFound();
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return NotFound();
            }

            // 2. calculate number of message queue messages to create
            long numMessages = fileLength % bytesPerMessage != 0 ? fileLength / bytesPerMessage + 1 : fileLength / bytesPerMessage;

            // 3. setup reusable objects to minimize `new` memory
            int messagesPerSend = config.GetMessagesPerSend();
            var builder = new HttpRequestBuilder()
                               .SetHost("/")
                               .SetMethod("post")
                               .SetPath("/api/filechunk");
            foreach (var header in HttpContext.Request.Headers) { builder.AddHeader(header.Key, header.Value); }

            var httpRequestArray = Enumerable.Range(0, messagesPerSend).Select(i => builder.Build()).ToArray();
            var messageArray = Enumerable.Range(0, messagesPerSend).Select(i => messageFactory.CreateMessage(HttpContext.Request.Headers)).ToArray();
            var messageList = new List<Message>(messagesPerSend);
            var idList = new List<string>(messagesPerSend);
            var fileChunk = new FileChunk { Path = path, Length = bytesPerMessage };

            // 4. create CloudBlockBlob
            var blobName = Uri.EscapeDataString(fileChunk.Path);
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
                    idList.Add(j.ToString(Constants.ID_FORMAT));
                }

                // 6. establish blocks in order
                await policy.ExecuteAsync(async () => await blob.PutBlockListAsync(idList));

                // 7. send messages in bulk to topic
                await policy.ExecuteAsync(async () => await topic.SendAsync(messageList));
            }

            return Ok();
        }

        
    }
}
