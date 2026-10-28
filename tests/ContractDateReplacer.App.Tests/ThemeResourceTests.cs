using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Xunit;

namespace ContractDateReplacer.App.Tests;

// Application resources are process-wide; keep this STA test isolated from view-model tests.
[CollectionDefinition("ThemeResourceTests", DisableParallelization = true)]
public sealed class ThemeResourceTestCollection { }

[Collection("ThemeResourceTests")]
public class ThemeResourceTests
{
    [Fact]
    public void LightTheme_PopupSurfacesAndInteractionStatesAreOpaque()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            ContractDateReplacer.App.App? app = null;
            try
            {
                app = new ContractDateReplacer.App.App();
                app.InitializeComponent();
                var resources = app.Resources;
                VerifyAttendanceLayouts();
                Assert.True(Assert.IsType<SolidColorBrush>(resources["ApplicationBackgroundBrush"]).Color.R > 240, "The complete Light palette must be loaded.");
                foreach (var key in new[] { "ApplicationBackgroundBrush",
                    "ContextMenuBackground", "ContextMenuForeground", "MenuBarBackground",
                    "MenuBarItemBackgroundSelected", "MenuBarItemBackgroundPressed",
                    "ComboBoxDropDownBackground", "ComboBoxItemBackgroundSelected" })
                    AssertOpaque(resources[key], key);

                foreach (Control control in new Control[] { new Menu(), new MenuItem { Header = "File" },
                    new ContextMenu(), new ComboBox(), new ComboBoxItem { Content = "Option" } })
                {
                    control.Style = (Style)resources[control.GetType()];
                    control.ApplyTemplate();
                    AssertOpaque(control.Background, control.GetType().Name);
                    AssertOpaque(control.Foreground, control.GetType().Name + " foreground");
                }
                var combo = new ComboBox { Style = (Style)resources[typeof(ComboBox)] };
                combo.ApplyTemplate();
                var popup = Assert.Single(Descendants(combo).OfType<Popup>());
                var borders = Descendants(popup.Child).OfType<Border>().ToArray();
                Assert.Contains(borders, b => b.Background is SolidColorBrush brush && brush.Color.A == 255 && brush.Opacity == 1);
                var menu = new Menu { Style = (Style)resources[typeof(Menu)] };
                var header = new MenuItem { Header = "File", Style = (Style)resources[typeof(MenuItem)] };
                header.Items.Add(new MenuItem { Header = "Open" });
                menu.Items.Add(header);
                menu.ApplyTemplate();
                header.ApplyTemplate();
                Assert.Single(Descendants(header).OfType<Popup>());
                AssertOpaque(Assert.IsType<Border>(header.Template.FindName("SubmenuBorder", header)).Background, "submenu backing");
                SetReadOnlyState(header, typeof(MenuItem), "IsHighlightedPropertyKey", true);
                AssertOpaque(Assert.IsType<Border>(header.Template.FindName("Border", header)).Background, "menu highlight");

                var item = new ComboBoxItem { Content = "Option", Style = (Style)resources[typeof(ComboBoxItem)] };
                item.ApplyTemplate();
                item.IsSelected = true;
                var itemBorder = Assert.IsType<Border>(item.Template.FindName("ContentBorder", item));
                AssertOpaque(itemBorder.Background, "selected item backing");
                item.IsSelected = false;
                SetReadOnlyState(item, typeof(ComboBoxItem), "IsHighlightedPropertyKey", true);
                AssertOpaque(itemBorder.Background, "highlighted item backing");

            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                app?.Shutdown();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Theme resource test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifyAttendanceLayouts()
    {
        var vm = new ContractDateReplacer.App.ViewModels.AttendanceCalendarViewModel(
            Moq.Mock.Of<ContractDateReplacer.Core.Interfaces.IAttendanceCalendarService>(),
            Moq.Mock.Of<ContractDateReplacer.Core.Interfaces.IBackupRollbackService>(),
            Moq.Mock.Of<ContractDateReplacer.App.Services.IAttendanceFileDialogService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ContractDateReplacer.App.ViewModels.AttendanceCalendarViewModel>.Instance);
        var table = new ContractDateReplacer.Core.Models.AttendanceTable("one", "Bảng chấm công", "rId1", 13, 4, 15, 18, 35, "A11", 2026, 10);
        vm.Tables.Add(table); vm.SelectedTable = table;
        vm.PeriodText = "Thời gian: tháng 12/2026(từ 01/12/2026 đến 31/12/2026)";
        vm.StatusMessage = "Công được giữ theo số ngày. Bản gốc được giữ nguyên; Chủ nhật được tô xanh.";
        for (int d = 1; d <= 31; d++) vm.PreviewDays.Add(new(d, new DateTime(2026, 12, d).DayOfWeek == DayOfWeek.Sunday ? "CN" : "T" + ((int)new DateTime(2026, 12, d).DayOfWeek + 1), new DateTime(2026, 12, d).DayOfWeek == DayOfWeek.Sunday));
        foreach (double scale in new[] { 1.0, 1.5 })
        {
            var view = new ContractDateReplacer.App.Views.AttendanceCalendarView { DataContext = vm };
            var host = new Border { Child = view, LayoutTransform = new ScaleTransform(scale, scale), Background = Brushes.White };
            var size = new Size(1200, 780); host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            foreach (string content in new[] { "← Quay lại", "Xuất bản sao Excel" })
            {
                var button = Assert.Single(Descendants(view).OfType<ButtonBase>().Where(b => Equals(b.Content, content)));
                var bounds = button.TransformToAncestor(host).TransformBounds(new Rect(button.RenderSize));
                Assert.True(bounds.Width > 0 && bounds.Height > 0, content + " must be visible.");
                Assert.True(bounds.Left >= 0 && bounds.Right <= host.ActualWidth + 1, content + " must fit horizontally at " + scale);
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= host.ActualHeight + 1, content + " must fit vertically at " + scale);
            }
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1200, 780, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            string folder = System.IO.Path.Combine(AppContext.BaseDirectory, "ui-layout"); System.IO.Directory.CreateDirectory(folder);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(System.IO.Path.Combine(folder, "attendance-" + (int)(scale * 100) + ".png")); encoder.Save(stream);
        }
    }
    // Exercise framework-owned highlight state without opening a native popup window.
    private static void SetReadOnlyState(DependencyObject target, Type owner, string keyName, bool value)
    {
        var field = owner.GetField(keyName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var key = Assert.IsType<DependencyPropertyKey>(field!.GetValue(null));
        target.SetValue(key, value);
    }

    private static void AssertOpaque(object value, string name)
    {
        var brush = Assert.IsType<SolidColorBrush>(value);
        Assert.True(brush.Color.A == 255 && brush.Opacity == 1, name + " must be opaque.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
