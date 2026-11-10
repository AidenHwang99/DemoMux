using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using DemoMux;

internal static class UiRegression
{
    internal static void Run(Action<bool, string> check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow(false);
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                var states = (Array)typeof(MainWindow).GetField("setupStates", flags)!.GetValue(window)!;
                var enumType = states.GetType().GetElementType()!;
                void Set(int index, string name) => states.SetValue(Enum.Parse(enumType, name), index);
                string State(int index) => states.GetValue(index)!.ToString()!;
                for (int i = 0; i < 4; i++) Set(i, "Complete");
                Set(4, "Running");
                typeof(MainWindow).GetField("setupStep", flags)!.SetValue(window, 5);
                // The real Run cancellation handler executes synchronously for this canceled task.
                window.Run("隔离取消回归", _ => Task.FromCanceled(new CancellationToken(true)), true);
                check(State(3) == "Stopped" && State(4) == "Stopped", "canceling public verification invalidates Funnel and verification completion");
                check(Enumerable.Range(0, 3).All(i => State(i) == "Complete"), "cancel preserves valid component and account prerequisites");
                typeof(MainWindow).GetField("setupIdentity", flags)!.SetValue(window, "original-fixture-identity");
                window.Run("隔离身份变化回归", _ => Task.FromException(new AccountIdentityException()), true);
                check(State(2) == "Pending" && State(3) == "Pending" && State(4) == "Failed", "identity failure invalidates account and Funnel while marking current step failed");
                check(typeof(MainWindow).GetField("setupIdentity", flags)!.GetValue(window) is null, "identity failure clears remembered setup identity");
                check(State(0) == "Complete" && State(1) == "Complete", "identity failure retains installed component facts");
                window.Close();
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
    }
}
