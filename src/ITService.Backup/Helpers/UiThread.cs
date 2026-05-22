using System.Windows;
using System.Windows.Threading;

namespace ITService.Backup.Helpers;

public static class UiThread
{
    public static void Run(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    public static T Run<T>(Func<T> func)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            return func();

        return dispatcher.Invoke(func);
    }
}
