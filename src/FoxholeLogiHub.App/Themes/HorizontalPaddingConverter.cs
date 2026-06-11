using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FoxholeLogiHub.App;

/// <summary>
/// Ne garde que la composante horizontale d'un Padding. Les champs ayant une hauteur fixe
/// (36 px, contenu centré), un padding vertical ne ferait que rogner le texte.
/// </summary>
public sealed class HorizontalPaddingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Thickness t ? new Thickness(t.Left, 0, t.Right, 0) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
