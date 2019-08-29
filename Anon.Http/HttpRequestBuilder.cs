using System;
using System.Collections.Generic;
using System.Linq;

namespace Anon.Http
{
    public class HttpRequestBuilder
    {
        byte[] body = new byte[] { };
        List<(string, string)> headerBuilder = new List<(string, string)>();
        string host;
        string method;
        string path;
        List<(string, string)> queryBuilder = new List<(string, string)>();

        public HttpRequestBuilder AddHeader(string key, string value)
        {
            headerBuilder.Add((key, value));
            return this;
        }

        public HttpRequestBuilder AddQuery(string lhs, string rhs)
        {
            queryBuilder.Add((lhs, rhs));
            return this;
        }

        public HttpRequest Build()
        {
            if (string.IsNullOrEmpty(host)) { throw new InvalidOperationException("'Host' is a required field"); }

            return new HttpRequest
            {
                Body = body,
                Headers = headerBuilder.ToArray(),
                Host = host,
                Method = method,
                Path = path,
                Query = "?" + queryBuilder.Select(cur => $"{cur.Item1}={cur.Item2}").Aggregate((cur, agg) => $"{agg}&{cur}"),
            };
        }

        public HttpRequestBuilder SetBody(byte[] body)
        {
            this.body = body;
            return this;
        }

        public HttpRequestBuilder SetHost(string host)
        {
            this.host = host;
            return this;
        }

        public HttpRequestBuilder SetMethod(string method)
        {
            method = method.ToLower();
            if (!new[] { "get", "head", "post", "put", "delete", "connect", "options", "trace", "patch" }.Contains(method))
            {
                throw new ArgumentException("invalid method", nameof(method));
            }

            this.method = method;
            return this;
        }

        public HttpRequestBuilder SetPath(string path)
        {
            this.path = path;
            return this;
        }

    }
}