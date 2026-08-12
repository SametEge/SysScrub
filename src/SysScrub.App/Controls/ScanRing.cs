using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SysScrub.App.Controls;

/// <summary>
/// Uygulamanın imza görseli: gradyan dolgulu dairesel ilerleme halkası.
/// Belirsiz modda döner, belirli modda değere yumuşak geçiş yapar.
///
/// Özel çizim tercih edildi çünkü tema değişiminde anında uyum sağlaması,
/// her DPI'da keskin kalması ve harici grafik kütüphanesi taşımaması gerekiyor.
/// </summary>
public sealed class ScanRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ScanRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, null, CoerceProgress));

    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(ScanRing),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnIndeterminateChanged));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(ScanRing),
        new FrameworkPropertyMetadata(14d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(ScanRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush), typeof(Brush), typeof(ScanRing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Belirsiz modda dönen yayın konumu; storyboard bunu sürer.</summary>
    private static readonly DependencyProperty SpinProperty = DependencyProperty.Register(
        nameof(Spin), typeof(double), typeof(ScanRing),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    private Storyboard? _spinStoryboard;

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush? ProgressBrush
    {
        get => (Brush?)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    private double Spin
    {
        get => (double)GetValue(SpinProperty);
        set => SetValue(SpinProperty, value);
    }

    private static object CoerceProgress(DependencyObject d, object value) => Math.Clamp((double)value, 0d, 1d);

    private static void OnIndeterminateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ring = (ScanRing)d;

        if ((bool)e.NewValue)
        {
            ring.StartSpin();
        }
        else
        {
            ring.StopSpin();
        }
    }

    private void StartSpin()
    {
        // Sistemin "animasyonları azalt" ayarı açıksa dönme yapılmaz.
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var animation = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(1.6)))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        _spinStoryboard = new Storyboard();
        _spinStoryboard.Children.Add(animation);
        Storyboard.SetTarget(animation, this);
        Storyboard.SetTargetProperty(animation, new PropertyPath(SpinProperty));
        _spinStoryboard.Begin();
    }

    private void StopSpin()
    {
        _spinStoryboard?.Stop();
        _spinStoryboard = null;
        Spin = 0;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);

        if (size <= 0)
        {
            return;
        }

        double thickness = Math.Min(Thickness, size / 4d);
        double radius = (size - thickness) / 2d;
        var center = new Point(ActualWidth / 2d, ActualHeight / 2d);

        if (TrackBrush is { } track)
        {
            dc.DrawEllipse(null, new Pen(track, thickness), center, radius, radius);
        }

        Brush progressBrush = ProgressBrush ?? Brushes.White;
        var pen = new Pen(progressBrush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        // Belirsiz modda sabit uzunlukta bir yay döner; belirli modda yay değere göre büyür.
        double startAngle = IsIndeterminate ? Spin - 90d : -90d;
        double sweep = IsIndeterminate ? 90d : Progress * 360d;

        if (sweep <= 0.01)
        {
            return;
        }

        // Tam tur çizilemez (başlangıç ve bitiş çakışır); az eksik bırakıp elips olarak çiz.
        if (sweep >= 359.99)
        {
            dc.DrawEllipse(null, new Pen(progressBrush, thickness), center, radius, radius);
            return;
        }

        dc.DrawGeometry(null, pen, BuildArc(center, radius, startAngle, sweep));
    }

    private static Geometry BuildArc(Point center, double radius, double startAngle, double sweepAngle)
    {
        Point start = PointOnCircle(center, radius, startAngle);
        Point end = PointOnCircle(center, radius, startAngle + sweepAngle);

        var geometry = new StreamGeometry();

        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(radius, radius), 0, sweepAngle > 180d, SweepDirection.Clockwise, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        double radians = degrees * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
