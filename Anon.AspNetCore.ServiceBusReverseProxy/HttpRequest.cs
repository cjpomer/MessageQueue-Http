namespace Anon.ServiceBus
{
    public struct HttpRequest
    {
        public byte[] Body { get; set; }
        public (string, string)[] Headers { get; set; }
        public string Host { get; set; }
        public string Method { get; set; }
        public string Path { get; set; }
        public string Query { get; set; }
    }
}