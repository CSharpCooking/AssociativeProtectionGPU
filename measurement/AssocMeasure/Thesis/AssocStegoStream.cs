// Verbatim from the thesis, Appendix 1
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
public class AssocStegoStream : Stream
{
private readonly Stream _stream;
private readonly AssocStego _stegoAlg;
private readonly int _containerByteLen;
private readonly int _hiddenByteLen;
public AssocStegoStream(Stream stream, AssocStego stegoAlg)
{
_stream = stream ?? throw new ArgumentNullException(nameof(stream));
_stegoAlg = stegoAlg ?? throw new ArgumentNullException(nameof(stegoAlg));
_containerByteLen = (_stegoAlg.etalonLength + 7) >> 3;
_hiddenByteLen = 3 * _containerByteLen;
}
public override bool CanRead => _stream.CanRead;
public override bool CanSeek => false;
public override bool CanWrite => _stream.CanWrite;
public override long Length => _stream.Length;
public override long Position
{
get => _stream.Position;
set => throw new NotSupportedException("Перемотка в стего-потоке не поддерживается.");
}
public override void Flush() => _stream.Flush();
public override void Write(byte[] buffer, int offset, int count)
{
if (buffer == null)
throw new ArgumentNullException(nameof(buffer));
if (offset < 0 || count < 0 || offset + count > buffer.Length)
throw new ArgumentOutOfRangeException(nameof(offset));
byte[] rented = ArrayPool<byte>.Shared.Rent(_hiddenByteLen);
try
{
var hidden = rented.AsSpan(0, _hiddenByteLen);
for (int i = offset; i < offset + count; i++)
{
_stegoAlg.HideByteToBuffer(buffer[i], hidden);
_stream.Write(rented, 0, _hiddenByteLen);
}
}
finally
{
ArrayPool<byte>.Shared.Return(rented);
}
}
public override int Read(byte[] buffer, int offset, int count)
{
if (buffer == null)
throw new ArgumentNullException(nameof(buffer));
if (offset < 0 || count < 0 || offset + count > buffer.Length)
throw new ArgumentOutOfRangeException(nameof(offset));
if (!_stream.CanRead)
throw new NotSupportedException("Подлежащий поток не поддерживает чтение.");
int readCount = 0;
byte[] rented = ArrayPool<byte>.Shared.Rent(_hiddenByteLen);
try
{
for (int i = offset; i < offset + count; i++)
{
int totalRead = 0;
while (totalRead < _hiddenByteLen)
{
int r = _stream.Read(rented, totalRead, _hiddenByteLen - totalRead);
if (r == 0)
return readCount;
totalRead += r;
}
buffer[i] = _stegoAlg.DiscloseByteFromBuffer(rented.AsSpan(0, _hiddenByteLen));
readCount++;
}
return readCount;
}
finally
{
ArrayPool<byte>.Shared.Return(rented);
}
}
public override long Seek(long offset, SeekOrigin origin)
=> throw new NotSupportedException();
public override void SetLength(long value)
=> throw new NotSupportedException();
private byte[] HideByte(byte value)
{
byte[] result = new byte[_hiddenByteLen];
_stegoAlg.HideByteToBuffer(value, result);
return result;
}
public byte DiscloseByte(byte[] hidden)
{
if (hidden == null)
throw new ArgumentNullException(nameof(hidden));
return _stegoAlg.DiscloseByteFromBuffer(hidden);
}
}
