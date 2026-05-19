using MongoDB.Bson;
using MongoDB.Driver;
using SatelliteData.Application.Pipeline;

namespace SatelliteData.Infrastructure.Mongo;

public sealed class MongoPkgSeriesReader : IMongoPkgSeriesReader
{
    public async Task<IReadOnlyList<RawSeriesPoint>> ReadSeriesAsync(
        string mongoUri,
        string databaseName,
        int prmSysId,
        int paraId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
    {
        try
        {
            var collectionName = $"pkg{prmSysId}";
            var client = new MongoClient(mongoUri);
            var db = client.GetDatabase(databaseName);
            var col = db.GetCollection<BsonDocument>(collectionName);
            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("id", paraId),
                Builders<BsonDocument>.Filter.Gte("dt", windowStart.UtcDateTime),
                Builders<BsonDocument>.Filter.Lte("dt", windowEnd.UtcDateTime));

            var cursor = await col.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Ascending("dt"))
                .ToCursorAsync(cancellationToken);
            var list = new List<RawSeriesPoint>();
            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var doc in cursor.Current)
                {
                    var ts = doc.TryGetValue("dt", out var t) && t.IsValidDateTime
                        ? new DateTimeOffset(t.ToUniversalTime(), TimeSpan.Zero)
                        : default;
                    double v = 0;
                    if (doc.TryGetValue("pv", out var pv))
                    {
                        if (pv.IsDouble) v = pv.AsDouble;
                        else if (pv.IsInt32) v = pv.AsInt32;
                        else if (pv.IsInt64) v = pv.AsInt64;
                    }

                    list.Add(new RawSeriesPoint(ts, v));
                }
            }

            return list;
        }
        catch
        {
            return Array.Empty<RawSeriesPoint>();
        }
    }
}
