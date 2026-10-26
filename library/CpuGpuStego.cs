// CpuGpuStego library: listing from Appendix 1 of the master's thesis on which the paper is based.
// Reproduced as printed there; only the indentation was restored. The listing has no using directives
// and does not contain the BitVector class; see library/README.md for the names used in the paper.

public sealed class StegoLibrary
{
    public StegoOperationResult EncryptFile(
    string inputFilePath,
    string? outputFilePath = null,
    ExecutionMode mode = ExecutionMode.Cpu)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFilePath);
        if (!File.Exists(inputFilePath))
        throw new FileNotFoundException("Исходный файл не найден.", inputFilePath);
        string resolvedOutputPath = ResolveEncryptedOutputPath(inputFilePath, outputFilePath);
        byte[] inputBytes = File.ReadAllBytes(inputFilePath);
        string[] etalons = EtalonFactory.CreateDefaultEtalons10();
        var stego = new AssocStego(etalons);
        var key = stego.CreateKey();
        stego.ValidateKeysOrThrow(key);
        using var session = AcceleratorSelector.Create(mode);
        string tempPayloadPath = Path.GetTempFileName();
        try
        {
            EncryptPayload(inputBytes, stego, session, tempPayloadPath);
            var header = new PackagedStegoHeader
            {
                OriginalFileName = Path.GetFileName(inputFilePath),
                OriginalByteLength = inputBytes.LongLength,
                EtalonBitLength = stego.etalonLength,
                Etalons = etalons,
                KeyVectors = key.Select(x => x.ToByteArray()).ToArray(),
                PayloadLength = new FileInfo(tempPayloadPath).Length,
            };
            PackagedStegoFile.WritePackage(resolvedOutputPath, header, tempPayloadPath);
            return CreateResult(
            inputFilePath,
            resolvedOutputPath,
            inputBytes.LongLength,
            new FileInfo(resolvedOutputPath).Length,
            mode,
            session,
            operationName: "Шифрование");
        }
        finally
        {
            TryDelete(tempPayloadPath);
        }
    }
    public StegoOperationResult DecryptFile(
    string encryptedFilePath,
    string? outputFilePath = null,
    ExecutionMode mode = ExecutionMode.Cpu)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedFilePath);
        if (!File.Exists(encryptedFilePath))
        throw new FileNotFoundException("Файл со стего-данными не найден.", encryptedFilePath);
        string tempPayloadPath = Path.GetTempFileName();
        try
        {
            PackagedStegoHeader header = PackagedStegoFile.ReadHeaderAndExtractPayload(encryptedFilePath, tempPayloadPath);
            string resolvedOutputPath = ResolveDecryptedOutputPath(encryptedFilePath, outputFilePath, header.OriginalFileName);
            if (header.OriginalByteLength > int.MaxValue)
            throw new NotSupportedException("Текущая реализация библиотеки поддерживает расшифровку файлов размером до 2 ГБ.");
            var stego = new AssocStego(header.Etalons);
            var keys = PackagedStegoFile.RestoreKeys(header);
            stego.Key = keys;
            stego.ValidateKeysOrThrow(keys);
            using var session = AcceleratorSelector.Create(mode);
            byte[] outputBytes = DecryptPayload(tempPayloadPath, (int)header.OriginalByteLength, stego, session);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resolvedOutputPath))!);
            File.WriteAllBytes(resolvedOutputPath, outputBytes);
            return CreateResult(
            encryptedFilePath,
            resolvedOutputPath,
            new FileInfo(encryptedFilePath).Length,
            outputBytes.LongLength,
            mode,
            session,
            operationName: "Дешифрование");
        }
        finally
        {
            TryDelete(tempPayloadPath);
        }
    }
    public byte[] EncryptBytes(byte[] data, ExecutionMode mode = ExecutionMode.Cpu)
    {
        ArgumentNullException.ThrowIfNull(data);
        string tempInputPath = Path.GetTempFileName();
        string tempOutputPath = Path.GetTempFileName();
        TryDelete(tempOutputPath);
        try
        {
            File.WriteAllBytes(tempInputPath, data);
            EncryptFile(tempInputPath, tempOutputPath, mode);
            return File.ReadAllBytes(tempOutputPath);
        }
        finally
        {
            TryDelete(tempInputPath);
            TryDelete(tempOutputPath);
        }
    }
    public byte[] DecryptBytes(byte[] encryptedData, ExecutionMode mode = ExecutionMode.Cpu)
    {
        ArgumentNullException.ThrowIfNull(encryptedData);
        string tempInputPath = Path.GetTempFileName();
        string tempOutputPath = Path.GetTempFileName();
        TryDelete(tempOutputPath);
        try
        {
            File.WriteAllBytes(tempInputPath, encryptedData);
            DecryptFile(tempInputPath, tempOutputPath, mode);
            return File.ReadAllBytes(tempOutputPath);
        }
        finally
        {
            TryDelete(tempInputPath);
            TryDelete(tempOutputPath);
        }
    }
    public byte[] EncryptText(string text, ExecutionMode mode = ExecutionMode.Cpu, System.Text.Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return EncryptBytes((encoding ?? System.Text.Encoding.UTF8).GetBytes(text), mode);
    }
    public string DecryptText(byte[] encryptedData, ExecutionMode mode = ExecutionMode.Cpu, System.Text.Encoding? encoding = null)
    {
        byte[] plainBytes = DecryptBytes(encryptedData, mode);
        return (encoding ?? System.Text.Encoding.UTF8).GetString(plainBytes);
    }
    private static void EncryptPayload(byte[] inputBytes, AssocStego stego, AcceleratorSession session, string payloadPath)
    {
        if (!session.IsCpuFallback)
        {
            using var encryptor = new GpuStegoEncryptor(session.Accelerator!, stego);
            encryptor.EncryptToStream(inputBytes, payloadPath);
            return;
        }
        using var output = CreateSequentialWriteStream(payloadPath);
        using var stegoStream = new AssocStegoStream(output, stego);
        stegoStream.Write(inputBytes, 0, inputBytes.Length);
    }
    private static byte[] DecryptPayload(string payloadPath, int originalByteLength, AssocStego stego, AcceleratorSession session)
    {
        if (!session.IsCpuFallback)
        {
            using var decryptor = new GpuStegoDecryptor(session.Accelerator!, stego);
            return decryptor.DecryptFromFile(payloadPath, originalByteLength);
        }
        byte[] result = new byte[originalByteLength];
        using var input = CreateSequentialReadStream(payloadPath);
        using var stegoStream = new AssocStegoStream(input, stego);
        int total = 0;
        while (total < originalByteLength)
        {
            int read = stegoStream.Read(result, total, originalByteLength - total);
            if (read == 0)
            break;
            total += read;
        }
        if (total != originalByteLength)
        throw new InvalidDataException("Не удалось полностью расшифровать исходные данные.");
        return result;
    }
    private static StegoOperationResult CreateResult(
    string inputPath,
    string outputPath,
    long inputLength,
    long outputLength,
    ExecutionMode requestedMode,
    AcceleratorSession session,
    string operationName)
    {
        ExecutionMode actualMode = session.IsCpuFallback
        ? ExecutionMode.Cpu
        : requestedMode;
        return new StegoOperationResult
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            RequestedMode = requestedMode,
            ActualMode = actualMode,
            DeviceDescription = session.Description,
            UsedCpuFallback = session.IsCpuFallback && requestedMode != ExecutionMode.Cpu,
            InputLength = inputLength,
            OutputLength = outputLength,
            Message = session.IsCpuFallback && requestedMode != ExecutionMode.Cpu
            ? $"{operationName}: подходящий GPU не найден, выполнен fallback на CPU. {session.Message}"
            : $"{operationName}: выполнено с использованием {session.Description}.",
        };
    }
    private static string ResolveEncryptedOutputPath(string inputFilePath, string? outputFilePath)
    {
        if (!string.IsNullOrWhiteSpace(outputFilePath))
        return Path.GetFullPath(outputFilePath);
        string directory = Path.GetDirectoryName(Path.GetFullPath(inputFilePath))!;
        string fileName = Path.GetFileName(inputFilePath);
        return Path.Combine(directory, fileName + ".cgs");
    }
    private static string ResolveDecryptedOutputPath(string encryptedFilePath, string? outputFilePath, string originalFileName)
    {
        if (!string.IsNullOrWhiteSpace(outputFilePath))
        return Path.GetFullPath(outputFilePath);
        string directory = Path.GetDirectoryName(Path.GetFullPath(encryptedFilePath))!;
        string safeOriginalName = string.IsNullOrWhiteSpace(originalFileName)
        ? Path.GetFileNameWithoutExtension(encryptedFilePath) + ".decrypted.txt"
        : originalFileName;
        return Path.Combine(directory, safeOriginalName);
    }
    private static FileStream CreateSequentialWriteStream(string path) =>
    new(path, new FileStreamOptions
    {
        Mode = FileMode.Create,
        Access = FileAccess.Write,
        Share = FileShare.None,
        BufferSize = 1 << 20,
        Options = FileOptions.SequentialScan,
    });
    private static FileStream CreateSequentialReadStream(string path) =>
    new(path, new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.Read,
        BufferSize = 1 << 20,
        Options = FileOptions.SequentialScan,
    });
    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        return;
        try
        {
            if (File.Exists(path))
            File.Delete(path);
        }
        catch
        {
        }
    }
}
public sealed class StegoOperationResult
{
    public required string InputPath { get; init; }
    public required string OutputPath { get; init; }
    public required ExecutionMode RequestedMode { get; init; }
    public required ExecutionMode ActualMode { get; init; }
    public required string DeviceDescription { get; init; }
    public required bool UsedCpuFallback { get; init; }
    public required long InputLength { get; init; }
    public required long OutputLength { get; init; }
    public required string Message { get; init; }
}
internal sealed class PackagedStegoHeader
{
    public required string OriginalFileName { get; init; }
    public required long OriginalByteLength { get; init; }
    public required int EtalonBitLength { get; init; }
    public required string[] Etalons { get; init; }
    public required byte[][] KeyVectors { get; init; }
    public required long PayloadLength { get; init; }
}
internal static class PackagedStegoFile
{
    private const int Magic = 0x31475343; // CGS1
    private const int Version = 1;
    public static void WritePackage(string outputPath, PackagedStegoHeader header, string payloadFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(header.OriginalFileName ?? string.Empty);
        writer.Write(header.OriginalByteLength);
        writer.Write(header.EtalonBitLength);
        writer.Write(header.Etalons.Length);
        foreach (string etalon in header.Etalons)
        writer.Write(etalon);
        writer.Write(header.KeyVectors.Length);
        foreach (byte[] keyVector in header.KeyVectors)
        {
            writer.Write(keyVector.Length);
            writer.Write(keyVector);
        }
        writer.Write(header.PayloadLength);
        writer.Flush();
        using var payload = new FileStream(payloadFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        payload.CopyTo(output);
    }
    public static PackagedStegoHeader ReadHeaderAndExtractPayload(string packagePath, string payloadOutputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadOutputPath);
        using var input = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        int magic = reader.ReadInt32();
        if (magic != Magic)
        throw new InvalidDataException("Файл не является пакетом CpuGpuStego или повреждён.");
        int version = reader.ReadInt32();
        if (version != Version)
        throw new InvalidDataException($"Неподдерживаемая версия пакета: {version}.");
        string originalFileName = reader.ReadString();
        long originalByteLength = reader.ReadInt64();
        int etalonBitLength = reader.ReadInt32();
        int etalonCount = reader.ReadInt32();
        if (etalonCount <= 0)
        throw new InvalidDataException("Некорректное количество эталонов в пакете.");
        string[] etalons = new string[etalonCount];
        for (int i = 0; i < etalonCount; i++)
        etalons[i] = reader.ReadString();
        int keyCount = reader.ReadInt32();
        if (keyCount != etalonCount)
        throw new InvalidDataException("Количество ключей не совпадает с количеством эталонов.");
        byte[][] keyVectors = new byte[keyCount][];
        for (int i = 0; i < keyCount; i++)
        {
            int length = reader.ReadInt32();
            if (length <= 0)
            throw new InvalidDataException("Некорректная длина ключевого вектора.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            throw new EndOfStreamException("Файл пакета повреждён: не удалось полностью прочитать ключевой вектор.");
            keyVectors[i] = bytes;
        }
        long payloadLength = reader.ReadInt64();
        if (payloadLength < 0)
        throw new InvalidDataException("Некорректная длина стего-полезной нагрузки.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(payloadOutputPath))!);
        using var payloadOutput = new FileStream(payloadOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        CopyExactly(input, payloadOutput, payloadLength);
        if (payloadOutput.Length != payloadLength)
        throw new InvalidDataException("Файл пакета повреждён: длина извлечённой стего-нагрузки не совпадает с ожидаемой.");
        return new PackagedStegoHeader
        {
            OriginalFileName = originalFileName,
            OriginalByteLength = originalByteLength,
            EtalonBitLength = etalonBitLength,
            Etalons = etalons,
            KeyVectors = keyVectors,
            PayloadLength = payloadLength,
        };
    }
    public static BitVector[] RestoreKeys(PackagedStegoHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        var keys = new BitVector[header.KeyVectors.Length];
        for (int i = 0; i < header.KeyVectors.Length; i++)
        keys[i] = new BitVector(header.KeyVectors[i], header.EtalonBitLength);
        return keys;
    }
    private static void CopyExactly(Stream input, Stream output, long bytesToCopy)
    {
        byte[] buffer = new byte[1024 * 1024];
        long remaining = bytesToCopy;
        while (remaining > 0)
        {
            int chunkSize = (int)Math.Min(buffer.Length, remaining);
            int read = input.Read(buffer, 0, chunkSize);
            if (read == 0)
            throw new EndOfStreamException("Файл пакета повреждён: стего-нагрузка обрывается раньше ожидаемого.");
            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }
}
public class GpuStegoEncryptor : IDisposable
{
    private readonly Accelerator _accelerator;
    private readonly Action<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int> _kernel;
    private readonly MemoryBuffer1D<byte, Stride1D.Dense> _gpuMaskedEtalons;
    private readonly MemoryBuffer1D<byte, Stride1D.Dense> _gpuKeys;
    private readonly int _containerByteLen;
    private readonly int _hiddenByteLen;
    private const int BLOCK_SIZE = 512 * 1024;
    public GpuStegoEncryptor(Accelerator accelerator, AssocStego stego)
    {
        _accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
        _containerByteLen = stego.ContainerByteLength;
        _hiddenByteLen = stego.HiddenByteLength;
        _gpuMaskedEtalons = accelerator.Allocate1D<byte>(stego.FlattenedMaskedEtalonsBytes);
        _gpuKeys = accelerator.Allocate1D<byte>(stego.FlattenedKeysBytes);
        _kernel = accelerator.LoadAutoGroupedStreamKernel<
        Index1D,
        ArrayView<byte>,
        ArrayView<byte>,
        ArrayView<byte>,
        ArrayView<byte>,
        int,
        int,
        int>(EncryptKernel);
    }
    public void EncryptToStream(byte[] input, string outputPath)
    {
        if (input == null)
        throw new ArgumentNullException(nameof(input));
        using var outputStream = CreateSequentialWriteStream(outputPath);
        int totalBlocks = (input.Length + BLOCK_SIZE - 1) / BLOCK_SIZE;
        byte[] fullInputBlock = new byte[BLOCK_SIZE];
        byte[] fullOutputBlock = new byte[BLOCK_SIZE * _hiddenByteLen];
        Console.WriteLine($"Обработка {totalBlocks} блоков по {BLOCK_SIZE} байт...");
        for (int blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
        {
            int offset = blockIndex * BLOCK_SIZE;
            int currentBlockSize = Math.Min(BLOCK_SIZE, input.Length - offset);
            int currentOutputSize = currentBlockSize * _hiddenByteLen;
            if (blockIndex % 10 == 0)
            {
                Console.WriteLine($"Обработан блок {blockIndex + 1}/{totalBlocks} ({(blockIndex + 1) * 100.0 / totalBlocks:F1}%)");
            }
            byte[] inputBlock = currentBlockSize == BLOCK_SIZE ? fullInputBlock : new byte[currentBlockSize];
            byte[] outputBlock = currentOutputSize == fullOutputBlock.Length ? fullOutputBlock : new byte[currentOutputSize];
            Buffer.BlockCopy(input, offset, inputBlock, 0, currentBlockSize);
            using var gpuInput = _accelerator.Allocate1D<byte>(currentBlockSize);
            using var gpuOutput = _accelerator.Allocate1D<byte>(currentOutputSize);
            gpuInput.CopyFromCPU(inputBlock);
            int blockSeed = RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);
            _kernel(
            currentBlockSize,
            gpuInput.View,
            gpuOutput.View,
            _gpuMaskedEtalons.View,
            _gpuKeys.View,
            _containerByteLen,
            offset,
            blockSeed);
            _accelerator.Synchronize();
            gpuOutput.CopyToCPU(outputBlock);
            outputStream.Write(outputBlock, 0, currentOutputSize);
        }
    }
    private static FileStream CreateSequentialWriteStream(string path) =>
    new FileStream(path, new FileStreamOptions
    {
        Mode = FileMode.Create,
        Access = FileAccess.Write,
        Share = FileShare.None,
        BufferSize = 1 << 20,
        Options = FileOptions.SequentialScan
    });
    private static void EncryptKernel(
    Index1D index,
    ArrayView<byte> input,
    ArrayView<byte> output,
    ArrayView<byte> maskedEtalons,
    ArrayView<byte> keys,
    int containerByteLen,
    int globalInputOffset,
    int blockSeed)
    {
        byte value = input[index];
        int d2 = value / 100;
        int rem = value % 100;
        int d1 = rem / 10;
        int d0 = rem % 10;
        int globalIndex = globalInputOffset + index;
        int containerBase = index * 3;
        ProcessDigit(output, maskedEtalons, keys, containerByteLen, containerBase, d2, 0, globalIndex, blockSeed);
        ProcessDigit(output, maskedEtalons, keys, containerByteLen, containerBase, d1, 1, globalIndex, blockSeed);
        ProcessDigit(output, maskedEtalons, keys, containerByteLen, containerBase, d0, 2, globalIndex, blockSeed);
    }
    private static void ProcessDigit(
    ArrayView<byte> output,
    ArrayView<byte> maskedEtalons,
    ArrayView<byte> keys,
    int containerByteLen,
    int containerBase,
    int digit,
    int digitIndex,
    int globalIndex,
    int blockSeed)
    {
        int containerIdx = containerBase + digitIndex;
        int outputOffset = containerIdx * containerByteLen;
        int etalonOffset = digit * containerByteLen;
        for (int i = 0; i < containerByteLen; i++)
        {
            byte keyByte = keys[etalonOffset + i];
            byte maskedEtalonByte = maskedEtalons[etalonOffset + i];
            byte randomByte = PseudoRandomByte(globalIndex, digitIndex, i, blockSeed);
            output[outputOffset + i] = (byte)(maskedEtalonByte | (randomByte & (byte)~keyByte));
        }
    }
    private static byte PseudoRandomByte(int globalIndex, int digitIndex, int byteIndex, int blockSeed)
    {
        uint x = (uint)blockSeed;
        x += (uint)(globalIndex + 1) * 747796405u;
        x ^= (uint)(digitIndex + 1) * 2891336453u;
        x += (uint)(byteIndex + 1) * 277803737u;
        x ^= x >> 16;
        x *= 2246822519u;
        x ^= x >> 13;
        x *= 3266489917u;
        x ^= x >> 16;
        return (byte)x;
    }
    public void Dispose()
    {
        _gpuMaskedEtalons?.Dispose();
        _gpuKeys?.Dispose();
    }
}
public class GpuStegoDecryptor : IDisposable
{
    private readonly Accelerator _accelerator;
    private readonly Action<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, int, int, int> _kernel;
    private readonly MemoryBuffer1D<byte, Stride1D.Dense> _gpuMaskedEtalons;
    private readonly MemoryBuffer1D<byte, Stride1D.Dense> _gpuKeys;
    private readonly int _containerByteLen;
    private readonly int _hiddenByteLen;
    private readonly int _etalonCount;
    private const int BLOCK_SIZE = 512 * 1024;
    public GpuStegoDecryptor(Accelerator accelerator, AssocStego stego)
    {
        _accelerator = accelerator ?? throw new ArgumentNullException(nameof(accelerator));
        _containerByteLen = stego.ContainerByteLength;
        _hiddenByteLen = stego.HiddenByteLength;
        _etalonCount = stego.etalons.Length;
        _gpuMaskedEtalons = accelerator.Allocate1D<byte>(stego.FlattenedMaskedEtalonsBytes);
        _gpuKeys = accelerator.Allocate1D<byte>(stego.FlattenedKeysBytes);
        _kernel = accelerator.LoadAutoGroupedStreamKernel<
        Index1D,
        ArrayView<byte>,
        ArrayView<byte>,
        ArrayView<byte>,
        ArrayView<byte>,
        int,
        int,
        int>(DecryptKernel);
    }
    public byte[] DecryptFromFile(string filePath, int expectedOutputLength)
    {
        var result = new byte[expectedOutputLength];
        using var inputStream = CreateSequentialReadStream(filePath);
        int totalBlocks = (expectedOutputLength + BLOCK_SIZE - 1) / BLOCK_SIZE;
        byte[] fullInputBlock = new byte[BLOCK_SIZE * _hiddenByteLen];
        byte[] fullOutputBlock = new byte[BLOCK_SIZE];
        Console.WriteLine($"Обработка {totalBlocks} блоков...");
        for (int blockIndex = 0; blockIndex < totalBlocks; blockIndex++)
        {
            int outputOffset = blockIndex * BLOCK_SIZE;
            int currentOutputBlockSize = Math.Min(BLOCK_SIZE, expectedOutputLength - outputOffset);
            int currentInputBlockSize = currentOutputBlockSize * _hiddenByteLen;
            if (blockIndex % 10 == 0)
            {
                Console.WriteLine($"Обработан блок {blockIndex + 1}/{totalBlocks} ({(blockIndex + 1) * 100.0 / totalBlocks:F1}%)");
            }
            byte[] inputBlock = currentInputBlockSize == fullInputBlock.Length ? fullInputBlock : new byte[currentInputBlockSize];
            byte[] outputBlock = currentOutputBlockSize == fullOutputBlock.Length ? fullOutputBlock : new byte[currentOutputBlockSize];
            ReadExactly(inputStream, inputBlock, currentInputBlockSize, blockIndex);
            using var gpuInput = _accelerator.Allocate1D<byte>(currentInputBlockSize);
            using var gpuOutput = _accelerator.Allocate1D<byte>(currentOutputBlockSize);
            gpuInput.CopyFromCPU(inputBlock);
            _kernel(
            currentOutputBlockSize,
            gpuInput.View,
            gpuOutput.View,
            _gpuMaskedEtalons.View,
            _gpuKeys.View,
            _containerByteLen,
            3,
            _etalonCount);
            _accelerator.Synchronize();
            gpuOutput.CopyToCPU(outputBlock);
            Buffer.BlockCopy(outputBlock, 0, result, outputOffset, currentOutputBlockSize);
        }
        return result;
    }
    private static FileStream CreateSequentialReadStream(string path) =>
    new FileStream(path, new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.Read,
        BufferSize = 1 << 20,
        Options = FileOptions.SequentialScan
    });
    private static void ReadExactly(FileStream stream, byte[] buffer, int count, int blockIndex)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int bytesRead = stream.Read(buffer, totalRead, count - totalRead);
            if (bytesRead == 0)
            throw new InvalidOperationException($"Не удалось прочитать блок {blockIndex}");
            totalRead += bytesRead;
        }
    }
    private static void DecryptKernel(
    Index1D index,
    ArrayView<byte> input,
    ArrayView<byte> output,
    ArrayView<byte> maskedEtalons,
    ArrayView<byte> keys,
    int containerByteLen,
    int containersPerByte,
    int etalonCount)
    {
        int inputBase = index * containersPerByte * containerByteLen;
        int d2 = DecodeContainer(input, maskedEtalons, keys, containerByteLen, inputBase, 0, etalonCount);
        int d1 = DecodeContainer(input, maskedEtalons, keys, containerByteLen, inputBase, 1, etalonCount);
        int d0 = DecodeContainer(input, maskedEtalons, keys, containerByteLen, inputBase, 2, etalonCount);
        output[index] = (byte)(d2 * 100 + d1 * 10 + d0);
    }
    private static int DecodeContainer(
    ArrayView<byte> input,
    ArrayView<byte> maskedEtalons,
    ArrayView<byte> keys,
    int containerByteLen,
    int inputBase,
    int containerIndex,
    int etalonCount)
    {
        int containerOffset = inputBase + containerIndex * containerByteLen;
        for (int etalonIdx = 0; etalonIdx < etalonCount; etalonIdx++)
        {
            bool matches = true;
            int etalonOffset = etalonIdx * containerByteLen;
            for (int i = 0; i < containerByteLen; i++)
            {
                byte containerByte = input[containerOffset + i];
                byte keyByte = keys[etalonOffset + i];
                if ((containerByte & keyByte) != maskedEtalons[etalonOffset + i])
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            return etalonIdx;
        }
        return 0;
    }
    public void Dispose()
    {
        _gpuMaskedEtalons?.Dispose();
        _gpuKeys?.Dispose();
    }
}
internal static class EtalonFactory
{
    public const int DefaultEtalonCount = 10;
    public const int DefaultEtalonBitLength = 78;
    private const string DefaultSeedPhrase = "DiplomaMag.DefaultEtalons.v1";
    public static string[] CreateDefaultEtalons10() =>
    CreateDeterministicEtalons(DefaultEtalonCount, DefaultEtalonBitLength, DefaultSeedPhrase);
    public static string[] CreateDeterministicEtalons(int count, int bitLength, string seedPhrase)
    {
        if (count <= 0)
        throw new ArgumentOutOfRangeException(nameof(count));
        if (bitLength <= 1)
        throw new ArgumentOutOfRangeException(nameof(bitLength));
        if (string.IsNullOrWhiteSpace(seedPhrase))
        throw new ArgumentException("Не задана seed-фраза для генерации эталонов.", nameof(seedPhrase));
        string[] result = new string[count];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int nonce = 0;
            while (true)
            {
                string etalon = CreateBitString(bitLength, $"{seedPhrase}:{i}:{nonce}");
                if (IsValidEtalon(etalon) && seen.Add(etalon))
                {
                    result[i] = etalon;
                    break;
                }
                nonce++;
            }
        }
        return result;
    }
    private static string CreateBitString(int bitLength, string seed)
    {
        var builder = new StringBuilder(bitLength);
        int round = 0;
        while (builder.Length < bitLength)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{round}"));
            foreach (byte value in hash)
            {
                for (int bit = 0; bit < 8 && builder.Length < bitLength; bit++)
                builder.Append(((value >> bit) & 1) == 1 ? '1' : '0');
            }
            round++;
        }
        return builder.ToString();
    }
    private static bool IsValidEtalon(string value)
    {
        bool hasZero = false;
        bool hasOne = false;
        foreach (char ch in value)
        {
            if (ch == '0')
            hasZero = true;
            else if (ch == '1')
            hasOne = true;
            else
            return false;
            if (hasZero && hasOne)
            return true;
        }
        return false;
    }
}
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
public class AssocStego
{
    public readonly BitVector[] etalons;
    public readonly int etalonLength;
    private readonly BitVector zeroVector;
    private readonly List<int>[] maskUnitPositions;
    private readonly int containerByteLength;
    private readonly byte[] flattenedEtalons;
    private byte[] flattenedKeys = Array.Empty<byte>();
    private byte[] flattenedMaskedEtalons = Array.Empty<byte>();
    private BitVector[] key;
    public IReadOnlyList<int>[] MaskUnitPositions => maskUnitPositions;
    public int ContainerByteLength => containerByteLength;
    public int HiddenByteLength => containerByteLength * 3;
    public byte[] FlattenedEtalonsBytes => flattenedEtalons;
    public byte[] FlattenedKeysBytes => key == null
    ? throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.")
    : flattenedKeys;
    public byte[] FlattenedMaskedEtalonsBytes => key == null
    ? throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.")
    : flattenedMaskedEtalons;
    public BitVector[] Key
    {
        get => key ?? throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.");
        set
        {
            if (value is null)
            throw new ArgumentNullException(nameof(value));
            if (value.Length != etalons.Length)
            throw new ArgumentException("Длина ключа должна совпадать с количеством эталонов.", nameof(value));
            key = value;
            RefreshKeyCache();
        }
    }
    public AssocStego(params string[] e)
    {
        if (e is null || e.Length == 0)
        throw new ArgumentException("Не передано ни одного эталона.", nameof(e));
        etalonLength = e[0].Length;
        if (e.Any(s => s is null || s.Length != etalonLength))
        throw new ArgumentException("Все эталоны должны иметь одинаковую длину.");
        etalons = new BitVector[e.Length];
        maskUnitPositions = new List<int>[e.Length];
        for (int i = 0; i < e.Length; i++)
        maskUnitPositions[i] = new List<int>();
        containerByteLength = (etalonLength + 7) >> 3;
        zeroVector = new BitVector((etalonLength + 7) & ~7);
        flattenedEtalons = new byte[e.Length * containerByteLength];
        for (int i = 0; i < e.Length; i++)
        {
            etalons[i] = new BitVector(e[i]);
            var bytes = etalons[i].ToByteArray();
            Buffer.BlockCopy(bytes, 0, flattenedEtalons, i * containerByteLength, containerByteLength);
        }
    }
    public BitVector[] CreateKey()
    {
        var eIndexes = Enumerable.Range(0, etalons.Length).ToList();
        CreateKey(eIndexes);
        var masks = new BitVector[etalons.Length];
        for (int i = 0; i < etalons.Length; i++)
        masks[i] = CreateVector(etalonLength, maskUnitPositions[i].ToArray());
        Key = masks;
        return masks;
    }
    public void CreateKey(List<int> eIndexes, bool firstStepPermutation = true, bool resetMaskUnitPositions = true)
    {
        if (resetMaskUnitPositions)
        ResetMaskUnitPositions();
        if (eIndexes is null)
        throw new ArgumentNullException(nameof(eIndexes));
        if (eIndexes.Count < 2)
        return;
        BitVector a_0 = new(etalonLength);
        BitVector a_1 = new(etalonLength);
        var c = firstStepPermutation ? CreateCryptoPermutation(eIndexes) : eIndexes;
        var d_1 = new List<int> { c[0] };
        a_0 = etalons[c[0]] ^ etalons[c[1]];
        for (int i = 0; i < c.Count - 2; i++)
        {
            a_1 = etalons[c[0]] ^ etalons[c[i + 2]];
            var a_3 = a_0 & a_1;
            if (a_3 == zeroVector)
            d_1.Add(c[i + 2]);
            else
            a_0 = a_3;
        }
        int randomOneBitIndex = GetRandomOneBitIndex(a_0);
        foreach (var idx in c)
        maskUnitPositions[idx].Add(randomOneBitIndex);
        var d_2 = c.Except(d_1).ToList();
        Parallel.Invoke(
        () => CreateKey(d_1, true, false),
        () => CreateKey(d_2, true, false)
        );
    }
    public void SaveKeyToFile(string filePath, BitVector[] keyToSave)
    {
        if (keyToSave == null)
        throw new ArgumentNullException(nameof(keyToSave));
        if (keyToSave.Length != etalons.Length)
        throw new ArgumentException("Длина массива ключей не совпадает с числом эталонов.");
        using var fs = File.Create(filePath);
        using var bw = new BinaryWriter(fs);
        const int MAGIC = 0x41534B59;
        bw.Write(MAGIC);
        bw.Write(keyToSave.Length);
        bw.Write(etalonLength);
        for (int i = 0; i < keyToSave.Length; i++)
        {
            byte[] bytes = keyToSave[i].ToByteArray();
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }
    }
    public BitVector[] LoadKeyFromFile(string filePath)
    {
        using var fs = File.OpenRead(filePath);
        using var br = new BinaryReader(fs);
        const int MAGIC = 0x41534B59;
        int magic = br.ReadInt32();
        if (magic != MAGIC)
        throw new InvalidDataException("Неверный формат файла ключа (magic).");
        int keyCount = br.ReadInt32();
        int bitLength = br.ReadInt32();
        if (bitLength != etalonLength)
        throw new InvalidDataException($"Длина битового вектора в файле ({bitLength}) не совпадает с etalonLength ({etalonLength}).");
        if (keyCount != etalons.Length)
        throw new InvalidDataException($"Количество ключей в файле ({keyCount}) не совпадает с количеством эталонов ({etalons.Length}).");
        var loadedKey = new BitVector[keyCount];
        for (int i = 0; i < keyCount; i++)
        {
            int len = br.ReadInt32();
            byte[] bytes = br.ReadBytes(len);
            if (bytes.Length != len)
            throw new EndOfStreamException("Файл ключа повреждён: не хватает данных.");
            loadedKey[i] = new BitVector(bytes, bitLength);
        }
        key = loadedKey;
        RefreshKeyCache();
        return loadedKey;
    }
    private void RefreshKeyCache()
    {
        if (key == null)
        throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.");
        flattenedKeys = new byte[etalons.Length * containerByteLength];
        flattenedMaskedEtalons = new byte[etalons.Length * containerByteLength];
        for (int i = 0; i < etalons.Length; i++)
        {
            byte[] keyBytes = key[i].ToByteArray();
            int offset = i * containerByteLength;
            Buffer.BlockCopy(keyBytes, 0, flattenedKeys, offset, containerByteLength);
            for (int j = 0; j < containerByteLength; j++)
            flattenedMaskedEtalons[offset + j] = (byte)(flattenedEtalons[offset + j] & keyBytes[j]);
        }
    }
    public void HideByteToBuffer(byte value, Span<byte> destination)
    {
        if (destination.Length < HiddenByteLength)
        throw new ArgumentException("Буфер назначения слишком мал.", nameof(destination));
        if (key == null)
        throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.");
        int v = value;
        int d2 = v / 100;
        int rem = v % 100;
        int d1 = rem / 10;
        int d0 = rem % 10;
        HideDigitToBuffer(d2, destination.Slice(0, containerByteLength));
        HideDigitToBuffer(d1, destination.Slice(containerByteLength, containerByteLength));
        HideDigitToBuffer(d0, destination.Slice(containerByteLength * 2, containerByteLength));
    }
    public byte DiscloseByteFromBuffer(ReadOnlySpan<byte> hidden)
    {
        if (hidden.Length < HiddenByteLength)
        throw new ArgumentException("Неверная длина скрытого блока для 10 эталонов.", nameof(hidden));
        if (key == null)
        throw new InvalidOperationException("Ключ ещё не сгенерирован и не задан.");
        int d2 = DecodeDigit(hidden.Slice(0, containerByteLength));
        int d1 = DecodeDigit(hidden.Slice(containerByteLength, containerByteLength));
        int d0 = DecodeDigit(hidden.Slice(containerByteLength * 2, containerByteLength));
        if (d2 < 0 || d1 < 0 || d0 < 0)
        throw new InvalidOperationException("Не удалось распознать десятичную цифру.");
        int value = d2 * 100 + d1 * 10 + d0;
        if (value > 255)
        throw new InvalidOperationException("Ошибка: декодировано число > 255.");
        return (byte)value;
    }
    private void HideDigitToBuffer(int digit, Span<byte> destination)
    {
        RandomNumberGenerator.Fill(destination);
        int offset = digit * containerByteLength;
        for (int i = 0; i < containerByteLength; i++)
        {
            byte randomByte = destination[i];
            destination[i] = (byte)(randomByte ^ ((randomByte ^ flattenedEtalons[offset + i]) & flattenedKeys[offset + i]));
        }
    }
    private int DecodeDigit(ReadOnlySpan<byte> source)
    {
        for (int etalonIdx = 0; etalonIdx < etalons.Length; etalonIdx++)
        {
            bool matches = true;
            int offset = etalonIdx * containerByteLength;
            for (int i = 0; i < containerByteLength; i++)
            {
                if ((source[i] & flattenedKeys[offset + i]) != flattenedMaskedEtalons[offset + i])
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            return etalonIdx;
        }
        return -1;
    }
    public void ValidateKeysOrThrow(BitVector[] key)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < etalons.Length; i++)
        {
            var sig = etalons[i] & key[i];
            string sigStr = sig.ToString();
            if (map.TryGetValue(sigStr, out int j))
            throw new InvalidOperationException($"Неоднозначные ключи: эталоны {j} и {i} имеют одинаковую подпись по своим маскам.");
            map[sigStr] = i;
        }
    }
    public int DiscloseEtalon(BitVector stegoContainer)
    {
        for (int i = 0; i < etalons.Length; i++)
        {
            if ((stegoContainer & key[i]) == (etalons[i] & key[i]))
            return i;
        }
        return -1;
    }
    public BitVector HideEtalon(int eIndex)
    {
        BitVector container = GenerateContainer(etalonLength);
        return container ^ ((container ^ etalons[eIndex]) & key[eIndex]);
    }
    public BitVector GenerateContainer(int bitLength)
    {
        int byteCount = (bitLength + 7) >> 3;
        byte[] randomBytes = new byte[byteCount];
        RandomNumberGenerator.Fill(randomBytes);
        return new BitVector(randomBytes, bitLength);
    }
    int GetRandomOneBitIndex(BitVector v)
    {
        int count = 0;
        int selectedIndex = -1;
        for (int i = 0; i < v.BitLength; i++)
        {
            if (!v.GetBit(i))
            continue;
            count++;
            if (RandomNumberGenerator.GetInt32(count) == 0)
            selectedIndex = i;
        }
        if (selectedIndex < 0)
        throw new InvalidOperationException("Во входном векторе отсутствуют единичные биты.");
        return selectedIndex;
    }
    List<int> CreateCryptoPermutation(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
    void ResetMaskUnitPositions()
    {
        foreach (var list in maskUnitPositions)
        list.Clear();
    }
    BitVector CreateVector(int bitLength, params int[] nonZeroIndexes)
    {
        int bufferLength = (bitLength + 7) >> 3;
        byte[] bitBuffer = new byte[bufferLength];
        foreach (var bitIndex in nonZeroIndexes)
        {
            if (bitIndex >= bitLength)
            throw new ArgumentOutOfRangeException(nameof(nonZeroIndexes), "Индекс бита выходит за пределы диапазона.");
            int byteIndex = bufferLength - 1 - (bitIndex >> 3);
            int bitOffset = bitIndex & 7;
            byte mask = (byte)(1 << bitOffset);
            bitBuffer[byteIndex] = (byte)(bitBuffer[byteIndex] & ~mask);
            bitBuffer[byteIndex] |= mask;
        }
        return new BitVector(bitBuffer, bitLength);
    }
}
internal sealed class AcceleratorSession : IDisposable
{
    public Context? Context { get; }
    public Accelerator? Accelerator { get; }
    public string Description { get; }
    public string Message { get; }
    public bool IsCpuFallback => Accelerator == null;
    public AcceleratorSession(Context? context, Accelerator? accelerator, string description, string message)
    {
        Context = context;
        Accelerator = accelerator;
        Description = description;
        Message = message;
    }
    public void Dispose()
    {
        Accelerator?.Dispose();
        Context?.Dispose();
    }
}
internal static class AcceleratorSelector
{
    public static AcceleratorSession Create(ExecutionMode mode)
    {
        if (mode == ExecutionMode.Cpu)
        {
            return new AcceleratorSession(null, null, "CPU", "Выбран режим чистого CPU.");
        }
        var context = Context.Create(builder => builder
        .Cuda()
        .OpenCL()
        .EnableAlgorithms()
        .Optimize(OptimizationLevel.O2));
        Device? selectedDevice = mode switch
        {
            ExecutionMode.NvidiaDiscreteGpu => FindBestCudaDevice(context),
            ExecutionMode.IntegratedGpu => FindBestIntegratedOpenClDevice(context),
            ExecutionMode.AmdDiscreteGpu => FindBestAmdDiscreteOpenClDevice(context),
            _ => null,
        };
        if (selectedDevice == null)
        {
            string availableDevices = DescribeDevices(context);
            context.Dispose();
            return new AcceleratorSession(
            null,
            null,
            "CPU",
            $"Подходящий графический ускоритель не найден. Выполняется fallback на CPU.\\nДоступные устройства:\\n{availableDevices}");
        }
        try
        {
            var accelerator = selectedDevice.CreateAccelerator(context);
            return new AcceleratorSession(
            context,
            accelerator,
            selectedDevice.ToString() ?? accelerator.Name,
            $"Используется акселератор: {selectedDevice}");
        }
        catch (Exception ex)
        {
            string deviceDescription = selectedDevice.ToString() ?? "неизвестное устройство";
            context.Dispose();
            return new AcceleratorSession(
            null,
            null,
            "CPU",
            $"Не удалось инициализировать выбранный GPU ({deviceDescription}). Выполняется fallback на CPU. Причина: {ex.Message}");
        }
    }
    private static Device? FindBestCudaDevice(Context context)
    {
        Device? best = null;
        long bestMemory = long.MinValue;
        foreach (Device device in context.GetCudaDevices())
        {
            if (device.MemorySize > bestMemory)
            {
                best = device;
                bestMemory = device.MemorySize;
            }
        }
        return best;
    }
    private static Device? FindBestIntegratedOpenClDevice(Context context)
    {
        Device? best = null;
        long bestMemory = long.MinValue;
        foreach (Device device in context.GetCLDevices())
        {
            string description = NormalizeDeviceText(device.ToString());
            if (!IsIntegratedOpenClDevice(description))
            continue;
            if (device.MemorySize > bestMemory)
            {
                best = device;
                bestMemory = device.MemorySize;
            }
        }
        return best;
    }
    private static Device? FindBestAmdDiscreteOpenClDevice(Context context)
    {
        Device? best = null;
        long bestMemory = long.MinValue;
        foreach (Device device in context.GetCLDevices())
        {
            string description = NormalizeDeviceText(device.ToString());
            if (!IsAmdDiscreteOpenClDevice(description))
            continue;
            if (device.MemorySize > bestMemory)
            {
                best = device;
                bestMemory = device.MemorySize;
            }
        }
        return best;
    }
    private static string DescribeDevices(Context context)
    {
        var lines = new List<string>();
        foreach (Device device in context.GetCudaDevices())
        lines.Add($"CUDA: {device}");
        foreach (Device device in context.GetCLDevices())
        lines.Add($"OpenCL: {device}");
        if (lines.Count == 0)
        return " устройств GPU не обнаружено";
        return string.Join(Environment.NewLine, lines.Select(x => " " + x));
    }
    private static string NormalizeDeviceText(string? text) =>
    (text ?? string.Empty).Trim().ToLowerInvariant();
    private static bool IsIntegratedOpenClDevice(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        return false;
        if (description.Contains("intel"))
        {
            return description.Contains("uhd") ||
            description.Contains("iris") ||
            description.Contains("xe") ||
            description.Contains("graphics");
        }
        if (description.Contains("amd") || description.Contains("advanced micro devices") || description.Contains("radeon"))
        {
            return description.Contains("radeon graphics") ||
            description.Contains("vega 3") ||
            description.Contains("vega 6") ||
            description.Contains("vega 7") ||
            description.Contains("vega 8") ||
            description.Contains("vega 11") ||
            description.Contains("vega 13") ||
            description.Contains("vega 16") ||
            description.Contains("680m") ||
            description.Contains("660m") ||
            description.Contains("780m") ||
            description.Contains("760m") ||
            description.Contains("740m") ||
            description.Contains("890m") ||
            description.Contains("880m") ||
            description.Contains("860m") ||
            (description.Contains("graphics") && !description.Contains("rx") && !description.Contains("pro"));
        }
        return false;
    }
    private static bool IsAmdDiscreteOpenClDevice(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        return false;
        bool isAmd = description.Contains("amd") ||
        description.Contains("advanced micro devices") ||
        description.Contains("radeon") ||
        description.Contains("firepro") ||
        description.Contains("instinct");
        if (!isAmd)
        return false;
        if (IsIntegratedOpenClDevice(description))
        return false;
        return description.Contains("rx") ||
        description.Contains("radeon pro") ||
        description.Contains("firepro") ||
        description.Contains("instinct") ||
        description.Contains("mi") ||
        !description.Contains("graphics");
    }
}
public enum ExecutionMode
{
    Cpu = 1,
    IntegratedGpu = 2,
    NvidiaDiscreteGpu = 3,
    AmdDiscreteGpu = 4,
}
