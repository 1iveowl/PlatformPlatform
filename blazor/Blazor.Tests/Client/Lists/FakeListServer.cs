using Blazor.Client.Components.Lists;

namespace Blazor.Tests.Client.Lists;

// Serves pages of numbered rows with the account API's paging contract: an explicit page offset at or beyond the total
// number of pages is a 400. A search filter keeps the rows that contain it. Requests can be held open to control the order
// in which they complete.
public sealed class FakeListServer(int rowCount)
{
    private readonly Dictionary<int, TaskCompletionSource> _holds = new();

    public List<DataListRequest> Requests { get; } = [];

    public int RowCount { get; set; } = rowCount;

    public int? FailWithStatus { get; set; }

    public void Hold(int requestNumber)
    {
        _holds[requestNumber] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Release(int requestNumber)
    {
        _holds[requestNumber].SetResult();
    }

    public async Task<DataListFetchResult<string>> FetchAsync(DataListRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var rowsAtRequest = RowCount;
        if (_holds.TryGetValue(Requests.Count, out var hold)) await hold.Task.WaitAsync(cancellationToken);
        if (FailWithStatus is { } status) return DataListFetchResult<string>.Failure(status, "Validation failed.");

        var search = request.Filters.GetValueOrDefault("search") ?? "";
        var rows = Enumerable.Range(0, rowsAtRequest).Select(index => $"row-{index:D3}{search}").ToArray();
        if (request.SortOrder == SharedKernel.Persistence.SortOrder.Descending) rows = rows.Reverse().ToArray();
        var totalPages = (rows.Length + request.PageSize - 1) / request.PageSize;
        if (request.PageOffset > 0 && request.PageOffset >= totalPages)
        {
            return DataListFetchResult<string>.Failure(400, $"The page offset {request.PageOffset} is greater than the total number of pages.");
        }

        return DataListFetchResult<string>.Success(rows.Skip(request.PageOffset * request.PageSize).Take(request.PageSize).ToArray(), rows.Length);
    }
}
