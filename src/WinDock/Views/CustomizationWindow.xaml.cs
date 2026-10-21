using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using WinDock.ViewModels;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace WinDock.Views;

public partial class CustomizationWindow : Window
{
    private readonly MainViewModel _viewModel;

    public CustomizationWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
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