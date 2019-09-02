using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.ServiceBus;
using Newtonsoft.Json;

namespace Anon.OnPremUploadDownload.AspNetCore
{
    public class HttpMessageFactory : IHttpMessageFactory
    {
        public Message CreateMessage(IHeaderDictionary headers, Http.HttpRequest request)
        {
            var m = CreateMessage(headers);
            m.Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request));
            return m;
        }

        public Message CreateMessage(IHeaderDictionary headers)
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
