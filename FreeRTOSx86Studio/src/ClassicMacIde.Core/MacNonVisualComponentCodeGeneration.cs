using System.Text;

namespace ClassicMacIde.Core;

public static partial class MacFormCodeGenerator
{
    private sealed record GeneratedMenu(
        int ResourceId,
        string Title,
        IReadOnlyList<(DesignerNonVisualComponent Component, int ComponentIndex)> Items);

    private static IReadOnlyList<GeneratedMenu> GetGeneratedMenus(
        IReadOnlyList<DesignerNonVisualComponent> components) =>
        components
            .Select((component, index) => (component, ComponentIndex: index + 1))
            .Where(item => item.component.ComponentTypeId == "menu-item")
            .GroupBy(
                item => item.component.Properties.GetValueOrDefault("menuTitle") ?? string.Empty,
                StringComparer.Ordinal)
            .Select((group, menuIndex) => new GeneratedMenu(
                600 + menuIndex,
                group.Key,
                group.Select(item => (item.component, item.ComponentIndex)).ToArray()))
            .ToArray();

    private static void AppendMenuItemResources(
        StringBuilder source,
        IReadOnlyList<DesignerNonVisualComponent> components)
    {
        foreach (var menu in GetGeneratedMenus(components))
        {
            source.AppendLine($"resource 'MENU' ({menu.ResourceId}) {{");
            source.AppendLine($"    {menu.ResourceId},");
            source.AppendLine("    textMenuProc,");
            source.AppendLine("    0x7FFFFFFF,");
            source.AppendLine("    enabled,");
            source.AppendLine($"    \"{EscapeRezString(menu.Title)}\",");
            source.AppendLine("    {");
            foreach (var (component, _) in menu.Items)
            {
                var shortcut = component.Properties.GetValueOrDefault("shortcut") ?? string.Empty;
                var key = shortcut.Length == 0
                    ? "noKey"
                    : $"'{EscapeRezString(shortcut)}'";
                source.AppendLine(
                    $"        \"{EscapeRezString(component.Properties.GetValueOrDefault("caption") ?? string.Empty)}\", noIcon, {key}, noMark, plain;");
            }
            source.AppendLine("    }");
            source.AppendLine("};");
            source.AppendLine();
        }
    }

    private static void AppendMenuInstallation(
        StringBuilder source,
        IReadOnlyList<DesignerNonVisualComponent> components)
    {
        foreach (var menu in GetGeneratedMenus(components))
        {
            source.AppendLine($"    MenuHandle designerMenu{menu.ResourceId} = GetMenu({menu.ResourceId});");
            source.AppendLine($"    if (designerMenu{menu.ResourceId} != NULL)");
            source.AppendLine("    {");
            source.AppendLine($"        InsertMenu(designerMenu{menu.ResourceId}, 0);");
            foreach (var (component, itemIndex) in menu.Items.Select(
                         (item, index) => (item.Component, ItemIndex: index + 1)))
            {
                if (!bool.TryParse(
                        component.Properties.GetValueOrDefault("enabled"),
                        out var enabled) ||
                    !enabled)
                {
                    source.AppendLine(
                        $"        DisableItem(designerMenu{menu.ResourceId}, {itemIndex});");
                }
            }
            source.AppendLine("    }");
        }

        if (GetGeneratedMenus(components).Count > 0)
        {
            source.AppendLine("    DrawMenuBar();");
        }
    }

    private static void AppendMenuEventLoopHandling(
        StringBuilder source,
        IReadOnlyList<DesignerNonVisualComponent> components,
        DesignerNonVisualComponentRegistry componentRegistry)
    {
        var menus = GetGeneratedMenus(components);
        if (menus.Count == 0)
        {
            return;
        }

        source.AppendLine("        if (event.what == mouseDown && event.where.v < 20)");
        source.AppendLine("        {");
        source.AppendLine("            long menuChoice = MenuSelect(event.where);");
        source.AppendLine("            short menuId = (short)(menuChoice >> 16);");
        source.AppendLine("            short menuItem = (short)(menuChoice & 0xFFFF);");
        source.AppendLine("            if (menuId != 0)");
        source.AppendLine("            {");
        source.AppendLine("                switch (menuId)");
        source.AppendLine("                {");
        foreach (var menu in menus)
        {
            source.AppendLine($"                    case {menu.ResourceId}:");
            source.AppendLine("                        switch (menuItem)");
            source.AppendLine("                        {");
            foreach (var (component, componentIndex, itemIndex) in menu.Items.Select(
                         (item, index) => (item.Component, item.ComponentIndex, ItemIndex: index + 1)))
            {
                if (!bool.TryParse(
                        component.Properties.GetValueOrDefault("enabled"),
                        out var enabled) ||
                    !enabled)
                {
                    continue;
                }

                var definition = componentRegistry.GetRequired(component.ComponentTypeId);
                var clickEventIndex = definition.Events
                    .ToList()
                    .FindIndex(eventDefinition => eventDefinition.Id == "OnClick");
                if (clickEventIndex < 0 ||
                    !component.Events.ContainsKey("OnClick"))
                {
                    continue;
                }

                source.AppendLine($"                            case {itemIndex}:");
                source.AppendLine(
                    $"                                DispatchNonVisualComponentEvent(dialog, {componentIndex}, {clickEventIndex + 1});");
                source.AppendLine("                                break;");
            }
            source.AppendLine("                            default:");
            source.AppendLine("                                break;");
            source.AppendLine("                        }");
            source.AppendLine("                        break;");
        }
        source.AppendLine("                    default:");
        source.AppendLine("                        break;");
        source.AppendLine("                }");
        source.AppendLine("                HiliteMenu(0);");
        source.AppendLine("                continue;");
        source.AppendLine("            }");
        source.AppendLine("        }");
    }

    private static void AppendNonVisualComponentEventDispatcher(
        StringBuilder source,
        string formTypeName,
        IReadOnlyList<DesignerNonVisualComponent> components,
        DesignerNonVisualComponentRegistry componentRegistry)
    {
        source.AppendLine(
            "static void DispatchNonVisualComponentEvent(DialogPtr dialog, short componentIndex, short eventIndex)");
        source.AppendLine("{");
        source.AppendLine("    switch (componentIndex)");
        source.AppendLine("    {");
        for (var componentIndex = 0; componentIndex < components.Count; componentIndex++)
        {
            var component = components[componentIndex];
            var definition = componentRegistry.GetRequired(component.ComponentTypeId);
            var boundEvents = definition.Events
                .Select((eventDefinition, eventIndex) =>
                    (eventDefinition, EventIndex: eventIndex + 1))
                .Where(item => component.Events.ContainsKey(item.eventDefinition.Id))
                .ToArray();
            if (boundEvents.Length == 0)
            {
                continue;
            }

            source.AppendLine($"        case {componentIndex + 1}:");
            source.AppendLine("            switch (eventIndex)");
            source.AppendLine("            {");
            foreach (var (eventDefinition, eventIndex) in boundEvents)
            {
                source.AppendLine($"                case {eventIndex}:");
                source.AppendLine(
                    $"                    {component.Events[eventDefinition.Id]}(({formTypeName})dialog, eventIndex);");
                source.AppendLine("                    break;");
            }
            source.AppendLine("                default:");
            source.AppendLine("                    break;");
            source.AppendLine("            }");
            source.AppendLine("            break;");
        }

        source.AppendLine("        default:");
        source.AppendLine("            break;");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();
    }

    private static MacNonVisualComponentCodeGenerationContext
        CreateComponentCodeGenerationContext(
            MacTargetProfile profile,
            IReadOnlyList<DesignerNonVisualComponent> components,
            int componentIndex,
            string formTypeName)
    {
        if (componentIndex < 1 || componentIndex > components.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(componentIndex));
        }

        return new MacNonVisualComponentCodeGenerationContext(
            profile,
            components[componentIndex - 1],
            componentIndex,
            formTypeName,
            components);
    }
}
