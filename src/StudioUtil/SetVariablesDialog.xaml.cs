using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace StudioUtil;

#nullable enable

/// <summary>
/// Represents the set variables dialog.
/// </summary>
public partial class SetVariablesDialog : Window
{
    public SetVariablesDialog()
    {
        InitializeComponent();
            
        Loaded += (s, e) =>
        {
            if (!string.IsNullOrEmpty(Target))
                txtTarget.Text = Target;

            if (!string.IsNullOrEmpty(Replacement))
                txtReplacement.Text = Replacement;

            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/StudioUtil;component/Resources/icon.png", UriKind.RelativeOrAbsolute));
            Title = Vsix.Name;
            lblTips.Content = "Sets variables across extension so other operations are faster";
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

    /// <summary>
    /// Gets or sets target input.
    /// </summary>
    public string TargetInput => txtTarget.Text.Trim();

    /// <summary>
    /// Gets or sets replacement input.
    /// </summary>
    public string ReplacementInput => txtReplacement.Text.Trim();

    /// <summary>
    /// The target.
    /// </summary>
    public string? Target;

    /// <summary>
    /// The replacement.
    /// </summary>
    public string? Replacement;

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}