using MongoDB.Bson;
using MongoDB.Driver;
using SatelliteData.Application.Pipeline;

namespace SatelliteData.Infrastructure.Mongo;

public sealed class MongoInstructionSeriesReader : IMongoInstructionSeriesReader
{
    public async Task<IReadOnlyList<InstructionHistoryPoint>> ReadHistoryAsync(
        string mongoUri,
        string databaseName,
        string collectionName,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyCollection<InstructionHistoryLookup> lookups,
        CancellationToken cancellationToken)
    {
        if (lookups.Count == 0)
        {
            return Array.Empty<InstructionHistoryPoint>();
        }

        try
        {
            var lookupByCmdId = lookups
                .GroupBy(x => x.CmdId)
                .ToDictionary(x => x.Key, x => x.First(), EqualityComparer<int>.Default);
            var cmdIds = lookupByCmdId.Keys.ToArray();

            var client = new MongoClient(mongoUri);
            var db = client.GetDatabase(databaseName);
            var col = db.GetCollection<BsonDocument>(collectionName);
            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.In("ci", cmdIds),
                Builders<BsonDocument>.Filter.Gte("et", windowStart.UtcDateTime),
                Builders<BsonDocument>.Filter.Lte("et", windowEnd.UtcDateTime));

            var cursor = await col.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Ascending("et"))
                .ToCursorAsync(cancellationToken);
            var result = new List<InstructionHistoryPoint>();
            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var doc in cursor.Current)
                {
                    if (!doc.TryGetValue("ci", out var ciVal) || !ciVal.IsNumeric)
                    {
                        continue;
                    }

                    var cmdId = ciVal.ToInt32();
                    if (!lookupByCmdId.TryGetValue(cmdId, out var lookup))
                    {
                        continue;
                    }

                    if (!doc.TryGetValue("et", out var etVal) || !etVal.IsValidDateTime)
                    {
                        continue;
                    }

                    var executeTime = MongoDateTimeMapper.ToLocalOffset(etVal);
                    result.Add(new InstructionHistoryPoint(
                        lookup.CommandId,
                        cmdId,
                        lookup.ChannelId,
                        executeTime));
                }
            }

            return result
                .OrderBy(x => x.ExecuteTime)
                .ToArray();
        }
        catch
        {
            return Array.Empty<InstructionHistoryPoint>();
        }
    }
}
