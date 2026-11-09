using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace ClassicMacIde.App;

public partial class HelpWindow : Window
{
    private static readonly HelpDocument[] Documents =
    [
        new("Getting started", "ClassicMacIde.App.Help.README.md"),
        new("IDE and components", "ClassicMacIde.App.Help.DeveloperManual.md"),
        new("C and Retro68", "ClassicMacIde.App.Help.CLanguage.md"),
    ];

    public HelpWindow()
    {
        InitializeComponent();
        HelpTopics.ItemsSource = Documents;
        HelpTopics.SelectedIndex = 0;
    }

    private void HelpTopics_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HelpTopics.SelectedItem is not HelpDocument document)
        {
            return;
        }

        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(document.ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded help document '{document.ResourceName}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        HelpContent.NavigateToString(RenderMarkdown(reader.ReadToEnd(), document.Title));
    }

    private void HelpContent_Navigating(object sender, NavigatingCancelEventArgs e)
    {
        if (e.Uri is not null && e.Uri.Scheme is not "about")
        {
            e.Cancel = true;
        }
    }

    private static string RenderMarkdown(string markdown, string title)
    {
        var html = new StringBuilder();
        html.Append("""
            <!doctype html>
            <html><head><meta charset="utf-8"><style>
            body{font-family:Segoe UI,Arial,sans-serif;color:#20242a;line-height:1.55;
                 margin:24px 32px 48px;max-width:920px}
            h1,h2,h3,h4{color:#263d5b;line-height:1.25;margin-top:1.5em}
            h1{font-size:2em;border-bottom:1px solid #d6dce5;padding-bottom:.25em}
            h2{font-size:1.5em;border-bottom:1px solid #e4e8ee;padding-bottom:.2em}
            p{margin:.65em 0}li{margin:.25em 0}
            code{font-family:Consolas,monospace;background:#f1f3f6;padding:1px 4px}
            pre{font-family:Consolas,monospace;background:#f4f6f8;border:1px solid #dce1e8;
                padding:12px;overflow-x:auto;white-space:pre-wrap}
            pre code{background:transparent;padding:0}
            table{border-collapse:collapse;width:100%;margin:1em 0}
            th,td{border:1px solid #cbd2dc;padding:6px 9px;text-align:left;vertical-align:top}
            th{background:#edf1f6}blockquote{border-left:4px solid #93a6c0;
                margin-left:0;padding:4px 14px;color:#4e5968}
            a{color:#155a9c}
            </style></head><body>
            """);

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inCode = false;
        var listTag = string.Empty;
        var inTable = false;
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                CloseList(html, ref listTag);
                if (inTable)
                {
                    html.Append("</tbody></table>");
                    inTable = false;
                }

                html.Append(inCode ? "</code></pre>" : "<pre><code>");
                inCode = !inCode;
                continue;
            }

            if (inCode)
            {
                html.Append(WebUtility.HtmlEncode(line)).Append('\n');
                continue;
            }

            if (line.TrimStart().StartsWith('|'))
            {
                CloseList(html, ref listTag);
                var cells = line.Trim().Trim('|').Split('|')
                    .Select(cell => cell.Trim())
                    .ToArray();
                if (cells.Length > 0 && cells.All(IsTableSeparator))
                {
                    continue;
                }

                if (!inTable)
                {
                    html.Append("<table><thead><tr>");
                    foreach (var cell in cells)
                    {
                        html.Append("<th>").Append(RenderInline(cell)).Append("</th>");
                    }

                    html.Append("</tr></thead><tbody>");
                    inTable = true;
                }
                else
                {
                    html.Append("<tr>");
                    foreach (var cell in cells)
                    {
                        html.Append("<td>").Append(RenderInline(cell)).Append("</td>");
                    }

                    html.Append("</tr>");
                }

                continue;
            }
            if (inTable)
            {
                html.Append("</tbody></table>");
                inTable = false;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                CloseList(html, ref listTag);
                continue;
            }
            if (trimmed == "---" || trimmed == "***")
            {
                CloseList(html, ref listTag);
                html.Append("<hr>");
                continue;
            }

            var heading = Regex.Match(trimmed, "^(#{1,6})\\s+(.+)$");
            if (heading.Success)
            {
                CloseList(html, ref listTag);
                var level = Math.Min(heading.Groups[1].Length, 4);
                html.Append("<h").Append(level).Append('>')
                    .Append(RenderInline(heading.Groups[2].Value))
                    .Append("</h").Append(level).Append('>');
                continue;
            }

            var listItem = Regex.Match(trimmed, "^(?:[-*+]\\s+|\\d+\\.\\s+)(.*)$");
            if (listItem.Success)
            {
                var desiredTag = Regex.IsMatch(trimmed, "^\\d+\\.\\s+") ? "ol" : "ul";
                if (listTag != desiredTag)
                {
                    CloseList(html, ref listTag);
                    listTag = desiredTag;
                    html.Append('<').Append(listTag).Append('>');
                }

                html.Append("<li>").Append(RenderInline(listItem.Groups[1].Value)).Append("</li>");
                continue;
            }

            CloseList(html, ref listTag);
            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                html.Append("<blockquote>").Append(RenderInline(trimmed[2..]))
                    .Append("</blockquote>");
            }
            else
            {
                html.Append("<p>").Append(RenderInline(trimmed)).Append("</p>");
            }
        }

        if (inCode)
        {
            html.Append("</code></pre>");
        }
        if (inTable)
        {
            html.Append("</tbody></table>");
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    private static string RenderInline(string value)
    {
        var text = WebUtility.HtmlEncode(value);
        text = Regex.Replace(text, "`([^`]+)`", "<code>$1</code>");
        text = Regex.Replace(text, "\\*\\*([^*]+)\\*\\*", "<strong>$1</strong>");
        text = Regex.Replace(text, "\\*([^*]+)\\*", "<em>$1</em>");
        return Regex.Replace(
            text,
            "\\[([^\\]]+)\\]\\((https?://[^)\\s]+)\\)",
            "<a href=\"$2\">$1</a>");
    }

    private static bool IsTableSeparator(string cell) =>
        Regex.IsMatch(cell, "^:?-{3,}:?$");

    private static void CloseList(StringBuilder html, ref string listTag)
    {
        if (listTag.Length == 0)
        {
            return;
        }

        html.Append("</").Append(listTag).Append('>');
        listTag = string.Empty;
    }

    private sealed record HelpDocument(string Title, string ResourceName);
}
