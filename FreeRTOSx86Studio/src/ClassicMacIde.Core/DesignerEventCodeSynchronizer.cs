using System.Text;
using System.Text.RegularExpressions;

namespace ClassicMacIde.Core;

/// <summary>Synchronizes designer event bindings with user-owned C handler source.</summary>
public static class DesignerEventCodeSynchronizer
{
    private static readonly Regex FunctionDeclarationPattern = new(
        @"(?m)^[ \t]*(?<storage>static[ \t]+)?(?<returnType>[A-Za-z_]\w*)[ \t]+" +
        @"(?<name>[A-Za-z_]\w*)[ \t]*\((?<parameters>[^)]*)\)\s*(?<terminator>[;{])",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Adds a no-op handler implementation when none exists, preserving compatible code.
    /// </summary>
    public static string EnsureHandlerImplementation(
        string source,
        string handlerName,
        string formTypeName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateIdentifier(handlerName, nameof(handlerName));
        ValidateIdentifier(formTypeName, nameof(formTypeName));

        foreach (Match declaration in FunctionDeclarationPattern.Matches(source))
        {
            if (!string.Equals(
                    declaration.Groups["name"].Value,
                    handlerName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(declaration.Groups["storage"].Value) ||
                declaration.Groups["returnType"].Value != "void" ||
                !HasCompatibleSignature(
                    declaration.Groups["parameters"].Value,
                    formTypeName))
            {
                throw new InvalidDataException(
                    $"'{handlerName}' already exists with a signature incompatible with " +
                    $"{formTypeName} form, short item.");
            }

            if (declaration.Groups["terminator"].Value == "{")
            {
                return source;
            }
        }

        return source.TrimEnd() + "\n\n" +
            $"void {handlerName}({formTypeName} form, short item)\n" +
            "{\n" +
            "    (void)form;\n" +
            "    (void)item;\n" +
            "}\n";
    }

    /// <summary>Updates bound handler parameter types when the project's form type changes.</summary>
    public static string UpdateHandlerFormTypes(
        string source,
        IEnumerable<string> handlerNames,
        string oldFormTypeName,
        string newFormTypeName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handlerNames);
        ValidateIdentifier(oldFormTypeName, nameof(oldFormTypeName));
        ValidateIdentifier(newFormTypeName, nameof(newFormTypeName));

        var boundHandlers = handlerNames.ToHashSet(StringComparer.Ordinal);
        foreach (var handlerName in boundHandlers)
        {
            ValidateIdentifier(handlerName, nameof(handlerNames));
        }
        if (boundHandlers.Count == 0 ||
            string.Equals(oldFormTypeName, newFormTypeName, StringComparison.Ordinal))
        {
            return source;
        }

        var oldTypePattern = new Regex(
            $@"^(\s*)(?:{Regex.Escape(oldFormTypeName)}|DialogPtr)\b",
            RegexOptions.CultureInvariant);
        var replacements = new List<(int Start, int Length, string Value)>();
        foreach (Match declaration in FunctionDeclarationPattern.Matches(source))
        {
            if (!boundHandlers.Contains(declaration.Groups["name"].Value))
            {
                continue;
            }

            var parameters = declaration.Groups["parameters"].Value;
            if (!string.IsNullOrEmpty(declaration.Groups["storage"].Value) ||
                declaration.Groups["returnType"].Value != "void" ||
                !HasCompatibleSignature(parameters, oldFormTypeName))
            {
                throw new InvalidDataException(
                    $"'{declaration.Groups["name"].Value}' does not use a supported event-handler signature.");
            }

            var updatedParameters = oldTypePattern.Replace(
                parameters,
                match => match.Groups[1].Value + newFormTypeName,
                1);
            replacements.Add((
                declaration.Groups["parameters"].Index,
                declaration.Groups["parameters"].Length,
                updatedParameters));
        }

        var updatedSource = new StringBuilder(source);
        foreach (var replacement in replacements.OrderByDescending(item => item.Start))
        {
            updatedSource.Remove(replacement.Start, replacement.Length);
            updatedSource.Insert(replacement.Start, replacement.Value);
        }

        return updatedSource.ToString();
    }

    private static bool HasCompatibleSignature(string parameters, string formTypeName) =>
        Regex.IsMatch(
            parameters,
            $@"^\s*(?:{Regex.Escape(formTypeName)}|DialogPtr)(?:\s+[A-Za-z_]\w*)?\s*,\s*short(?:\s+[A-Za-z_]\w*)?\s*$",
            RegexOptions.CultureInvariant);

    private static void ValidateIdentifier(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!ClassicMacCIdentifier.IsValid(value))
        {
            throw new ArgumentException(
                "The value must be a non-keyword C identifier of at most 31 characters.",
                parameterName);
        }
    }
}
