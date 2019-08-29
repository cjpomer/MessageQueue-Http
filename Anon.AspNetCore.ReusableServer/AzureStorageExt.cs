using System;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Extensions.Configuration;

namespace Anon.AspNetCore.ReusableServer
{
    public static class AzureStorageExt
    {
        public static Uri GetContainerUri(this IConfiguration config)
        {
            return new Uri(config.GetValue("AzureStorage:ContainerUri", string.Empty));
        }

        public static StorageCredentials GetStorageCredentials(this IConfiguration config)
        {
            return new StorageCredentials(config.GetValue("AzureStorage:AccountName", string.Empty), config.GetValue("AzureStorage:KeyValue", string.Empty), config.GetValue("AzureStorage:KeyName", string.Empty));
        }
    }
}
