using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using CloudRedirect.Services;

namespace CloudRedirect.Dialogs;

public partial class UpdateChangelogDialog : Wpf.Ui.Controls.FluentWindow
{
    public UpdateChangelogDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadChangelogAsync();
    }

    private async Task LoadChangelogAsync()
    {
        LoadingState.Visibility = Visibility.Visible;
        ChangelogScroll.Visibility = Visibility.Collapsed;

        try
        {
            var items = await AppUpdater.FetchReleasesChangelogAsync();
            ReleasesItemsControl.ItemsSource = items;
        }
        catch (Exception ex)
        {
            App.LogStartup($"Failed to load changelog items: {ex.Message}");
        }
        finally
        {
            LoadingState.Visibility = Visibility.Collapsed;
            ChangelogScroll.Visibility = Visibility.Visible;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadChangelogAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
