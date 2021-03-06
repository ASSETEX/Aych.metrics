﻿using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace StackExchange.Metrics.Infrastructure
{
    internal static class MemoryExtensions
    {
#pragma warning disable RCS1231 // Make parameter ref read-only.
        internal static ArraySegment<byte> GetArray(this Memory<byte> buffer) => GetArray((ReadOnlyMemory<byte>)buffer);
        internal static ArraySegment<byte> GetArray(this ReadOnlyMemory<byte> buffer)
#pragma warning restore RCS1231 // Make parameter ref read-only.
        {
            if (!MemoryMarshal.TryGetArray(buffer, out var segment))
            {
                throw new InvalidOperationException("MemoryMarshal.TryGetArray<byte> could not provide an array");
            }
            return segment;
        }

        internal static Task WriteAsync(this Stream stream, ReadOnlyMemory<byte> buffer)
        {
            var arraySegment = buffer.GetArray();
            return stream.WriteAsync(arraySegment.Array, arraySegment.Offset, arraySegment.Count);
        }

        internal static Task WriteAsync(this Stream stream, ReadOnlySequence<byte> sequence)
        {
            if (sequence.IsSingleSegment)
            {
                return stream.WriteAsync(sequence.First);
            }

            return Awaited(stream, sequence);

            async Task Awaited(Stream s, ReadOnlySequence<byte> seq)
            {
                foreach (var segment in seq)
                {
                    await stream.WriteAsync(segment);
                }
            }
        }

        internal static ReadOnlySequence<byte> Trim(this ReadOnlySequence<byte> sequence, char element)
        {
            var firstByte = sequence.First.Span[0];
            if (firstByte == element)
            {
                sequence = sequence.Slice(1);
            }

            if (sequence.Length == 0)
            {
                return sequence;
            }

            var lastIndex = sequence.Length - 1;
            var endSequence = sequence.Slice(lastIndex, 1);
            var lastByte = endSequence.First.Span[0];
            if (lastByte == element)
            {
                sequence = sequence.Slice(0, lastIndex);
            }

            return sequence;
        }
    }
}
