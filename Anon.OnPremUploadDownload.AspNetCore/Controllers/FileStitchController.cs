using Anon.OnPremUploadDownload.AspNetCore.Filters;
using Anon.OnPremUploadDownload.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.Storage.Blob;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace Anon.OnPremUploadDownload.AspNetCore.Controllers
{
    public class FileStitchController : ControllerBase
    {
        private readonly ITopicClient topic;
        private readonly IHttpMessageFactory httpMessageFactory;
        private readonly AsyncRetryPolicy policy;
        private readonly Polly.Retry.RetryPolicy policyFile;

        public FileStitchController(ITopicClient topic, IHttpMessageFactory httpMessageFactory)
        {
            this.topic = topic ?? throw new ArgumentNullException(nameof(topic));
            this.httpMessageFactory = httpMessageFactory ?? throw new ArgumentNullException(nameof(httpMessageFactory));

            // asyncronously handle transient exceptions by waiting 0.5 seconds, 2 seconds, 4 seconds, etc., before retrying, and finally throwing the exception
            policy = Policy.Handle<Exception>().WaitAndRetryAsync(Constants.MAX_TRANSIENT_EXCEPTIONS, i => TimeSpan.FromSeconds(0.5 * Math.Pow(2, i)));

            // syncronously handle transient file IO exceptions (excluding security exceptions) by waiting 0.5 seconds, 2 seconds, 4 seconds, etc., before retrying, and finally throwing the exception
            policyFile = Policy.Handle<Exception>(ex => !(ex is SecurityException || ex is UnauthorizedAccessException))
                .WaitAndRetry(3, i => TimeSpan.FromSeconds(0.5 * Math.Pow(2, i)));
        }


        // GET api/filestitch
        [HttpGet]
        [AuthorizeEnterpriseIam]
        public async Task<IActionResult> GetAsync(string path, long length, long bytesPerChunk)
        {
            // 0. Wait
            await Task.Delay(Constants.FILESTITCH_DELAY);

            // 1. Determine which chunk is next up
            var chunkFolder = $"{path}.{Constants.PARTIAL}";
            FileInfo fileinfo = null;
            try
            {
                policyFile.Execute(() => fileinfo = new FileInfo(path));
            }
            catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return NotFound();
            }

            var fileLength = fileinfo.Length;
            if (fileLength == length)
            {
                try
                {
                    policyFile.Execute(() => Directory.Delete(chunkFolder));
                    return Ok();
                }
                catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException)
                {
                    return Forbid();
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                {
                    return NotFound();
                }
            }
            long nextChunk = fileLength / bytesPerChunk;

            // 2. Determine if file is ready for next chunk
            bool fileReady = fileLength % bytesPerChunk == 0;

            // 3. Determine if next-up chunk is ready to go
            var chunkPath = $"{chunkFolder}/{nextChunk.ToString(Constants.ID_FORMAT)}";
            var chunkinfo = new FileInfo(chunkPath);
            var lastChunk = length % bytesPerChunk != 0 ? length / bytesPerChunk + 1 : length / bytesPerChunk;
            var lastChunkLength = length - lastChunk * bytesPerChunk;
            bool chunkReady = chunkinfo.Length == bytesPerChunk || (nextChunk == lastChunk && chunkinfo.Length == lastChunkLength);

            // 4. If chunk is ready to be stitched, stitch
            if (chunkReady)
            {
                try
                {
                    await policy.ExecuteAsync(async () =>
                    {
                        using (var chunkStream = chunkinfo.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                        using (var fileStream = fileinfo.Open(FileMode.Append, FileAccess.Write, FileShare.None))
                        {
                            await chunkStream.CopyToAsync(fileStream);
                        }
                        chunkinfo.Delete();
                    });
                }
                catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException)
                {
                    return Forbid();
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
                {
                    return NotFound();
                }
            }

            // 5. Let someone else do the rest of the work
            var builder = new HttpRequestBuilder();
            var stitchRequest = builder.SetPath("/api/filestitch")
                .AddQuery("path", path)
                .AddQuery("length", length.ToString())
                .AddQuery("bytesPerChunk", bytesPerChunk.ToString())
                .Build();
            var stitchMessage = httpMessageFactory.CreateMessage(HttpContext.Request.Headers, stitchRequest);
            await policy.ExecuteAsync(() => topic.SendAsync(stitchMessage));

            return Ok();
        }
    }
}
