namespace SatelliteData.Application.Tasks;

public sealed record TaskProcessedDataColumnDto(string ParamId, string Label);

public sealed record TaskProcessedDataCellDto(double? Value, bool IsOutlier);

public sealed record TaskProcessedDataRowDto(string Ts, IReadOnlyDictionary<string, TaskProcessedDataCellDto> Cells);

public sealed record TaskProcessedDataDto(
    Guid RunId,
    IReadOnlyList<TaskProcessedDataColumnDto> Columns,
    IReadOnlyList<TaskProcessedDataRowDto> Rows,
    long Total,
    int Page,
    int PageSize);
