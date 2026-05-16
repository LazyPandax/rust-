using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class RustPlusToolsWindow : Window
{
    private readonly RustPlusToolsDataService _tools = new();
    private RustPlusToolEntry? _selected;
    private bool _ready;

    public RustPlusToolsWindow()
    {
        InitializeComponent();

        try
        {
            _tools.Load();
            CctvMonumentBox.ItemsSource = _tools.CctvMonuments;
            if (CctvMonumentBox.Items.Count > 0)
                CctvMonumentBox.SelectedIndex = 0;

            _ready = true;
            SearchBox.Text = "rifle";
            RunSearch();
        }
        catch (Exception ex)
        {
            ItemOutput.Text = "Rust++ tool data could not be loaded.\r\n\r\n" + ex.Message;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready && SearchBox.Text.Trim().Length >= 2)
            RunSearch();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSearch();
            e.Handled = true;
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => RunSearch();

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = ResultsList.SelectedItem as RustPlusToolEntry;
        RefreshOutputs();
    }

    private void Craft_Click(object sender, RoutedEventArgs e)
        => CraftOutput.Text = _tools.FormatCraft(_selected, ParseQuantity(CraftQuantityBox));

    private void Recycle_Click(object sender, RoutedEventArgs e)
        => RecycleOutput.Text = _tools.FormatRecycle(_selected, ParseQuantity(RecycleQuantityBox), SelectedRecyclerType());

    private void Research_Click(object sender, RoutedEventArgs e)
        => ResearchOutput.Text = _tools.FormatResearch(_selected);

    private void Decay_Click(object sender, RoutedEventArgs e)
        => DecayOutput.Text = _tools.FormatDecay(_selected);

    private void Upkeep_Click(object sender, RoutedEventArgs e)
        => UpkeepOutput.Text = _tools.FormatUpkeep(_selected);

    private void CctvMonumentBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => CctvOutput.Text = _tools.FormatCctv(CctvMonumentBox.SelectedItem as string);

    private void RunSearch()
    {
        var results = _tools.Search(SearchBox.Text).ToList();
        ResultsList.ItemsSource = results;
        if (results.Count > 0)
            ResultsList.SelectedIndex = 0;
        else
        {
            _selected = null;
            RefreshOutputs();
        }
    }

    private void RefreshOutputs()
    {
        SelectedSummary.Text = _selected is null
            ? "No selection"
            : $"{_selected.Name}\r\n{_selected.KindLabel}";

        ItemOutput.Text = _tools.FormatItemDetails(_selected);
        CraftOutput.Text = _tools.FormatCraft(_selected, ParseQuantity(CraftQuantityBox));
        RecycleOutput.Text = _tools.FormatRecycle(_selected, ParseQuantity(RecycleQuantityBox), SelectedRecyclerType());
        ResearchOutput.Text = _tools.FormatResearch(_selected);
        DecayOutput.Text = _tools.FormatDecay(_selected);
        UpkeepOutput.Text = _tools.FormatUpkeep(_selected);
    }

    private static int ParseQuantity(TextBox box)
    {
        return int.TryParse(box.Text, out var value) && value > 0 ? Math.Min(value, 9999) : 1;
    }

    private string SelectedRecyclerType()
    {
        if (RecyclerTypeBox.SelectedItem is ComboBoxItem item && item.Content is string text)
            return text;
        return "recycler";
    }
}
