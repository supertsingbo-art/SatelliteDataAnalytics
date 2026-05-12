using MongoDB.Bson;
using MongoDB.Driver;
using SatelliteData.Application.Pipeline;

namespace SatelliteData.Infrastructure.Mongo;

public sealed class MongoRawSeriesReader : IMongoRawSeriesReader
{
    public async Task<IReadOnlyList<RawSeriesPoint>> ReadSeriesAsync(
        string mongoUri,
        string databaseName,
        string collectionName,
        string tasookNo,
        string satelliteNo,
        string testBatchId,
        string paramId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = new MongoClient(mongoUri);
            var db = client.GetDatabase(databaseName);
            var col = db.GetCollection<BsonDocument>(collectionName);
            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("tasook_no", tasookNo),
                Builders<BsonDocument>.Filter.Eq("satellite_no", satelliteNo),
                Builders<BsonDocument>.Filter.Eq("test_batch_id", testBatchId),
                Builders<BsonDocument>.Filter.Eq("param_id", paramId),
                Builders<BsonDocument>.Filter.Gte("ts", windowStart.UtcDateTime),
                Builders<BsonDocument>.Filter.Lte("ts", windowEnd.UtcDateTime));

            var cursor = await col.Find(filter).Sort(Builders<BsonDocument>.Sort.Ascending("ts")).ToCursorAsync(cancellationToken);
            var list = new List<RawSeriesPoint>();
            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var doc in cursor.Current)
                {
                    var ts = doc.TryGetValue("ts", out var t) && t.IsValidDateTime
                        ? new DateTimeOffset(t.ToUniversalTime(), TimeSpan.Zero)
                        : default;
                    double v = 0;
                    if (doc.TryGetValue("value", out var val))
                    {
                        if (val.IsDouble) v = val.AsDouble;
                        else if (val.IsInt32) v = val.AsInt32;
                    }
                    else if (doc.TryGetValue("processed_value", out var pv) && pv.IsDouble)
                    {
                        v = pv.AsDouble;
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
