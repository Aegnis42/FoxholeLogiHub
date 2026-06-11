using System.Windows;

namespace FoxholeLogiHub.App;

/// <summary>
/// Propriété attachée « placeholder » : <c>app:Hint.Text="…"</c> sur un TextBox ou un ComboBox.
/// Le texte gris s'affiche tant que le champ est vide (rendu dans les templates du thème).
/// </summary>
public static class Hint
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Hint), new FrameworkPropertyMetadata(""));

    public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);
}
