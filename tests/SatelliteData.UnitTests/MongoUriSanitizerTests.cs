using SatelliteData.Domain.Assets;
using Xunit;

namespace SatelliteData.UnitTests;

public class MongoUriSanitizerTests
{
    [Theory]
    [InlineData("mongodb://127.0.0.1:27017/测试库", "测试库")]
    [InlineData("mongodb://127.0.0.1:27017/%E6%B5%8B%E8%AF%95%E5%BA%93", "测试库")]
    [InlineData("mongodb://user:pass@127.0.0.1:27017/mydb", "mydb")]
    [InlineData("mongodb+srv://cluster.example.net/sat_db", "sat_db")]
    public void ExtractDatabaseName_parses_path(string uri, string expected)
    {
        var actual = MongoUriSanitizer.ExtractDatabaseName(uri);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ExtractDatabaseName_does_not_leave_percent_encoding()
    {
        var uri = "mongodb://127.0.0.1:27017/%E6%B5%8B%E8%AF%95%E5%BA%93";
        var name = MongoUriSanitizer.ExtractDatabaseName(uri);
        Assert.Equal("测试库", name);
        Assert.DoesNotContain('%', name!);
    }

    [Fact]
    public void NormalizeDbName_repairs_latin1_mojibake()
    {
        var garbled = "\u00e6\u00b5\u008b\u00e8\u00af\u0095";
        Assert.Equal("测试", MongoUriSanitizer.NormalizeDbName(garbled));
    }
}
