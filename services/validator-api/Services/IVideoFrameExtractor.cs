using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ValidatorApi.Services;

public record ExtractedFrame(int ElapsedMs, byte[] Rgba, int Width, int Height);

public interface IVideoFrameExtractor
{
    Task<IReadOnlyList<ExtractedFrame>> ExtractFramesAsync(
        Stream videoStream,
        int intervalMs,
        CancellationToken ct);
}

public interface IVideoMetadataReader
{
    Task<int?> GetRotationDegreesAsync(Stream videoStream, CancellationToken ct);
}
