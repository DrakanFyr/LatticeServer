namespace LatticeServer.Models;

/// <summary>
/// The raw file contents of a template package, regardless of source (directory, ZIP, APK, upload).
/// </summary>
public record RawTemplateFiles(
    string EntityJson,
    string? ConfigJson,
    byte[]? BehaviorDll,
    string? TaskConfigurationsJson);
