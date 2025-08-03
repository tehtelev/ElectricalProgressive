using System;
using Cairo;

namespace ElectricalProgressive.Utils;

public class IconStorage
{
  public static void DrawTool1x3(
    Context cr,
    int x,
    int y,
    float width,
    float height,
    double[] rgba)
  {
    Matrix matrix = cr.Matrix;
    cr.Save();
    float num1 = 129f;
    float num2 = 129f;
    float num3 = Math.Min(width / num1, height / num2);
    matrix.Translate(x + (double)Math.Max(0.0f, (float)((width - num1 * (double)num3) / 2.0)),
      y + (double)Math.Max(0.0f, (float)((height - num2 * (double)num3) / 2.0)));
    matrix.Scale(num3, num3);
    cr.Matrix = matrix;
    cr.Operator = Operator.Over;
    Pattern source3 = new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
    cr.SetSource(source3);
    cr.NewPath();
    cr.MoveTo(3381.0 / 64.0, 949.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 949.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 2549.0 / 64.0);
    cr.LineTo(3381.0 / 64.0, 2549.0 / 64.0);
    cr.ClosePath();
    cr.MoveTo(3381.0 / 64.0, 949.0 / 64.0);
    cr.Tolerance = 0.1;
    cr.Antialias = Antialias.Default;
    cr.FillRule = FillRule.Winding;
    cr.FillPreserve();
    source3?.Dispose();
    cr.Operator = Operator.Over;
    cr.LineWidth = 8.0;
    cr.MiterLimit = 10.0;
    cr.LineCap = LineCap.Butt;
    cr.LineJoin = LineJoin.Miter;
    Pattern source4 = new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
    cr.SetSource(source4);
    cr.NewPath();
    cr.MoveTo(3381.0 / 64.0, 949.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 949.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 2549.0 / 64.0);
    cr.LineTo(3381.0 / 64.0, 2549.0 / 64.0);
    cr.ClosePath();
    cr.MoveTo(3381.0 / 64.0, 949.0 / 64.0);
    cr.Tolerance = 0.1;
    cr.Antialias = Antialias.Default;
    cr.StrokePreserve();
    source4?.Dispose();
    cr.Operator = Operator.Over;
    Pattern source7 = new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
    cr.SetSource(source7);
    cr.NewPath();
    cr.MoveTo(3381.0 / 64.0, 3381.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 3381.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 4981.0 / 64.0);
    cr.LineTo(3381.0 / 64.0, 4981.0 / 64.0);
    cr.ClosePath();
    cr.MoveTo(3381.0 / 64.0, 3381.0 / 64.0);
    cr.Tolerance = 0.1;
    cr.Antialias = Antialias.Default;
    cr.FillRule = FillRule.Winding;
    cr.FillPreserve();
    source7?.Dispose();
    cr.Operator = Operator.Over;
    cr.LineWidth = 8.0;
    cr.MiterLimit = 10.0;
    cr.LineCap = LineCap.Butt;
    cr.LineJoin = LineJoin.Miter;
    Pattern source8 = new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
    cr.SetSource(source8);
    cr.NewPath();
    cr.MoveTo(3381.0 / 64.0, 3381.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 3381.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 4981.0 / 64.0);
    cr.LineTo(3381.0 / 64.0, 4981.0 / 64.0);
    cr.ClosePath();
    cr.MoveTo(3381.0 / 64.0, 3381.0 / 64.0);
    cr.Tolerance = 0.1;
    cr.Antialias = Antialias.Default;
    cr.StrokePreserve();
    source8?.Dispose();
    cr.Operator = Operator.Over;
    Pattern source15 = new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
    cr.SetSource(source15);
    cr.NewPath();
    cr.MoveTo(3381.0 / 64.0, 5845.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 5845.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 7445.0 / 64.0);
    cr.LineTo(3381.0 / 64.0, 7445.0 / 64.0);
    cr.ClosePath();
    cr.MoveTo(3381.0 / 64.0, 5845.0 / 64.0);
    cr.Tolerance = 0.1;
    cr.Antialias = Antialias.Default;
    cr.FillRule = FillRule.Winding;
    cr.FillPreserve();
    source15?.Dispose();
    cr.Operator = Operator.Over;
    cr.LineWidth = 8.0;
    cr.MiterLimit = 10.0;
    cr.LineCap = LineCap.Butt;
    cr.LineJoin = LineJoin.Miter;
    Pattern source16 = new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
    cr.SetSource(source16);
    cr.NewPath();
    cr.MoveTo(3381.0 / 64.0, 5845.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 5845.0 / 64.0);
    cr.LineTo(4981.0 / 64.0, 7445.0 / 64.0);
    cr.LineTo(3381.0 / 64.0, 7445.0 / 64.0);
    cr.ClosePath();
    cr.MoveTo(3381.0 / 64.0, 5845.0 / 64.0);
    cr.Tolerance = 0.1;
    cr.Antialias = Antialias.Default;
    cr.StrokePreserve();
    source16?.Dispose();
    cr.Operator = Operator.Over;
    cr.Restore();
  }
  
  public static void DrawTool1x1(
    Context cr,
    int x,
    int y,
    float width,
    float height,
    double[] rgba)
  {
    Matrix matrix = cr.Matrix;
    cr.Save();
    float num1 = 129f;
    float num2 = 129f;
    float num3 = Math.Min(width / num1, height / num2);
    matrix.Translate(x + (double) Math.Max(0.0f, (float) ((width - num1 * (double) num3) / 2.0)), y + (double) Math.Max(0.0f, (float) ((height - num2 * (double) num3) / 2.0)));
    matrix.Scale(num3, num3);
    cr.Matrix = matrix;
    cr.Operator = Operator.Over;
    Pattern source1 = new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
    cr.SetSource(source1);
    cr.NewPath();
    cr.MoveTo(3317.0 / 64.0, 3317.0 / 64.0);
    cr.LineTo(4917.0 / 64.0, 3317.0 / 64.0);
    cr.LineTo(4917.0 / 64.0, 4917.0 / 64.0);
    cr.LineTo(3317.0 / 64.0, 4917.0 / 64.0);
    cr.ClosePath();
    cr.MoveTo(3317.0 / 64.0, 3317.0 / 64.0);
    cr.Tolerance = 0.1;
    cr.Antialias = Antialias.Default;
    cr.FillRule = FillRule.Winding;
    cr.FillPreserve();
    source1?.Dispose();
    cr.Operator = Operator.Over;
    cr.LineWidth = 8.0;
    cr.MiterLimit = 10.0;
    cr.LineCap = LineCap.Butt;
    cr.LineJoin = LineJoin.Miter;
    Pattern source2 = new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
    cr.SetSource(source2);
    cr.NewPath();
    cr.MoveTo(3317.0 / 64.0, 3317.0 / 64.0);
    cr.LineTo(4917.0 / 64.0, 3317.0 / 64.0);
    cr.LineTo(4917.0 / 64.0, 4917.0 / 64.0);
    cr.LineTo(3317.0 / 64.0, 4917.0 / 64.0);
    cr.ClosePath();
    cr.MoveTo(3317.0 / 64.0, 3317.0 / 64.0);
    cr.Tolerance = 0.1;
    cr.Antialias = Antialias.Default;
    cr.StrokePreserve();
    source2?.Dispose();
    cr.Restore();
  }
}

