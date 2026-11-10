using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Paarde.Core.Models;
using Paarde.Presentation.Helpers;

namespace Paarde.WinUI.Views;

// specs/editor-intelligence R5: thin breadcrumb strip above the code editor, bound to the tab's
// DocumentSymbols plus the editor's CaretPosition - port of Paarde.Avalonia's BreadcrumbBar.
public sealed partial class BreadcrumbBar : UserControl
{
    public static readonly DependencyProperty DocumentSymbolsProperty =
        DependencyProperty.Register(nameof(DocumentSymbols), typeof(LspDocumentSymbol), typeof(BreadcrumbBar), new PropertyMetadata(null, OnInputChanged));

    public static readonly DependencyProperty CaretPositionProperty =
        DependencyProperty.Register(nameof(CaretPosition), typeof(LspPosition), typeof(BreadcrumbBar), new PropertyMetadata(null, OnInputChanged));

    // specs/editor-intelligence R4/R5: the host forwards this through the same
    // NavigateToLocation path the outline panel uses.
    public event EventHandler<LspDocumentSymbol>? SegmentClicked;

    public LspDocumentSymbol? DocumentSymbols
    {
        get => (LspDocumentSymbol?)GetValue(DocumentSymbolsProperty);
        set => SetValue(DocumentSymbolsProperty, value);
    }

    public LspPosition? CaretPosition
    {
        get => (LspPosition?)GetValue(CaretPositionProperty);
        set => SetValue(CaretPositionProperty, value);
    }

    public BreadcrumbBar()
    {
        InitializeComponent();
    }

    private static void OnInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (BreadcrumbBar)d;
        bar.SegmentsItemsControl.ItemsSource = BreadcrumbPathBuilder.Build(bar.DocumentSymbols, bar.CaretPosition);
    }

    private void OnSegmentButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: LspDocumentSymbol symbol })
        {
            SegmentClicked?.Invoke(this, symbol);
        }
    }
}
