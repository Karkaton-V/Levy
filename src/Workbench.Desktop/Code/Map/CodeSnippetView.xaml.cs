using System.Windows;
using System.Windows.Controls;

namespace Workbench.Desktop;

// Shows the Code property in a read-only AvalonEdit, using the Editor's colours.
public partial class CodeSnippetView : UserControl
{
    public static readonly DependencyProperty CodeProperty =
        DependencyProperty.Register(nameof(Code), typeof(string), typeof(CodeSnippetView),
            new PropertyMetadata("", (d, e) => ((CodeSnippetView)d).Editor.Text = e.NewValue as string ?? ""));

    public CodeSnippetView()
    {
        InitializeComponent();
        Editor.SyntaxHighlighting = Shared.RulesSyntax;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.TextArea.Caret.CaretBrush = System.Windows.Media.Brushes.Transparent;
    }

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }
}
