using Microsoft.AspNetCore.Components;

namespace UI.Components.Shared
{
    public class EntityColumn<TItem>
    {
        public required string Name { get; init; }
        public required string Header { get; init; }
        public int Width { get; init; } = 1;
        public string CssClass { get; init; } = string.Empty;

        public required Func<TItem, object?> ValueSelector { get; init; }
        public RenderFragment<TItem>? CellTemplate { get; init; }
    }
}
