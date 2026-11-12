using System.Windows;
using System.Windows.Controls;

namespace TestEngineer.Desktop.Controls;

public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    public static readonly DependencyProperty BindPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BindPassword",
            typeof(bool),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(false, OnBindPasswordChanged));

    private static readonly DependencyProperty UpdatingProperty =
        DependencyProperty.RegisterAttached("Updating", typeof(bool), typeof(PasswordBoxHelper), new PropertyMetadata(false));

    public static void SetBoundPassword(DependencyObject element, string value) => element.SetValue(BoundPasswordProperty, value);

    public static string GetBoundPassword(DependencyObject element) => (string)element.GetValue(BoundPasswordProperty);

    public static void SetBindPassword(DependencyObject element, bool value) => element.SetValue(BindPasswordProperty, value);

    public static bool GetBindPassword(DependencyObject element) => (bool)element.GetValue(BindPasswordProperty);

    private static void OnBindPasswordChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not PasswordBox box)
        {
            return;
        }

        if (args.NewValue is true)
        {
            box.PasswordChanged += OnPasswordChanged;
        }
        else
        {
            box.PasswordChanged -= OnPasswordChanged;
        }
    }

    private static void OnBoundPasswordChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not PasswordBox box || box.GetValue(UpdatingProperty) is true)
        {
            return;
        }

        box.SetValue(UpdatingProperty, true);
        box.Password = args.NewValue as string ?? "";
        box.SetValue(UpdatingProperty, false);
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs args)
    {
        if (sender is not PasswordBox box || box.GetValue(UpdatingProperty) is true)
        {
            return;
        }

        box.SetValue(UpdatingProperty, true);
        SetBoundPassword(box, box.Password);
        box.SetValue(UpdatingProperty, false);
    }
}
