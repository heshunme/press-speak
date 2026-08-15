using System.Net;
using SharpCompress.Common;
using SharpCompress.Writers;

namespace HsAsrDictation.Tests;

internal static class ProvisioningTestDoubles
{
    public static HttpMessageHandler RespondingWithBytes(byte[] body) =>
        new StubHttpMessageHandler(new ByteArrayContent(body));

    public static HttpMessageHandler HangingBody() =>
        new StubHttpMessageHandler(new StreamContent(new HangingReadStream()));

    public static byte[] BuildTarBz2(string entryName, byte[] payload)
    {
        using var ms = new MemoryStream();
        using (var writer = WriterFactory.Open(
                   ms,
                   ArchiveType.Tar,
                   new WriterOptions(CompressionType.BZip2) { LeaveStreamOpen = true }))
        {
            writer.Write(entryName, new MemoryStream(payload));
        }

        return ms.ToArray();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpContent _content;

        public StubHttpMessageHandler(HttpContent content) => _content = content;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = _content });
    }

    // 读取永久挂起（直到取消）的流，用于模拟网络挂起的下载正文。
    private sealed class HangingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
