using System.IO.Compression;
using LatticeServer.Models;

namespace LatticeServer.Helpers;

/// <summary>
/// Reads template file contents from a ZIP archive (or APK with an asset prefix).
/// </summary>
public static class ZipTemplateReader
{
    /// <summary>
    /// Reads template files from a stream containing a ZIP archive.
    /// </summary>
    /// <param name="stream">The stream to read from. Not closed after reading.</param>
    /// <param name="entryPrefix">Prefix prepended to each entry name when searching. Use "" for plain ZIPs, "assets/" for APKs.</param>
    public static async Task<RawTemplateFiles> ReadAsync(Stream stream, string entryPrefix = "")
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var entityEntry = zip.GetEntry($"{entryPrefix}entity.json")
            ?? throw new InvalidDataException("Template package is missing entity.json.");

        var entityJson = await ReadEntryAsync(entityEntry);

        string? configJson = null;
        var configEntry = zip.GetEntry($"{entryPrefix}config.json");
        if (configEntry != null)
            configJson = await ReadEntryAsync(configEntry);

        byte[]? behaviorDll = null;
        var dllEntry = zip.GetEntry($"{entryPrefix}behavior.dll");
        if (dllEntry != null)
            behaviorDll = await ReadEntryBytesAsync(dllEntry);

        string? taskConfigurationsJson = null;
        var taskConfigEntry = zip.GetEntry($"{entryPrefix}task-configurations.json");
        if (taskConfigEntry != null)
            taskConfigurationsJson = await ReadEntryAsync(taskConfigEntry);

        return new RawTemplateFiles(entityJson, configJson, behaviorDll, taskConfigurationsJson);
    }

    /// <summary>
    /// Reads template files from a ZIP file on disk.
    /// </summary>
    public static async Task<RawTemplateFiles> ReadAsync(string zipPath)
    {
        await using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await ReadAsync(fs);
    }

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry)
    {
        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        return await reader.ReadToEndAsync();
    }

    private static async Task<byte[]> ReadEntryBytesAsync(ZipArchiveEntry entry)
    {
        await using var entryStream = entry.Open();
        using var ms = new MemoryStream();
        await entryStream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
