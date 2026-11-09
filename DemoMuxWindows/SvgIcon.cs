using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
namespace DemoMux;

// Render the checked-in SVG source directly, so both platforms share one icon definition.
internal sealed class SvgIcon : Control
{
    private static readonly Dictionary<string, Geometry[]> cache = [];
    private readonly Geometry[] shapes;
    public SvgIcon(string name)
    {
        shapes = Load(name); Width = Height = 18; IsHitTestVisible = false; Focusable = false;
        SetBinding(ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
    }
    internal static Geometry[] Load(string name)
    {
        if (cache.TryGetValue(name, out var found)) return found;
        using var stream = typeof(SvgIcon).Assembly.GetManifestResourceStream("DemoMux.Icons." + name + ".svg") ?? throw new IOException("缺少图标：" + name);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Invalid icon");
        if ((string?)root.Attribute("viewBox") != "0 0 24 24") throw new InvalidDataException("Unsupported icon viewport");
        static double N(XElement e, string key, double fallback = 0) => e.Attribute(key) is { } a ? double.Parse(a.Value,CultureInfo.InvariantCulture) : fallback;
        var geometries = root.Elements().Select(e => e.Name.LocalName switch {
            "path" => Geometry.Parse((string?)e.Attribute("d") ?? ""),
            "circle" => (Geometry)new EllipseGeometry(new Point(N(e,"cx"),N(e,"cy")),N(e,"r"),N(e,"r")),
            "rect" => new RectangleGeometry(new Rect(N(e,"x"),N(e,"y"),N(e,"width"),N(e,"height")),N(e,"rx"),N(e,"ry",N(e,"rx"))),
            "line" => new LineGeometry(new Point(N(e,"x1"),N(e,"y1")),new Point(N(e,"x2"),N(e,"y2"))),
            _ => throw new InvalidDataException("Unsupported icon element: " + e.Name.LocalName)
        }).ToArray();
        foreach (var geometry in geometries) geometry.Freeze();
        return cache[name] = geometries;
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var size = Math.Min(ActualWidth,ActualHeight);
        drawing.PushTransform(new TranslateTransform((ActualWidth-size)/2,(ActualHeight-size)/2));
        drawing.PushTransform(new ScaleTransform(size/24,size/24));
        var pen = new Pen(Foreground,2) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round };
        foreach(var geometry in shapes) drawing.DrawGeometry(null,pen,geometry);
        drawing.Pop(); drawing.Pop();
    }
}
