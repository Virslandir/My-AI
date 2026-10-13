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
    private readonly Dictionary<string, List<string>> _columnValues = new();
    private readonly Dictionary<string, HashSet<string>> _selectedColumnValues = new();
    private string? _filterProperty;

    public MainWindow()
    {
        InitializeComponent();
        SearchLocationTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify) + "\\Downloads";
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
            ConfigureColumnFilters(response.Files);
            _resultsView.Filter = MatchesColumnFilters;
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

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: string property } button || _resultsView is null) return;

        _filterProperty = property;
        FilterSearchTextBox.Text = string.Empty;
        PopulateFilterTree();
        FilterPopup.PlacementTarget = button;
        FilterPopup.IsOpen = true;
        FilterSearchTextBox.Focus();
    }

    private void FilterSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_filterProperty is not null) PopulateFilterTree();
    }

    private void ConfigureColumnFilters(IEnumerable<FileSearchResult> files)
    {
        foreach (var property in new[] { nameof(FileSearchResult.Name), nameof(FileSearchResult.FullPath), nameof(FileSearchResult.LastWriteTime) })
        {
            var values = files.Select(file => GetColumnValue(file, property))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _columnValues[property] = values;
            _selectedColumnValues[property] = new HashSet<string>(values, StringComparer.CurrentCultureIgnoreCase);
        }
    }

    private bool MatchesColumnFilters(object item)
    {
        if (item is not FileSearchResult file) return false;

        return _selectedColumnValues.All(filter => filter.Value.Contains(GetColumnValue(file, filter.Key)));
    }

    private void PopulateFilterTree()
    {
        if (_filterProperty is null || !_columnValues.TryGetValue(_filterProperty, out var values)) return;

        var selectedValues = _selectedColumnValues[_filterProperty];
        var searchText = FilterSearchTextBox.Text.Trim();
        var visibleValues = values.Where(value => value.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)).ToList();
        var rootCheckBox = new CheckBox
        {
            Content = "Select all",
            IsThreeState = true,
            IsChecked = selectedValues.Count == values.Count ? true : selectedValues.Count == 0 ? false : null,
            Tag = new FilterOption(_filterProperty, null)
        };
        rootCheckBox.Checked += FilterCheckBox_Changed;
        rootCheckBox.Unchecked += FilterCheckBox_Changed;
        var root = new TreeViewItem { Header = rootCheckBox, IsExpanded = true };

        foreach (var value in visibleValues)
        {
            var valueCheckBox = new CheckBox { Content = value, IsChecked = selectedValues.Contains(value), Tag = new FilterOption(_filterProperty, value) };
            valueCheckBox.Checked += FilterCheckBox_Changed;
            valueCheckBox.Unchecked += FilterCheckBox_Changed;
            root.Items.Add(new TreeViewItem { Header = valueCheckBox });
        }

        FilterTreeView.Items.Clear();
        FilterTreeView.Items.Add(root);
    }

    private void FilterCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: FilterOption option } checkBox || !_selectedColumnValues.TryGetValue(option.Property, out var selectedValues)) return;

        if (option.Value is null)
        {
            selectedValues.Clear();
            if (checkBox.IsChecked == true) selectedValues.UnionWith(_columnValues[option.Property]);
        }
        else if (checkBox.IsChecked == true)
        {
            selectedValues.Add(option.Value);
        }
        else
        {
            selectedValues.Remove(option.Value);
        }

        _resultsView?.Refresh();
        PopulateFilterTree();
    }

    private static string GetColumnValue(FileSearchResult file, string property) => property switch
    {
        nameof(FileSearchResult.Name) => file.Name,
        nameof(FileSearchResult.FullPath) => file.FullPath,
        nameof(FileSearchResult.LastWriteTime) => file.LastWriteTime.ToString(),
        _ => string.Empty
    };

    private sealed record FilterOption(string Property, string? Value);

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
