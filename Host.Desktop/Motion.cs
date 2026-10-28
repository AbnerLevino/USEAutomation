using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AutomationPlatform.Host.Desktop;

/// <summary>
/// Movimentos leves da conversa: a fala sobe e aparece, os pontinhos de "digitando" pulam em onda e a
/// rolagem acompanha com suavidade. Respeita a opção "reduzir animações" do Windows: sem ela, tudo
/// aparece direto, sem movimento.
/// </summary>
public static class Motion
{
    private static bool Enabled => SystemParameters.ClientAreaAnimation;
    private static IEasingFunction EaseOut => new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>A fala sobe uns poucos pixels enquanto aparece.</summary>
    public static void Enter(FrameworkElement element, double fromY = 14)
    {
        if (!Enabled) return;

        var move = new TranslateTransform(0, fromY);
        element.RenderTransform = move;
        element.Opacity = 0;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut };
        fade.Completed += (_, _) => { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1; };
        element.BeginAnimation(UIElement.OpacityProperty, fade);

        var rise = new DoubleAnimation(fromY, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = EaseOut };
        rise.Completed += (_, _) => { move.BeginAnimation(TranslateTransform.YProperty, null); element.RenderTransform = Transform.Identity; };
        move.BeginAnimation(TranslateTransform.YProperty, rise);
    }

    /// <summary>Os três pontinhos de "digitando…": pulam e acendem em onda, sem parar.</summary>
    public static FrameworkElement TypingDots(Brush color)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 7, 2, 7) };
        for (var i = 0; i < 3; i++)
        {
            var move = new TranslateTransform();
            var dot = new Ellipse
            {
                Width = 7, Height = 7, Fill = color,
                Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0),
                RenderTransform = move, Opacity = Enabled ? 0.35 : 1
            };
            panel.Children.Add(dot);
            if (!Enabled) continue;

            // Cada ponto começa um pouco depois do anterior: forma a onda.
            Animation(TimeSpan.FromMilliseconds(i * 170), out var up, out var glow);
            move.BeginAnimation(TranslateTransform.YProperty, up);
            dot.BeginAnimation(UIElement.OpacityProperty, glow);
        }
        return panel;

        static void Animation(TimeSpan begin, out DoubleAnimation up, out DoubleAnimation glow)
        {
            var length = TimeSpan.FromMilliseconds(380);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            up = new DoubleAnimation(0, -5, length)
                { BeginTime = begin, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
            glow = new DoubleAnimation(0.35, 1, length)
                { BeginTime = begin, AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
        }
    }
}

/// <summary>Rolagem animada até o fim da conversa.</summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(SmoothScroll),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));

    public static double GetOffset(DependencyObject d) => (double)d.GetValue(OffsetProperty);
    public static void SetOffset(DependencyObject d, double value) => d.SetValue(OffsetProperty, value);

    public static void ToEnd(ScrollViewer viewer)
    {
        viewer.UpdateLayout();
        var target = viewer.ScrollableHeight;
        if (Math.Abs(target - viewer.VerticalOffset) < 1) return;

        if (!SystemParameters.ClientAreaAnimation)
        {
            viewer.ScrollToVerticalOffset(target);
            return;
        }

        viewer.BeginAnimation(OffsetProperty, new DoubleAnimation(viewer.VerticalOffset, target, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }
}
