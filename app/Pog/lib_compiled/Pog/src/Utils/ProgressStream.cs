using System;
using System.IO;

namespace Pog.Utils;

/// Wrapper for another readable <see cref="Stream"/> instance that reports read progress.
public class ProgressStream(Stream stream, Action<long> progressCb) : Stream {
    private long _position = 0;

    public ProgressStream(Stream stream, IProgress<long> progress) : this(stream, progress.Report) {}

    protected override void Dispose(bool disposing) {
        stream.Dispose();
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) {
        var result = stream.Read(buffer, offset, count);
        _position += result;
        progressCb(_position);
        return result;
    }

    public override void Write(byte[] buffer, int offset, int count) {
        throw new NotSupportedException("The stream does not support writing.");
    }

    public override void Flush() => stream.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override bool CanRead => stream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position {
        get => _position;
        set => throw new NotSupportedException();
    }
}
