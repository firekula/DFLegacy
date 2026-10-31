using System.Buffers.Binary;
using System.Numerics;

namespace DFLegacy.Manager;

/// <summary>Validate encrypted index and payload ranges without loading the archive into RAM.</summary>
internal static class PvfIndexValidator
{
    internal static async Task ValidateAsync(string path, byte[] header, long archiveLength, CancellationToken cancellationToken)
    {
        var treeLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(15, 4));
        var key = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(19, 4));
        var count = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(23, 4));
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        source.Position = header.Length;
        var reader = new TreeReader(source, treeLength, key);
        var entry = new byte[20];
        var bodyLength = archiveLength - header.Length - treeLength;
        for (uint index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await reader.ReadAsync(entry, cancellationToken);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(4, 4));
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(8, 4));
            var pathLength = BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(16, 4));
            if (length < 0 || pathLength <= 0 || pathLength > reader.Remaining
                || offset + (((long)length + 3) & ~3L) > bodyLength)
                throw new InvalidDataException($"Script.pvf 索引项 {index} 或对应数据不完整，原资源已保留。");
            await reader.SkipAsync(pathLength, cancellationToken);
        }
    }

    private sealed class TreeReader(Stream source, int length, uint key)
    {
        private readonly byte[] _buffer = new byte[65536];
        private int _position, _available, _loaded;
        internal long Remaining { get; private set; } = length;

        private async ValueTask FillAsync(CancellationToken token)
        {
            if (_position < _available) return;
            _available = Math.Min(_buffer.Length, length - _loaded); _position = 0;
            if (_available <= 0) throw new InvalidDataException("Script.pvf 索引提前结束。");
            await source.ReadExactlyAsync(_buffer.AsMemory(0, _available), token);
            for (var offset = 0; offset < _available; offset += 4)
            {
                var cipher = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(offset, 4));
                BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(offset, 4), BitOperations.RotateRight(cipher ^ key, 6));
            }
            _loaded += _available;
        }

        internal async ValueTask ReadAsync(Memory<byte> target, CancellationToken token)
        {
            if (target.Length > Remaining) throw new InvalidDataException("Script.pvf 索引项被截断。");
            while (!target.IsEmpty)
            {
                await FillAsync(token);
                var take = Math.Min(target.Length, _available - _position);
                _buffer.AsMemory(_position, take).CopyTo(target);
                _position += take; Remaining -= take; target = target[take..];
            }
        }

        internal async ValueTask SkipAsync(int count, CancellationToken token)
        {
            if (count < 0 || count > Remaining) throw new InvalidDataException("Script.pvf 路径超出索引范围。");
            while (count > 0)
            {
                await FillAsync(token);
                var take = Math.Min(count, _available - _position);
                _position += take; Remaining -= take; count -= take;
            }
        }
    }
}
