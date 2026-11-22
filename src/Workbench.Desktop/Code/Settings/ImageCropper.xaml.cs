using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Workbench.Desktop;

// Picks the area of an image to keep, in the image's own pixels, at a fixed shape. Raises Changed as the frame moves.
public partial class ImageCropper : UserControl
{
    const double HandleReach = 12;   // How close (in screen pixels) the mouse must be to a corner to resize from it.
    const double MinScreenSize = 40; // The smallest the frame can get on screen.

    enum Drag { None, Move, TopLeft, TopRight, BottomLeft, BottomRight }

    BitmapSource? _source;
    double _aspect = 1;
    bool _round;
    Rect _area;                 // The chosen area, in image pixels.
    double _scale = 1;          // Screen pixels per image pixel.
    Point _origin;              // Where the image's top-left corner sits on the stage.
    Drag _drag;
    Point _dragStart;
    Rect _areaAtStart;

    public ImageCropper()
    {
        InitializeComponent();
        Stage.SizeChanged += (_, _) => Layout();
        Stage.MouseLeftButtonDown += StartDrag;
        Stage.MouseMove += MoveDrag;
        Stage.MouseLeftButtonUp += (_, _) => { _drag = Drag.None; Stage.ReleaseMouseCapture(); };
        Stage.MouseWheel += (_, e) => { Resize(e.Delta > 0 ? 1 / 1.1 : 1.1); e.Handled = true; };
        Stage.KeyDown += Keys;
    }

    public event Action? Changed;

    // The chosen area, rounded to whole pixels and kept inside the image.
    public Int32Rect Area
    {
        get
        {
            if (_source is null) return Int32Rect.Empty;
            var x = (int)Math.Round(_area.X);
            var y = (int)Math.Round(_area.Y);
            var width = Math.Min((int)Math.Round(_area.Width), _source.PixelWidth - x);
            var height = Math.Min((int)Math.Round(_area.Height), _source.PixelHeight - y);
            return new Int32Rect(x, y, Math.Max(1, width), Math.Max(1, height));
        }
    }

    // Shows an image with the frame on the largest area of the given shape in its middle (or on the area given). Round draws the frame as a circle.
    public void Load(BitmapSource source, double aspect, bool round, Int32Rect? area = null)
    {
        _source = source;
        _aspect = aspect;
        _round = round;
        var start = area ?? ProfileImages.Centered(source, aspect);
        _area = new Rect(start.X, start.Y, start.Width, start.Height);
        Photo.Source = source;
        Layout();
        Stage.Focus();
        Changed?.Invoke();
    }

    // Fits the image in the stage and places the frame, the shade and the handles over it.
    void Layout()
    {
        if (_source is null || Stage.ActualWidth <= 0) return;
        _scale = Math.Min(Stage.ActualWidth / _source.PixelWidth, Stage.ActualHeight / _source.PixelHeight);
        var shown = new Size(_source.PixelWidth * _scale, _source.PixelHeight * _scale);
        _origin = new Point((Stage.ActualWidth - shown.Width) / 2, (Stage.ActualHeight - shown.Height) / 2);
        Photo.Width = shown.Width;
        Photo.Height = shown.Height;
        Photo.Margin = new Thickness(_origin.X, _origin.Y, 0, 0);

        var frame = OnScreen(_area);
        Frame.Width = frame.Width;
        Frame.Height = frame.Height;
        Frame.CornerRadius = new CornerRadius(_round ? frame.Width / 2 : 0);
        Canvas.SetLeft(Frame, frame.X);
        Canvas.SetTop(Frame, frame.Y);
        Place(HandleTopLeft, frame.TopLeft);
        Place(HandleTopRight, frame.TopRight);
        Place(HandleBottomLeft, frame.BottomLeft);
        Place(HandleBottomRight, frame.BottomRight);

        Geometry hole = _round ? new EllipseGeometry(frame) : new RectangleGeometry(frame);
        Shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(_origin, shown)), hole);
    }

    static void Place(FrameworkElement handle, Point corner)
    {
        Canvas.SetLeft(handle, corner.X - handle.Width / 2);
        Canvas.SetTop(handle, corner.Y - handle.Height / 2);
    }

    Rect OnScreen(Rect area) => new(_origin.X + area.X * _scale, _origin.Y + area.Y * _scale, area.Width * _scale, area.Height * _scale);

    // A press near a corner resizes from it; inside the frame moves it.
    void StartDrag(object sender, MouseButtonEventArgs e)
    {
        Stage.Focus();
        var point = e.GetPosition(Stage);
        _drag = HitTest(point);
        if (_drag == Drag.None) return;
        _dragStart = point;
        _areaAtStart = _area;
        Stage.CaptureMouse();
        e.Handled = true;
    }

    void MoveDrag(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(Stage);
        if (_drag == Drag.None)
        {
            Stage.Cursor = HitTest(point) switch
            {
                Drag.Move => Cursors.SizeAll,
                Drag.TopLeft or Drag.BottomRight => Cursors.SizeNWSE,
                Drag.TopRight or Drag.BottomLeft => Cursors.SizeNESW,
                _ => null,
            };
            return;
        }
        var dx = (point.X - _dragStart.X) / _scale;
        var dy = (point.Y - _dragStart.Y) / _scale;
        if (_drag == Drag.Move) MoveTo(_areaAtStart.X + dx, _areaAtStart.Y + dy);
        else ResizeFromCorner(dx, dy);
    }

    Drag HitTest(Point point)
    {
        var frame = OnScreen(_area);
        bool Near(Point corner) => Math.Abs(point.X - corner.X) <= HandleReach && Math.Abs(point.Y - corner.Y) <= HandleReach;
        if (Near(frame.TopLeft)) return Drag.TopLeft;
        if (Near(frame.TopRight)) return Drag.TopRight;
        if (Near(frame.BottomLeft)) return Drag.BottomLeft;
        if (Near(frame.BottomRight)) return Drag.BottomRight;
        return frame.Contains(point) ? Drag.Move : Drag.None;
    }

    void MoveTo(double x, double y)
    {
        if (_source is null) return;
        _area.X = Math.Clamp(x, 0, _source.PixelWidth - _area.Width);
        _area.Y = Math.Clamp(y, 0, _source.PixelHeight - _area.Height);
        Layout();
        Changed?.Invoke();
    }

    // Keeps the opposite corner still and the shape fixed; the width follows whichever way the mouse moved further.
    void ResizeFromCorner(double dx, double dy)
    {
        if (_source is null) return;
        var start = _areaAtStart;
        var left = _drag is Drag.TopLeft or Drag.BottomLeft;
        var top = _drag is Drag.TopLeft or Drag.TopRight;
        var growX = left ? -dx : dx;
        var growY = (top ? -dy : dy) * _aspect;
        var width = start.Width + (Math.Abs(growX) > Math.Abs(growY) ? growX : growY);
        var anchorX = left ? start.Right : start.X;
        var anchorY = top ? start.Bottom : start.Y;
        // Room to grow toward the dragged corner, before running off the image.
        var roomX = left ? anchorX : _source.PixelWidth - anchorX;
        var roomY = top ? anchorY : _source.PixelHeight - anchorY;
        width = Math.Clamp(width, MinWidth(), Math.Min(roomX, roomY * _aspect));
        var height = width / _aspect;
        _area = new Rect(left ? anchorX - width : anchorX, top ? anchorY - height : anchorY, width, height);
        Layout();
        Changed?.Invoke();
    }

    // Grows or shrinks the frame about its centre (scroll wheel, + and -).
    void Resize(double factor)
    {
        if (_source is null) return;
        var centre = new Point(_area.X + _area.Width / 2, _area.Y + _area.Height / 2);
        var largest = ProfileImages.Centered(_source, _aspect).Width;
        var width = Math.Clamp(_area.Width * factor, MinWidth(), largest);
        var height = width / _aspect;
        _area = new Rect(centre.X - width / 2, centre.Y - height / 2, width, height);
        MoveTo(_area.X, _area.Y);
    }

    double MinWidth() => Math.Min(MinScreenSize / _scale, ProfileImages.Centered(_source!, _aspect).Width);

    void Keys(object sender, KeyEventArgs e)
    {
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 20 / _scale : 2 / _scale;
        switch (e.Key)
        {
            case Key.Left: MoveTo(_area.X - step, _area.Y); break;
            case Key.Right: MoveTo(_area.X + step, _area.Y); break;
            case Key.Up: MoveTo(_area.X, _area.Y - step); break;
            case Key.Down: MoveTo(_area.X, _area.Y + step); break;
            case Key.OemPlus or Key.Add: Resize(1 / 1.1); break;
            case Key.OemMinus or Key.Subtract: Resize(1.1); break;
            default: return;
        }
        e.Handled = true;
    }
}
