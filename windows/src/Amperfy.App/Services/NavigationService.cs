using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Amperfy.App.Services;

/// Navigation inside the main content frame.
public sealed class NavigationService
{
    private Frame? _frame;

    public event Action? Navigated;

    /// Navigation parameter of the current page.
    public object? CurrentParameter { get; private set; }

    public void Attach(Frame frame)
    {
        _frame = frame;
        frame.Navigated += (_, e) =>
        {
            CurrentParameter = e.Parameter;
            Navigated?.Invoke();
        };
    }

    public Frame? Frame => _frame;

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public Type? CurrentPageType => _frame?.CurrentSourcePageType;

    public bool Navigate(Type pageType, object? parameter = null, bool clearBackStack = false)
    {
        if (_frame is null) return false;
        var ok = _frame.Navigate(pageType, parameter, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
        if (ok && clearBackStack) _frame.BackStack.Clear();
        return ok;
    }

    public bool Navigate<TPage>(object? parameter = null, bool clearBackStack = false) where TPage : Page =>
        Navigate(typeof(TPage), parameter, clearBackStack);

    public void GoBack()
    {
        if (_frame?.CanGoBack == true) _frame.GoBack();
    }
}
