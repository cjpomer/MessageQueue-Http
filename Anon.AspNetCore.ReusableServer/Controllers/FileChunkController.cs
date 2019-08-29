using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Anon.AspNetCore.ReusableServer.Filters;
using Anon.AspNetCore.ReusableServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Storage.Blob;
using Polly;

namespace Anon.AspNetCore.ReusableServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileChunkController : ControllerBase
    {
        private readonly CloudBlobContainer container;

        public FileChunkController(CloudBlobContainer container)
        {
            this.container = container ?? throw new ArgumentNullException(nameof(container));
        }

        // POST api/filechunk
        [HttpPost]
        [AuthorizeEnterpriseIam]
        public async Task<IActionResult> PostAsync([FromBody] FileChunk fileChunk)
        {
            // 1. Check file exists
            var file = new FileInfo(fileChunk.Path);
            if (!file.Exists)
            {
                return NotFound();
            }

            try
            {
                // 2. Setup the file stream
                using (var filestream = file.Open(FileMode.Open, FileAccess.Read))
                {
                    filestream.Seek(fileChunk.StartIndex, SeekOrigin.Begin);
                    filestream.SetLength(fileChunk.Length);

                    // 3. Setup BlockBlob
                    var blobName = Convert.ToBase64String(Encoding.UTF8.GetBytes(fileChunk.Path));
                    var blob = container.GetBlockBlobReference(blobName);
                    var id = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Format((fileChunk.StartIndex / fileChunk.Length).ToString(Constants.IdFormat))));

                    // 4. Upload the the block
                    var policy = Policy.Handle<Exception>().WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i)));
                    await policy.ExecuteAsync(() => blob.PutBlockAsync(id, filestream, string.Empty));
                }

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