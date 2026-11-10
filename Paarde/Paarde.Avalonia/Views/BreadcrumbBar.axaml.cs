using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Paarde.Avalonia.Helpers;
using Paarde.Core.Models;
using Paarde.Presentation.Helpers;

namespace Paarde.Avalonia.Views;

// Thin breadcrumb strip sitting above CodeEditor inside
// ExplorerFileTabControl.axaml, bound to the same DocumentSymbols the outline panel uses plus
// CodeEditor's own CaretPosition - BreadcrumbPathBuilder.Build (a pure function, independently
// tested) does the actual enclosing-chain computation.
public partial class BreadcrumbBar : UserControl
{
    public static readonly StyledProperty<LspDocumentSymbol?> DocumentSymbolsProperty =
        AvaloniaProperty.Register<BreadcrumbBar, LspDocumentSymbol?>(nameof(DocumentSymbols));

    public static readonly StyledProperty<LspPosition?> CaretPositionProperty =
        AvaloniaProperty.Register<BreadcrumbBar, LspPosition?>(nameof(CaretPosition));

    public static readonly StyledProperty<IReadOnlyList<LspDocumentSymbol>> SegmentsProperty =
        AvaloniaProperty.Register<BreadcrumbBar, IReadOnlyList<LspDocumentSymbol>>(nameof(Segments), defaultValue: []);

    // Same deviation as the outline panel - this control
    // isn't a child of CodeEditor and can't raise an event CodeEditor itself declares, so it raises
    // its own event instead, forwarded by ExplorerFileTabControl.axaml.cs through the same
    // NavigateToLocation/ApplyPendingCaretTarget path.
    public event EventHandler<LspDocumentSymbol>? SegmentClicked;

    public LspDocumentSymbol? DocumentSymbols
    {
        get => GetValue(DocumentSymbolsProperty);
        set => SetValue(DocumentSymbolsProperty, value);
    }

    public LspPosition? CaretPosition
    {
        get => GetValue(CaretPositionProperty);
        set => SetValue(CaretPositionProperty, value);
    }

    public IReadOnlyList<LspDocumentSymbol> Segments
    {
        get => GetValue(SegmentsProperty);
        private set => SetValue(SegmentsProperty, value);
    }

    static BreadcrumbBar()
    {
        DocumentSymbolsProperty.Changed.AddClassHandler<BreadcrumbBar>((bar, _) => bar.Recompute());
        CaretPositionProperty.Changed.AddClassHandler<BreadcrumbBar>((bar, _) => bar.Recompute());
    }

    public BreadcrumbBar()
    {
        InitializeComponent();
    }

    private void Recompute() => Segments = BreadcrumbPathBuilder.Build(DocumentSymbols, CaretPosition);

    private void OnSegmentButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LspDocumentSymbol symbol })
        {
            SegmentClicked?.Invoke(this, symbol);
        }
    }
}
