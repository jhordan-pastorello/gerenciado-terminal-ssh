using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StorageTerm.Models;

namespace StorageTerm.Dialogs;

public partial class ConfirmDialog : Window
{
    public bool DoNotAskAgain => DontAskCheck.IsChecked == true;

    public ConfirmDialog(string title, string message, string confirmText, string? checkboxText = null)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;

        if (string.IsNullOrWhiteSpace(checkboxText))
        {
            DontAskCheck.Visibility = Visibility.Collapsed;
        }
        else
        {
            DontAskCheck.Content = checkboxText;
        }

        SourceInitialized += (_, _) => Services.WindowTheme.Apply(this);
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

