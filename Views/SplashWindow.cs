using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MotionForge.Services;

namespace MotionForge.Views;

/// <summary>
/// Заставка при запуске: знак приложения, название, версия и полоса загрузки.
/// Показывается недолго и закрывается, когда главное окно уже готово.
/// </summary>
public class SplashWindow : Window
{
    private readonly Border _barFill;

    public SplashWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 520;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        UseLayoutRounding = true;

        var card = new Border
        {
            CornerRadius = new CornerRadius(20),
            Margin = new Thickness(14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 6,
                Opacity = 0.55,
                Color = Colors.Black,
            },
        };
        var gradient = new LinearGradientBrush { StartPoint = new Point(0.05, 0), EndPoint = new Point(0.95, 1) };
        gradient.GradientStops.Add(new GradientStop(Branding.BackgroundTop, 0));
        gradient.GradientStops.Add(new GradientStop(Branding.BackgroundMiddle, 0.55));
        gradient.GradientStops.Add(new GradientStop(Branding.BackgroundBottom, 1));
        card.Background = gradient;

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var logo = new Image
        {
            Source = Branding.Render(132, false, false),
            Width = 132,
            Height = 132,
            Margin = new Thickness(0, 34, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };

        var title = new TextBlock
        {
            Text = "MotionForge",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 33,
            FontWeight = FontWeights.Light,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };

        var subtitle = new TextBlock
        {
            Text = L10n.T("Студия двумерной анимации"),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var text = new StackPanel();
        text.Children.Add(title);
        text.Children.Add(subtitle);

        var header = new StackPanel();
        header.Children.Add(logo);
        header.Children.Add(text);
        Grid.SetRow(header, 0);
        content.Children.Add(header);

        _barFill = new Border
        {
            Height = 3,
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(1.5),
            Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
        };
        var track = new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            ClipToBounds = true,
            Child = _barFill,
            Margin = new Thickness(60, 0, 60, 0),
        };

        var version = new TextBlock
        {
            Text = "версия " + Updates.VersionText,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 26),
        };

        var footer = new StackPanel();
        footer.Children.Add(track);
        footer.Children.Add(version);
        Grid.SetRow(footer, 2);
        content.Children.Add(footer);

        card.Child = content;
        Content = card;

        Opacity = 0;
        Loaded += (s, e) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            var animation = new DoubleAnimation(-120, 460, TimeSpan.FromMilliseconds(1400))
            {
                RepeatBehavior = RepeatBehavior.Forever,
            };
            _barFill.RenderTransform = new TranslateTransform();
            _barFill.RenderTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        };
    }
}
