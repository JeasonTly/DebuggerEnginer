using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TestEngineer.UI;

/// <summary>24×24 描边矢量图标（对应原型 svg viewBox 24，stroke 1.7，圆端）。颜色取自 Foreground。</summary>
public sealed class Icon : Control
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(Icon), new PropertyMetadata(null));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(Icon), new PropertyMetadata(1.7));

    public Icon()
    {
        IsTabStop = false;
        Focusable = false;
        Width = 16;
        Height = 16;
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }
}
