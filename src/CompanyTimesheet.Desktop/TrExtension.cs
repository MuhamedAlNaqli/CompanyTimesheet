using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;

namespace CompanyTimesheet.Desktop;

/// <summary>
/// A text of a window that follows the language: <c>Text="{em:Tr 'Moja šihtarica'}"</c>. The Bosnian wording is the key; the English one is
/// in <see cref="Translations"/>. The text is bound to the version of <see cref="Loc"/>, so it is read again when the language changes.
/// </summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string text)
    {
        Text = text;
    }

    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(Loc.Version))
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
            Converter = TrConverter.Instance,
            ConverterParameter = Text
        };
}

/// <summary>Turns the version of the language into the wording of the text that is the parameter</summary>
public sealed class TrConverter : IValueConverter
{
    public static TrConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Loc.T(parameter as string ?? "");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
