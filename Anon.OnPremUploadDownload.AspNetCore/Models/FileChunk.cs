using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Anon.OnPremUploadDownload.AspNetCore.Models
{
    [BindProperties(SupportsGet = true)]
    public class FileChunk
    {
        public string Path { get; set; }
        public long StartIndex { get; set; }
        public long Length { get; set; }
    }
}
