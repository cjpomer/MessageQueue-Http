﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Anon.AspNetCore.ReusableServer.Models
{
    public class FileChunk
    {
        public string Path { get; set; }
        public long StartIndex { get; set; }
        public long Length { get; set; }
    }
}
