// Revision kit: GPU encryptor/decryptor variants used for the new measurements.
// Structure and file handling follow GpuStegoEncryptor/GpuStegoDecryptor of the thesis (Appendix 1);
// differences: selectable recognition algorithm, explicit group size (0 = automatic, as before),
// optional reuse of device buffers across blocks, no console output inside the measured code.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using ILGPU;
using ILGPU.Runtime;

public enum DecodeAlgorithm { Search, BranchFree, Table }

public sealed class GpuStegoEncryptorV2 : IDisposable
{
    private readonly Accelerator _acc;
    private readonly Action<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int> _kernel;
    private readonly MemoryBuffer1D<byte, Stride1D.Dense> _gpuMaskedEtalons, _gpuKeys;
    private readonly int _c, _h, _blockSize;
    private readonly bool _reuse;
    public GpuStegoEncryptorV2(Accelerator acc, AssocStego stego, int blockSize = 512 * 1024, int groupSize = 0, bool reuseDeviceBuffers = false)
    {
        _acc = acc; _c = stego.ContainerByteLength; _h = stego.HiddenByteLength; _blockSize = blockSize; _reuse = reuseDeviceBuffers;
        _gpuMaskedEtalons = acc.Allocate1D<byte>(stego.FlattenedMaskedEtalonsBytes);
        _gpuKeys = acc.Allocate1D<byte>(stego.FlattenedKeysBytes);
        _kernel = groupSize <= 0
            ? acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int>(NewKernels.EncryptKernelCopy)
            : acc.LoadImplicitlyGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int>(NewKernels.EncryptKernelCopy, groupSize);
    }
    public void EncryptToStream(byte[] input, string outputPath)
    {
        using var outputStream = new FileStream(outputPath, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });
        int totalBlocks = (input.Length + _blockSize - 1) / _blockSize;
        // Only API calls already used by the thesis code: Allocate1D, CopyFromCPU(array), CopyToCPU(array), Synchronize.
        byte[]? fullIn = input.Length >= _blockSize ? new byte[_blockSize] : null;
        byte[]? fullOut = input.Length >= _blockSize ? new byte[_blockSize * _h] : null;
        MemoryBuffer1D<byte, Stride1D.Dense>? dIn = null, dOut = null;
        if (_reuse && fullIn != null) { dIn = _acc.Allocate1D<byte>(_blockSize); dOut = _acc.Allocate1D<byte>((long)_blockSize * _h); }
        try
        {
            for (int b = 0; b < totalBlocks; b++)
            {
                int offset = b * _blockSize;
                int n = Math.Min(_blockSize, input.Length - offset);
                bool full = n == _blockSize;
                byte[] inBlock = full ? fullIn! : new byte[n];
                byte[] outBlock = full ? fullOut! : new byte[n * _h];
                Buffer.BlockCopy(input, offset, inBlock, 0, n);
                bool reuseThis = _reuse && full;
                var gin = reuseThis ? dIn! : _acc.Allocate1D<byte>(n);
                var gout = reuseThis ? dOut! : _acc.Allocate1D<byte>((long)n * _h);
                try
                {
                    gin.CopyFromCPU(inBlock);
                    int seed = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
                    _kernel(n, gin.View, gout.View, _gpuMaskedEtalons.View, _gpuKeys.View, _c, offset, seed);
                    _acc.Synchronize();
                    gout.CopyToCPU(outBlock);
                }
                finally { if (!reuseThis) { gin.Dispose(); gout.Dispose(); } }
                outputStream.Write(outBlock, 0, n * _h);
            }
        }
        finally { dIn?.Dispose(); dOut?.Dispose(); }
    }
    public void Dispose() { _gpuMaskedEtalons.Dispose(); _gpuKeys.Dispose(); }
}

public sealed class GpuStegoDecryptorV2 : IDisposable
{
    private readonly Accelerator _acc;
    private readonly DecodeAlgorithm _alg;
    private readonly Action<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int>? _search;
    private readonly Action<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<int>, ArrayView<int>, ArrayView<byte>, int, int>? _table;
    private readonly MemoryBuffer1D<byte, Stride1D.Dense> _gpuMaskedEtalons, _gpuKeys, _gpuTable;
    private readonly MemoryBuffer1D<int, Stride1D.Dense> _gpuPosByte, _gpuPosBit;
    private readonly int _c, _h, _etalonCount, _m, _blockSize;
    private readonly bool _reuse;
    public GpuStegoDecryptorV2(Accelerator acc, AssocStego stego, DecodeAlgorithm alg, int blockSize = 512 * 1024, int groupSize = 0, bool reuseDeviceBuffers = false)
    {
        _acc = acc; _alg = alg; _c = stego.ContainerByteLength; _h = stego.HiddenByteLength; _etalonCount = stego.etalons.Length;
        _blockSize = blockSize; _reuse = reuseDeviceBuffers;
        _gpuMaskedEtalons = acc.Allocate1D<byte>(stego.FlattenedMaskedEtalonsBytes);
        _gpuKeys = acc.Allocate1D<byte>(stego.FlattenedKeysBytes);
        var tb = RecognitionTable.Build(stego.FlattenedKeysBytes, stego.FlattenedMaskedEtalonsBytes, _c, _etalonCount);
        _m = tb.M;
        _gpuTable = acc.Allocate1D<byte>(tb.Table);
        _gpuPosByte = acc.Allocate1D<int>(tb.PosByte);
        _gpuPosBit = acc.Allocate1D<int>(tb.PosBit);
        if (alg == DecodeAlgorithm.Table)
            _table = groupSize <= 0
                ? acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<int>, ArrayView<int>, ArrayView<byte>, int, int>(NewKernels.DecryptTableKernel)
                : acc.LoadImplicitlyGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<int>, ArrayView<int>, ArrayView<byte>, int, int>(NewKernels.DecryptTableKernel, groupSize);
        else
        {
            Action<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int> k =
                alg == DecodeAlgorithm.Search ? NewKernels.DecryptSearchKernelCopy : NewKernels.DecryptBranchFreeKernel;
            _search = groupSize <= 0
                ? acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int>(k)
                : acc.LoadImplicitlyGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int>(k, groupSize);
        }
    }
    public byte[] DecryptFromFile(string filePath, int expectedOutputLength)
    {
        var result = new byte[expectedOutputLength];
        using var inputStream = new FileStream(filePath, new FileStreamOptions { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 1 << 20, Options = FileOptions.SequentialScan });
        int totalBlocks = (expectedOutputLength + _blockSize - 1) / _blockSize;
        byte[]? fullIn = expectedOutputLength >= _blockSize ? new byte[_blockSize * _h] : null;
        byte[]? fullOut = expectedOutputLength >= _blockSize ? new byte[_blockSize] : null;
        MemoryBuffer1D<byte, Stride1D.Dense>? dIn = null, dOut = null;
        if (_reuse && fullIn != null) { dIn = _acc.Allocate1D<byte>((long)_blockSize * _h); dOut = _acc.Allocate1D<byte>(_blockSize); }
        try
        {
            for (int b = 0; b < totalBlocks; b++)
            {
                int outOffset = b * _blockSize;
                int n = Math.Min(_blockSize, expectedOutputLength - outOffset);
                bool full = n == _blockSize;
                int inN = n * _h;
                byte[] inBlock = full ? fullIn! : new byte[inN];
                byte[] outBlock = full ? fullOut! : new byte[n];
                int read = 0;
                while (read < inN) { int r = inputStream.Read(inBlock, read, inN - read); if (r == 0) throw new InvalidOperationException($"block {b}: unexpected end of file"); read += r; }
                bool reuseThis = _reuse && full;
                var gin = reuseThis ? dIn! : _acc.Allocate1D<byte>(inN);
                var gout = reuseThis ? dOut! : _acc.Allocate1D<byte>(n);
                try
                {
                    gin.CopyFromCPU(inBlock);
                    if (_alg == DecodeAlgorithm.Table)
                        _table!(n, gin.View, gout.View, _gpuPosByte.View, _gpuPosBit.View, _gpuTable.View, _m, _c);
                    else
                        _search!(n, gin.View, gout.View, _gpuMaskedEtalons.View, _gpuKeys.View, _c, 3, _etalonCount);
                    _acc.Synchronize();
                    gout.CopyToCPU(outBlock);
                }
                finally { if (!reuseThis) { gin.Dispose(); gout.Dispose(); } }
                Buffer.BlockCopy(outBlock, 0, result, outOffset, n);
            }
        }
        finally { dIn?.Dispose(); dOut?.Dispose(); }
        return result;
    }
    public void Dispose() { _gpuMaskedEtalons.Dispose(); _gpuKeys.Dispose(); _gpuTable.Dispose(); _gpuPosByte.Dispose(); _gpuPosBit.Dispose(); }
}
