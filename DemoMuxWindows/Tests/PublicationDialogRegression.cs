using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DemoMux;

internal static class PublicationDialogRegression
{
    internal static void Run(Action<bool, string> check)
    {
        using var server = new PublicationPreflightRegression.Server(path => new(path == "/" ? 200 : 404));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            _ = dispatcher.InvokeAsync(async () =>
            {
                MainWindow? window = null;
                try
                {
                    window = new MainWindow(false) { Title = "DemoMux · 隔离发布回归" };
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var catalog = (Catalog)typeof(MainWindow).GetField("catalog", flags)!.GetValue(window)!;
                    var gateway = (Gateway)typeof(MainWindow).GetField("gateway", flags)!.GetValue(window)!;
                    var project = new Project { Kind = "port", Name = "隔离本机项目", Slug = "preflight-fixture", Port = server.Port };
                    catalog.Projects = [project];
                    window.Show();
                    var open = dispatcher.InvokeAsync(() => typeof(MainWindow).GetMethod("PublishDialog", flags)!.Invoke(window, [project, null]));
                    await Until(() => window.OwnedWindows.Cast<Window>().Any(w => w.Title == "DemoMux · 发布项目"));
                    var dialog = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "DemoMux · 发布项目");
                    T Control<T>(string id) where T : FrameworkElement => Descendants(dialog).OfType<T>().Single(e => AutomationProperties.GetAutomationId(e) == id);
                    var confirm = Control<Button>("publish-confirm");
                    await Until(() => confirm.IsEnabled);
                    check(Control<Button>("publish-advanced").IsVisible && !Control<TextBox>("publish-slug").IsVisible, "port publishing initially shows recommended entry and hides advanced configuration");
                    check(!Descendants(dialog).OfType<CheckBox>().Any(), "port dialog no longer relies on self-confirmed base-path compatibility");
                    check(gateway.Slots.Shares.Count == 0, "opening recommended local preflight consumes no public slot");
                    var linkField = Descendants(dialog).OfType<TextBox>().Single(text => text.IsReadOnly);
                    linkField.Focus();
                    check(linkField.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)) && Control<Button>("publish-advanced").IsKeyboardFocused,
                        "keyboard traversal from the link reaches the advanced entry");
                    Control<Button>("publish-advanced").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => Control<TextBlock>("publish-preflight").Text.Contains("不支持"));
                    check(!confirm.IsEnabled && Control<Button>("publish-recommended").IsVisible, "path 404 stays in the dialog with publication disabled and an explicit independent-entry action");
                    check(gateway.Slots.Shares.Count == 0, "failed path compatibility check creates no public share");
                    check(!Descendants(dialog).OfType<ScrollViewer>().Any(scroll => confirm.IsDescendantOf(scroll)), "publish and cancel actions live outside the scrolling body");
                    dialog.Width = 480; dialog.Height = 400; dialog.UpdateLayout();
                    check(confirm.IsVisible && confirm.TranslatePoint(new Point(), dialog).Y < dialog.ActualHeight, "publish footer remains inside a narrow short dialog");
                    window.CopyPublicationPath = _ => throw new ExternalException("fixture clipboard busy");
                    Control<Button>("publish-copy-path").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(Control<TextBlock>("publish-copy-feedback").Text.Contains("剪贴板正在使用"), "clipboard failure has explicit retry feedback without changing compatibility status");
                    Control<TextBox>("publish-slug").Text = "edited-path";
                    check(!confirm.IsEnabled, "editing the slug immediately invalidates publish eligibility");
                    Control<Button>("publish-back").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Until(() => confirm.IsEnabled);
                    check(!Control<TextBox>("publish-slug").IsVisible && gateway.Slots.Shares.Count == 0, "switching back checks the root while keeping the edited draft and no slot reservation");
                    Control<Button>("publish-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await open.Task;
                    check(project.Slug == "preflight-fixture" && gateway.Slots.Shares.Count == 0, "canceling the dialog preserves project metadata and publication slots");

                    var reopen = dispatcher.InvokeAsync(() => typeof(MainWindow).GetMethod("PublishDialog", flags)!.Invoke(window, [project, false]));
                    await Until(() => window.OwnedWindows.Cast<Window>().Any(w => w.Title == "DemoMux · 发布项目"));
                    dialog = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "DemoMux · 发布项目");
                    check(Control<TextBox>("publish-slug").Text == "edited-path", "reopening after cancellation preserves the draft without changing stored project fields");
                    Control<Button>("publish-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await reopen.Task;
                    await Task.Delay(450);
                    check(gateway.Slots.Shares.Count == 0 && !window.OwnedWindows.Cast<Window>().Any(), "closing during debounced preflight leaves no dialog or pending public share");

                    var html = new Project { Kind = "html", Name = "隔离 HTML", Slug = "html-fixture" }; catalog.Projects.Add(html);
                    var htmlOpen = dispatcher.InvokeAsync(() => typeof(MainWindow).GetMethod("PublishDialog", flags)!.Invoke(window, [html, null]));
                    await Until(() => window.OwnedWindows.Cast<Window>().Any(w => w.Title == "DemoMux · 发布项目"));
                    dialog = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "DemoMux · 发布项目");
                    check(Control<ComboBox>("publish-method").SelectedIndex == 0 && Control<Button>("publish-confirm").IsEnabled,
                        "static HTML retains path-mode default without local-port preflight");
                    Control<Button>("publish-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await htmlOpen.Task;

                    var failureType = typeof(MainWindow).GetNestedType("PublishFailure", BindingFlags.NonPublic)!;
                    var failureContext = Activator.CreateInstance(failureType, project.Id, false, 404);
                    typeof(MainWindow).GetField("publishFailure", flags)!.SetValue(window, failureContext);
                    var noticeField = typeof(MainWindow).GetField("notice", flags)!;
                    noticeField.SetValue(window, Enum.Parse(noticeField.FieldType, "Error"));
                    typeof(MainWindow).GetMethod("Render", flags | BindingFlags.DeclaredOnly)!.Invoke(window, null);
                    var recovery = Descendants(window).OfType<Button>().Single(button => button.Content as string == "改用独立入口…");
                    var recoveryOpen = dispatcher.InvokeAsync(() => recovery.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                    await Until(() => window.OwnedWindows.Cast<Window>().Any(w => w.Title == "DemoMux · 发布项目"));
                    dialog = window.OwnedWindows.Cast<Window>().Single(w => w.Title == "DemoMux · 发布项目");
                    await Until(() => Control<Button>("publish-confirm").IsEnabled);
                    check(Control<Button>("publish-advanced").IsVisible && gateway.Slots.Shares.Count == 0,
                        "public path-404 recovery opens recommended mode without silently publishing a new URL");
                    check(Control<TextBox>("publish-slug").Text == "edited-path", "public failure recovery retains the existing publish draft");
                    Control<Button>("publish-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await recoveryOpen.Task;
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    if (window is not null) { foreach (var owned in window.OwnedWindows.Cast<Window>().ToArray()) owned.Close(); window.Close(); }
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new Exception("Publication dialog regression timed out");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Dialog condition did not become ready"); await Task.Delay(20); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }
}
