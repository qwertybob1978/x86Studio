using System.Text.RegularExpressions;

namespace ClassicMacIde.Core;

internal static class ClassicMacCIdentifier
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "auto", "break", "case", "char", "const", "continue", "default", "do",
        "double", "else", "enum", "extern", "float", "for", "goto", "if",
        "int", "long", "register", "return", "short", "signed", "sizeof",
        "static", "struct", "switch", "typedef", "union", "unsigned", "void",
        "volatile", "while", "inline", "restrict", "_Bool", "_Complex",
        "_Imaginary",
    };

    private static readonly Regex Pattern = new(
        "^[A-Za-z][A-Za-z0-9_]{0,30}$",
        RegexOptions.CultureInvariant);

    public static bool IsValid(string? identifier) =>
        identifier is not null &&
        Pattern.IsMatch(identifier) &&
        !Keywords.Contains(identifier);
}
