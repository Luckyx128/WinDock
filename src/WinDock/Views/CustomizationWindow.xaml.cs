using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WinDock.ViewModels;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace WinDock.Views;

public partial class CustomizationWindow : Window
{
    private readonly MainViewModel _viewModel;
    private int _lastSelectedCategoryIndex = 0;
    private bool _isSearching = false;

    public CustomizationWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = SearchBox.Text.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            if (!_isSearching)
            {
                _lastSelectedCategoryIndex = SidebarList.SelectedIndex;
                _isSearching = true;
            }

            if (ClearSearchButton != null)
            {
                ClearSearchButton.Visibility = Visibility.Visible;
            }

            if (SidebarList.SelectedIndex != 4)
            {
                SidebarList.SelectedIndex = 4; // Switch to "Ver Tudo" tab
            }

            FilterAllCards(query);
        }
        else
        {
            if (ClearSearchButton != null)
            {
                ClearSearchButton.Visibility = Visibility.Collapsed;
            }

            if (_isSearching)
            {
                _isSearching = false;
                SidebarList.SelectedIndex = _lastSelectedCategoryIndex >= 0 ? _lastSelectedCategoryIndex : 0;
            }

            FilterAllCards(string.Empty);
        }
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
    }

    private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSearching && SidebarList != null && SidebarList.SelectedIndex >= 0)
        {
            _lastSelectedCategoryIndex = SidebarList.SelectedIndex;
        }
    }

    private void FilterAllCards(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            if (WidgetsGroup != null) WidgetsGroup.Visibility = Visibility.Visible;
            if (AppsGroup != null) AppsGroup.Visibility = Visibility.Visible;
            if (ShortcutsGroup != null) ShortcutsGroup.Visibility = Visibility.Visible;
            if (ThemesGroup != null) ThemesGroup.Visibility = Visibility.Visible;
            if (BehaviorGroup != null) BehaviorGroup.Visibility = Visibility.Visible;
            return;
        }

        string q = query.ToLowerInvariant();
        bool matchWidgets = q.Contains("widg") || q.Contains("cpu") || q.Contains("ram") || q.Contains("gpu") ||
                            q.Contains("med") || q.Contains("you") || q.Contains("steam") || q.Contains("disc") ||
                            q.Contains("ia") || q.Contains("ai") || q.Contains("relog") || q.Contains("cloc") ||
                            q.Contains("hardw") || q.Contains("gauge") || q.Contains("telemetr");

        bool matchApps = q.Contains("app") || q.Contains("ord") || q.Contains("fil") || q.Contains("doc") ||
                         q.Contains("mov") || q.Contains("fix") || q.Contains("desafix") || q.Contains("foco") ||
                         q.Contains("esquerd") || q.Contains("direit");

        bool matchShortcuts = q.Contains("atalh") || q.Contains("short") || q.Contains("janel") || q.Contains("nov") ||
                              q.Contains("exec") || q.Contains("exe") || q.Contains("lnk") || q.Contains("adicion");

        bool matchThemes = q.Contains("tem") || q.Contains("cor") || q.Contains("accent") || q.Contains("palet") ||
                           q.Contains("estil") || q.Contains("dark") || q.Contains("glass");

        bool matchBehavior = q.Contains("comp") || q.Contains("pos") || q.Contains("top") || q.Contains("bas") ||
                             q.Contains("barr") || q.Contains("nativ") || q.Contains("desk") || q.Contains("wall") ||
                             q.Contains("sist");

        if (!matchWidgets && !matchApps && !matchShortcuts && !matchThemes && !matchBehavior)
        {
            matchWidgets = true;
            matchApps = true;
            matchShortcuts = true;
            matchThemes = true;
            matchBehavior = true;
        }

        if (WidgetsGroup != null) WidgetsGroup.Visibility = matchWidgets ? Visibility.Visible : Visibility.Collapsed;
        if (AppsGroup != null) AppsGroup.Visibility = matchApps ? Visibility.Visible : Visibility.Collapsed;
        if (ShortcutsGroup != null) ShortcutsGroup.Visibility = matchShortcuts ? Visibility.Visible : Visibility.Collapsed;
        if (ThemesGroup != null) ThemesGroup.Visibility = matchThemes ? Visibility.Visible : Visibility.Collapsed;
        if (BehaviorGroup != null) BehaviorGroup.Visibility = matchBehavior ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Selecionar Aplicativo ou Atalho",
            Filter = "Programas e Atalhos (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Todos os Arquivos (*.*)|*.*",
            CheckFileExists = true
        };

        if (openFileDialog.ShowDialog(this) == true)
        {
            ShortcutPathTextBox.Text = openFileDialog.FileName;
            if (string.IsNullOrWhiteSpace(ShortcutNameTextBox.Text))
            {
                ShortcutNameTextBox.Text = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
            }
        }
    }

    private void AddShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        string path = ShortcutPathTextBox.Text.Trim();
        string name = ShortcutNameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show(this, "Por favor, selecione ou digite o caminho do aplicativo.", "WinDock", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileNameWithoutExtension(path);
        }

        _viewModel.AddShortcut(name, path);

        ShortcutNameTextBox.Clear();
        ShortcutPathTextBox.Clear();
    }
}
