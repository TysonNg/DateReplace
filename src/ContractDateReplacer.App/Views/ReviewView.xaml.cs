using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ContractDateReplacer.App.ViewModels;

namespace ContractDateReplacer.App.Views;

/// <summary>
/// Màn hình Review & Selection cho phép người dùng rà soát và lựa chọn các ngày cần thay thế.
/// </summary>
public partial class ReviewView : UserControl
{
    private ReviewViewModel? _viewModel;

    public ReviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null && DataContext is ReviewViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            RenderPreview();
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        _viewModel = e.NewValue as ReviewViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RenderPreview();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModel();
    }

    private void DetachViewModel()
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReviewViewModel.PreviewRevision))
        {
            RenderPreview();
        }
    }

    private void DateCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DateItemViewModel item } &&
            DataContext is ReviewViewModel viewModel)
        {
            viewModel.SelectDateForPreview(item);
        }
    }

    private void ApplySuggestedBusinessDay_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ReviewViewModel { SelectedDateItem: { } item })
        {
            item.ApplySuggestedBusinessDay();
        }
    }
    private void RenderPreview()
    {
        if (PreviewRichTextBox == null) return;

        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13
        };
        Run? highlightedRun = null;

        if (_viewModel != null)
        {
            foreach (var segment in _viewModel.PreviewSegments)
            {
                var heading = new Paragraph { Margin = new Thickness(0, 10, 0, 4) };
                heading.Inlines.Add(new Run(segment.StoryLabel)
                {
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(29, 78, 216))
                });
                if (!string.IsNullOrWhiteSpace(segment.SourceLocation))
                {
                    heading.Inlines.Add(new Run($"  •  {segment.SourceLocation}")
                    {
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                    });
                }
                document.Blocks.Add(heading);

                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 0, 0, 12),
                    LineHeight = 22,
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59))
                };
                int start = Math.Clamp(segment.HighlightStart, 0, segment.Text.Length);
                int length = segment.HighlightStart >= 0
                    ? Math.Clamp(segment.HighlightLength, 0, segment.Text.Length - start)
                    : 0;

                if (length > 0)
                {
                    paragraph.Inlines.Add(new Run(segment.Text[..start]));
                    highlightedRun = new Run(segment.Text.Substring(start, length))
                    {
                        Background = new SolidColorBrush(Color.FromRgb(254, 240, 138)),
                        Foreground = new SolidColorBrush(Color.FromRgb(113, 63, 18)),
                        FontWeight = FontWeights.Bold
                    };
                    paragraph.Inlines.Add(highlightedRun);
                    paragraph.Inlines.Add(new Run(segment.Text[(start + length)..]));
                }
                else
                {
                    paragraph.Inlines.Add(new Run(segment.Text));
                }

                document.Blocks.Add(paragraph);
            }
        }

        PreviewRichTextBox.Document = document;
        if (highlightedRun != null)
        {
            Dispatcher.BeginInvoke(
                () => highlightedRun.BringIntoView(),
                DispatcherPriority.Loaded);
        }
        else
        {
            PreviewRichTextBox.ScrollToHome();
        }
    }
}

/// <summary>Highlights the target value in the read-only Excel context grid.</summary>
public sealed class ExcelTargetCellConverter : IValueConverter
{
    public object Convert(object value, System.Type targetType, object parameter, CultureInfo culture)
    {
        string? text = value switch
        {
            string s => s,
            TextBlock tb => tb.Text,
            ContentPresenter cp => cp.Content?.ToString(),
            _ => value?.ToString()
        };
        return text != null && text.StartsWith("\u25b6 ", System.StringComparison.Ordinal);
    }

    public object ConvertBack(object value, System.Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}