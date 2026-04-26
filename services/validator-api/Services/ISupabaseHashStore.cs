using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ValidatorApi.Models;

namespace ValidatorApi.Services;

public interface ISupabaseHashStore
{
    Task<CaptureSession?> GetSessionAsync(string sessionId, CancellationToken ct);
    Task<IReadOnlyList<FrameHashRecord>> GetFrameHashesAsync(string sessionId, string? source, CancellationToken ct);
    Task InsertFrameHashesAsync(IReadOnlyList<FrameHashRecord> records, CancellationToken ct);
    Task SetReferenceSourceAsync(string sessionId, string referenceSource, CancellationToken ct);
}
