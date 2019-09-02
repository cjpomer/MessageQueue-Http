using Anon.OnPremUploadDownload.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.ServiceBus;

namespace Anon.OnPremUploadDownload.AspNetCore
{
    public interface IHttpMessageFactory
    {
        Message CreateMessage(IHeaderDictionary headers);
        Message CreateMessage(IHeaderDictionary headers, Http.HttpRequest request);
    }
}