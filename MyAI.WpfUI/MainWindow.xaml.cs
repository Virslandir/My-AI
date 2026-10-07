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
using MyAI.Actions;

namespace MyAI.WpfUI;

public partial class MainWindow : Window
{
    private readonly FileSearchService _searchService = new();

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
            ResultsListView.ItemsSource = null;
            StatusTextBlock.Text = "Searching…";
            var query = new FileSearchQuery(SearchLocationTextBox.Text,
                FileSearchQuery.ParseAlternatives(NameTermsTextBox.Text),
                FileSearchQuery.ParseAlternatives(ExtensionsTextBox.Text));
            if (query.NameTerms.Count == 0 && query.Extensions.Count == 0)
            {
                MessageBox.Show("Enter at least one file name term or extension.", "Search criteria", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var response = await _searchService.SearchAsync(query);
            ResultsListView.ItemsSource = response.Files;
            StatusTextBlock.Text = response.SkippedFolderCount == 0
                ? $"{response.Files.Count:N0} file(s) found. Double-click a result to show it in Explorer."
                : $"{response.Files.Count:N0} file(s) found. {response.SkippedFolderCount:N0} inaccessible folder(s) skipped.";
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = "Search could not be completed.";
            MessageBox.Show(exception.Message, "Search error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SearchButton.IsEnabled = true; }
    }

    private void ResultsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ShowSelectedFileInExplorer();
    private void ShowInExplorer_Click(object sender, RoutedEventArgs e) => ShowSelectedFileInExplorer();

    private void ShowSelectedFileInExplorer()
    {
        if (ResultsListView.SelectedItem is not FileSearchResult file) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file.FullPath}\"") { UseShellExecute = true });
    }
}
