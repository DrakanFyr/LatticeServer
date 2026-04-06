using System.IO.Compression;
using System.Text;
using LatticeServer.Helpers;

namespace LatticeServer.Tests;

public class ZipTemplateReaderTests
{
    private static MemoryStream CreateTestZip(params (string name, string content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task ReadAsync_AllFourFiles_PopulatesAllFields()
    {
        using var ms = CreateTestZip(
            ("entity.json", """{"entityId":"<new_uuid>","isLive":true}"""),
            ("config.json", """{"displayName":"Test"}"""),
            ("task-configurations.json", "[]"));

        // We can't include a real DLL in a unit test, so we write raw bytes
        var ms2 = new MemoryStream();
        using (var zip = new ZipArchive(ms2, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e1 = zip.CreateEntry("entity.json");
            using (var w = new StreamWriter(e1.Open())) w.Write("""{"entityId":"<new_uuid>","isLive":true}""");
            var e2 = zip.CreateEntry("config.json");
            using (var w = new StreamWriter(e2.Open())) w.Write("""{"displayName":"Test"}""");
            var e3 = zip.CreateEntry("behavior.dll");
            using (var s = e3.Open()) s.Write(new byte[] { 0x4D, 0x5A }); // MZ header bytes
            var e4 = zip.CreateEntry("task-configurations.json");
            using (var w = new StreamWriter(e4.Open())) w.Write("[]");
        }
        ms2.Position = 0;

        var result = await ZipTemplateReader.ReadAsync(ms2);

        Assert.Contains("<new_uuid>", result.EntityJson);
        Assert.NotNull(result.ConfigJson);
        Assert.NotNull(result.BehaviorDll);
        Assert.Equal(2, result.BehaviorDll!.Length); // MZ bytes
        Assert.NotNull(result.TaskConfigurationsJson);
    }

    [Fact]
    public async Task ReadAsync_OnlyEntityJson_OptionalFieldsAreNull()
    {
        using var ms = CreateTestZip(("entity.json", """{"entityId":"<new_uuid>","isLive":true}"""));

        var result = await ZipTemplateReader.ReadAsync(ms);

        Assert.Contains("<new_uuid>", result.EntityJson);
        Assert.Null(result.ConfigJson);
        Assert.Null(result.BehaviorDll);
        Assert.Null(result.TaskConfigurationsJson);
    }

    [Fact]
    public async Task ReadAsync_MissingEntityJson_ThrowsInvalidDataException()
    {
        using var ms = CreateTestZip(("config.json", """{"displayName":"Test"}"""));

        await Assert.ThrowsAsync<InvalidDataException>(() => ZipTemplateReader.ReadAsync(ms));
    }

    [Fact]
    public async Task ReadAsync_WithEntryPrefix_ReadsFilesFromSubdirectory()
    {
        using var ms = CreateTestZip(
            ("assets/entity.json", """{"entityId":"<new_uuid>","isLive":true}"""),
            ("assets/config.json", """{"displayName":"Prefixed"}"""));

        var result = await ZipTemplateReader.ReadAsync(ms, entryPrefix: "assets/");

        Assert.Contains("<new_uuid>", result.EntityJson);
        Assert.NotNull(result.ConfigJson);
        Assert.Contains("Prefixed", result.ConfigJson!);
    }

    [Fact]
    public async Task ReadAsync_WithEntryPrefix_MissingEntityJson_Throws()
    {
        // Has entity.json at root but not under prefix
        using var ms = CreateTestZip(("entity.json", """{"entityId":"<new_uuid>"}"""));

        await Assert.ThrowsAsync<InvalidDataException>(() => ZipTemplateReader.ReadAsync(ms, entryPrefix: "assets/"));
    }

    [Fact]
    public async Task ReadAsync_EmptyStream_ThrowsInvalidDataException()
    {
        var ms = new MemoryStream(Array.Empty<byte>());

        await Assert.ThrowsAsync<InvalidDataException>(() => ZipTemplateReader.ReadAsync(ms));
    }

    [Fact]
    public async Task ReadAsync_CorruptStream_ThrowsInvalidDataException()
    {
        var ms = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip file"));

        await Assert.ThrowsAsync<InvalidDataException>(() => ZipTemplateReader.ReadAsync(ms));
    }
}
