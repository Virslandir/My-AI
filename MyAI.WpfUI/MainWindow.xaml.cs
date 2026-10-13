using System.Text;
using System.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using MyAI.Actions;
using Microsoft.Win32;

namespace MyAI.WpfUI;

public partial class MainWindow : Window
{
    private readonly FileSearchService _searchService = new();
    private CancellationTokenSource? _searchCancellation;
    private ICollectionView? _resultsView;
    private string? _sortProperty;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    public MainWindow()
    {
        InitializeComponent();
        SearchLocationTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        StatusTextBlock.Text = "Enter one or more names and extensions, then search.";
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SearchButton.IsEnabled = false;
            AbortButton.IsEnabled = true;
            ResultsListView.ItemsSource = null;
            StatusTextBlock.Text = "Searching…";
            var query = new FileSearchQuery(FileSearchQuery.ParseAlternatives(SearchLocationTextBox.Text),
                FileSearchQuery.ParseAlternatives(NameTermsTextBox.Text),
                FileSearchQuery.ParseAlternatives(ExtensionsTextBox.Text));
            if (query.NameTerms.Count == 0 && query.Extensions.Count == 0)
            {
                MessageBox.Show("Enter at least one file name term or extension.", "Search criteria", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _searchCancellation = new CancellationTokenSource();
            var response = await _searchService.SearchAsync(query, cancellationToken: _searchCancellation.Token);
            _resultsView = CollectionViewSource.GetDefaultView(response.Files);
            _resultsView.SortDescriptions.Add(new SortDescription(nameof(FileSearchResult.Name), ListSortDirection.Ascending));
            _sortProperty = nameof(FileSearchResult.Name);
            _sortDirection = ListSortDirection.Ascending;
            ResultsListView.ItemsSource = _resultsView;
            StatusTextBlock.Text = response.SkippedFolderCount == 0
                ? $"{response.Files.Count:N0} file(s) found. Double-click a result to show it in Explorer."
                : $"{response.Files.Count:N0} file(s) found. {response.SkippedFolderCount:N0} inaccessible folder(s) skipped.";
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Search aborted.";
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = "Search could not be completed.";
            MessageBox.Show(exception.Message, "Search error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _searchCancellation?.Dispose();
            _searchCancellation = null;
            SearchButton.IsEnabled = true;
            AbortButton.IsEnabled = false;
        }
    }

    private void AbortButton_Click(object sender, RoutedEventArgs e) => _searchCancellation?.Cancel();

    private void ResultsColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader { Tag: string property }) return;

        _sortDirection = _sortProperty == property && _sortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _sortProperty = property;

        if (_resultsView is null) return;

        _resultsView.SortDescriptions.Clear();
        _resultsView.SortDescriptions.Add(new SortDescription(property, _sortDirection));
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder to search" };
        if (dialog.ShowDialog() != true) return;
        SearchLocationTextBox.Text = string.IsNullOrWhiteSpace(SearchLocationTextBox.Text)
            ? dialog.FolderName
            : $"{SearchLocationTextBox.Text}; {dialog.FolderName}";
    }

    private void ResultsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ShowSelectedFileInExplorer();
    private void ShowInExplorer_Click(object sender, RoutedEventArgs e) => ShowSelectedFileInExplorer();

    private void ShowSelectedFileInExplorer()
    {
        if (ResultsListView.SelectedItem is not FileSearchResult file) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file.FullPath}\"") { UseShellExecute = true });
    }
}
