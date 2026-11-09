namespace ClassicMacIde.Core;

public sealed record GeneratedMacForm(
    string CSource,
    string HeaderSource,
    string ResourceSource,
    string CMakeLists,
    string MainSource);
