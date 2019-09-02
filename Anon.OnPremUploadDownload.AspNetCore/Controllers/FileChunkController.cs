using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Anon.OnPremUploadDownload.AspNetCore.Filters;
using Anon.OnPremUploadDownload.AspNetCore.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Storage.Blob;
using Polly;
using Polly.Retry;

namespace Anon.OnPremUploadDownload.AspNetCore.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileChunkController : ControllerBase
    {
        private readonly CloudBlobContainer container;
        private readonly AsyncRetryPolicy policy;
        private readonly Polly.Retry.RetryPolicy policyFile;

        public FileChunkController(CloudBlobContainer container)
        {
            this.container = container ?? throw new ArgumentNullException(nameof(container));

            // asyncronously handle transient exceptions by waiting 0.5 seconds, 2 seconds, 4 seconds, etc., before retrying, and finally throwing the exception
            policy = Policy.Handle<Exception>().WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(0.5 * Math.Pow(2, i)));

            // syncronously handle transient file IO exceptions (excluding security exceptions) by waiting 0.5 seconds, 2 seconds, 4 seconds, etc., before retrying, and finally throwing the exception
            policyFile = Policy.Handle<Exception>(ex => !(ex is SecurityException || ex is UnauthorizedAccessException))
                .WaitAndRetry(3, i => TimeSpan.FromSeconds(0.5 * Math.Pow(2, i)));
        }

        // GET api/filechunk
        [HttpGet]
        [AuthorizeEnterpriseIam]
        public async Task<IActionResult> GetAsync(FileChunk fileChunk)
        {
            // 1. Check the blob exists
            var blobName = Uri.EscapeDataString(fileChunk.Path);
            var blob = container.GetBlockBlobReference(blobName);
            if (!await blob.ExistsAsync())
            {
                return NotFound();
            }

            try
            {
                // 2. Setup the file stream
                var id = string.Format((fileChunk.StartIndex / fileChunk.Length).ToString(Constants.ID_FORMAT));
                await policy.ExecuteAsync(async () =>
                {
                    var file = new FileInfo($"{fileChunk.Path}.{Constants.PARTIAL}/{id}");
                    using (var filestream = file.Open(FileMode.Create, FileAccess.Write))
                    using (var blobStream = await blob.OpenReadAsync())
                    {
                        filestream.Seek(fileChunk.StartIndex, SeekOrigin.Begin);
                        filestream.SetLength(fileChunk.Length);

                        // 3. Setup the blob stream
                        blobStream.Seek(fileChunk.StartIndex, SeekOrigin.Begin);

                        // 4. Download the block
                        await blobStream.CopyToAsync(filestream);
                    }
                });

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

        // POST api/filechunk
        [HttpPost]
        [AuthorizeEnterpriseIam]
        public async Task<IActionResult> PostAsync([FromBody] FileChunk fileChunk)
        {
            try
            {
                // 1. Check file exists
                FileInfo file = null;
                policyFile.Execute(() => file = new FileInfo(fileChunk.Path));
                if (!file.Exists)
                {
                    return NotFound();
                }

                // 2. Setup the file stream
                await policy.ExecuteAsync(async () =>
                {
                    using (var filestream = file.Open(FileMode.Open, FileAccess.Read))
                    {
                        filestream.Seek(fileChunk.StartIndex, SeekOrigin.Begin);
                        filestream.SetLength(fileChunk.Length);

                        // 3. Setup BlockBlob
                        var blobName = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileChunk.Path));
                        var blob = container.GetBlockBlobReference(blobName);
                        var id = string.Format((fileChunk.StartIndex / fileChunk.Length).ToString(Constants.ID_FORMAT));

                        // 4. Upload the the block
                        await policy.ExecuteAsync(async () => await blob.PutBlockAsync(id, filestream, string.Empty));
                    }
                });

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
    }
}