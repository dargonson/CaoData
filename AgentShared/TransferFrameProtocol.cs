using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentShared
{
    public static class TransferFrameProtocol
    {
        public const byte BinaryDownloadChunkMarker = 0x02;
        public const byte BinaryUploadChunkMarker = 0x03;
        private const int BinaryHeaderPrefixSize = 5;
        private const int StreamCopyBufferSize = 128 * 1024;
        private static readonly byte[] BinaryDownloadChunkMarkerBytes = { BinaryDownloadChunkMarker };
        private static readonly byte[] BinaryUploadChunkMarkerBytes = { BinaryUploadChunkMarker };

        public static async Task WriteJsonPacketAsync(Stream stream, SocketPacket packet, CancellationToken token = default)
        {
            byte[] dataBytes = JsonSerializer.SerializeToUtf8Bytes(packet);
            byte[] sizeBytes = BitConverter.GetBytes(dataBytes.Length);

            await stream.WriteAsync(sizeBytes, 0, sizeBytes.Length, token);
            await stream.WriteAsync(dataBytes, 0, dataBytes.Length, token);
            await stream.FlushAsync(token);
        }

        public static async Task WriteBinaryDownloadChunkAsync(
            Stream stream,
            FileChunkPacket header,
            byte[] buffer,
            int count,
            CancellationToken token = default)
        {
            await WriteBinaryChunkAsync(stream, BinaryDownloadChunkMarkerBytes, header, buffer, count, token);
        }

        public static async Task WriteBinaryUploadChunkAsync(
            Stream stream,
            FileChunkPacket header,
            byte[] buffer,
            int count,
            CancellationToken token = default)
        {
            await WriteBinaryChunkAsync(stream, BinaryUploadChunkMarkerBytes, header, buffer, count, token);
        }

        private static async Task WriteBinaryChunkAsync(
            Stream stream,
            byte[] markerBytes,
            FileChunkPacket header,
            byte[] buffer,
            int count,
            CancellationToken token)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            header.ChunkSize = count;
            header.Base64Data = string.Empty;

            byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(header);
            int frameSize = BinaryHeaderPrefixSize + headerBytes.Length + count;
            byte[] frameSizeBytes = BitConverter.GetBytes(frameSize);
            byte[] headerSizeBytes = BitConverter.GetBytes(headerBytes.Length);

            await stream.WriteAsync(frameSizeBytes, 0, frameSizeBytes.Length, token);
            await stream.WriteAsync(markerBytes, 0, markerBytes.Length, token);
            await stream.WriteAsync(headerSizeBytes, 0, headerSizeBytes.Length, token);
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length, token);

            if (count > 0)
            {
                await stream.WriteAsync(buffer, 0, count, token);
            }

            await stream.FlushAsync(token);
        }

        public static async Task<int> ReadExactOrEndAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token = default)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, token);
                if (read == 0)
                {
                    return totalRead;
                }

                totalRead += read;
            }

            return totalRead;
        }

        public static async Task ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken token = default)
        {
            int totalRead = await ReadExactOrEndAsync(stream, buffer, offset, count, token);
            if (totalRead != count)
            {
                throw new EndOfStreamException("Socket closed before the full frame was received.");
            }
        }

        public static SocketPacket DeserializeJsonFrame(byte firstByte, byte[] rentedBuffer, int frameSize)
        {
            rentedBuffer[0] = firstByte;
            string json = Encoding.UTF8.GetString(rentedBuffer, 0, frameSize);
            return JsonSerializer.Deserialize<SocketPacket>(json) ?? new SocketPacket();
        }

        public static async Task<(FileChunkPacket Header, int BodySize)> ReadBinaryChunkHeaderAsync(Stream stream, int frameSize, CancellationToken token = default)
        {
            if (frameSize < BinaryHeaderPrefixSize)
            {
                throw new InvalidDataException("Invalid binary download frame.");
            }

            byte[] headerSizeBytes = new byte[4];
            await ReadExactAsync(stream, headerSizeBytes, 0, headerSizeBytes.Length, token);

            int headerSize = BitConverter.ToInt32(headerSizeBytes, 0);
            int maxHeaderSize = frameSize - BinaryHeaderPrefixSize;
            if (headerSize < 0 || headerSize > maxHeaderSize)
            {
                throw new InvalidDataException("Invalid binary download header size.");
            }

            byte[] headerBytes = ArrayPool<byte>.Shared.Rent(headerSize);
            try
            {
                await ReadExactAsync(stream, headerBytes, 0, headerSize, token);
                FileChunkPacket header = JsonSerializer.Deserialize<FileChunkPacket>(headerBytes.AsSpan(0, headerSize)) ?? new FileChunkPacket();
                return (header, maxHeaderSize - headerSize);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(headerBytes);
            }
        }

        public static async Task CopyExactToAsync(Stream source, Stream destination, int bytesToCopy, CancellationToken token = default)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(StreamCopyBufferSize);
            try
            {
                int remaining = bytesToCopy;
                while (remaining > 0)
                {
                    int readSize = Math.Min(buffer.Length, remaining);
                    int read = await source.ReadAsync(buffer, 0, readSize, token);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("Socket closed before the chunk body was received.");
                    }

                    await destination.WriteAsync(buffer, 0, read, token);
                    remaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static async Task DrainExactAsync(Stream source, int bytesToDrain, CancellationToken token = default)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(StreamCopyBufferSize);
            try
            {
                int remaining = bytesToDrain;
                while (remaining > 0)
                {
                    int readSize = Math.Min(buffer.Length, remaining);
                    int read = await source.ReadAsync(buffer, 0, readSize, token);
                    if (read == 0)
                    {
                        throw new EndOfStreamException("Socket closed before the frame was drained.");
                    }

                    remaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
