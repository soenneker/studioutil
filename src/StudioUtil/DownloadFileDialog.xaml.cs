using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace StudioUtil;

#nullable enable

public partial class DownloadFileDialog : Window
{
    public DownloadFileDialog()
    {
        InitializeComponent();
            
        Loaded += (s, e) =>
        {
            if (!string.IsNullOrEmpty(Target))
                txtTarget.Text = Target;

            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/StudioUtil;component/Resources/icon.png", UriKind.RelativeOrAbsolute));
            Title = Vsix.Name;
            lblTips.Content = "Downloads from a particular URI and creates the file from the URI's file name";
            txtTarget.Focus();
            txtTarget.CaretIndex = 0;
            txtTarget.Select(0, txtTarget.Text!.Length);

            txtTarget.PreviewKeyDown += (a, b) =>
            {
                if (b.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
            };

        };
    }

    public string TargetInput => txtTarget.Text.Trim();

    public string? Target;

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}