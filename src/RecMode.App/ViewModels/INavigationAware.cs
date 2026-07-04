namespace RecMode.App.ViewModels;

/// <summary>Optional hooks a page view model can implement to react to navigation (drives §3.9 teardown).</summary>
public interface INavigationAware
{
    void OnNavigatedTo();
    void OnNavigatedFrom();
}
