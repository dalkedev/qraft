using Qraft.Contracts;

namespace Qraft.Application;

public sealed record StoredProcessing(Guid MessageId, string PayloadHash, InspectionProcessedV1 Result);

public interface IQualityStore
{
    Task<StoredProcessing?> FindAsync(Guid messageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InspectionProcessedV1>> RecentAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<InspectionProcessedV1> SaveAsync(
        InspectionSubmittedV1? submission, InspectionProcessedV1 result,
        string rawJson, string payloadHash, CancellationToken cancellationToken = default);
}
