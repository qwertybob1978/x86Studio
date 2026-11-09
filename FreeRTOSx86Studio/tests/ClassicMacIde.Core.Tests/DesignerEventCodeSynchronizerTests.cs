using ClassicMacIde.Core;

namespace ClassicMacIde.Core.Tests;

public class DesignerEventCodeSynchronizerTests
{
    [Fact]
    public void CreatesAnEventHandlerAndDoesNotDuplicateItsImplementation()
    {
        const string source = "#include \"designer_form.h\"\n";

        var generated = DesignerEventCodeSynchronizer.EnsureHandlerImplementation(
            source,
            "RunButtonClick",
            "MainForm");
        var repeated = DesignerEventCodeSynchronizer.EnsureHandlerImplementation(
            generated,
            "RunButtonClick",
            "MainForm");

        Assert.Equal(generated, repeated);
        Assert.Contains(
            "void RunButtonClick(MainForm form, short item)",
            generated);
        Assert.Contains("(void)form;", generated);
        Assert.Contains("(void)item;", generated);
    }

    [Fact]
    public void PreservesExistingHandlerBodiesAndAcceptsMatchingPrototypes()
    {
        const string source = """
            #include "designer_form.h"

            void RunButtonClick(MainForm form, short item);

            void RunButtonClick(DialogPtr form, short item)
            {
                MacStudioSetRunButtonValue(form, 1);
            }
            """;

        var result = DesignerEventCodeSynchronizer.EnsureHandlerImplementation(
            source,
            "RunButtonClick",
            "MainForm");

        Assert.Equal(source, result);
    }

    [Fact]
    public void RejectsExistingHandlersWithAnIncompatibleSignature()
    {
        const string source = "void RunButtonClick(short item) { }\n";

        Assert.Throws<InvalidDataException>(() =>
            DesignerEventCodeSynchronizer.EnsureHandlerImplementation(
                source,
                "RunButtonClick",
                "MainForm"));
    }

    [Fact]
    public void UpdatesBoundHandlerPrototypeAndImplementationWhenFormTypeChanges()
    {
        const string source = """
            void RunButtonClick(MainForm form, short item);

            void RunButtonClick(DialogPtr form, short item)
            {
                (void)form;
                (void)item;
            }
            """;

        var updated = DesignerEventCodeSynchronizer.UpdateHandlerFormTypes(
            source,
            ["RunButtonClick"],
            "MainForm",
            "EditorForm");

        Assert.Equal(2, CountOccurrences(
            updated,
            "EditorForm form, short item"));
        Assert.DoesNotContain("MainForm form", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogPtr form", updated, StringComparison.Ordinal);
        Assert.Contains("(void)form;", updated);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }
}
