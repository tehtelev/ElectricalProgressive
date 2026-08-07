using System;
using System.Linq;
using System.Reflection;
class P {
  static void Main() {
    AppDomain.CurrentDomain.AssemblyResolve += (s,e) => {
      var name = new AssemblyName(e.Name).Name + ".dll";
      foreach (var dir in new[]{@"E:\Vintagestory", @"E:\Vintagestory\Lib", @"E:\Vintagestory\Mods", System.IO.Path.GetDirectoryName(@"E:\\Vintagestory\\VintagestoryAPI.dll")}) {
        var p = System.IO.Path.Combine(dir ?? "", name);
        if (System.IO.File.Exists(p)) return Assembly.LoadFrom(p);
      }
      return null;
    };
    var api = Assembly.LoadFrom(@"E:\\Vintagestory\\VintagestoryAPI.dll");
    var md = api.GetType("Vintagestory.API.Client.MeshData");
    Console.WriteLine("=== MeshData methods ===");
    foreach (var m in md.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).OrderBy(x=>x.Name)) {
      var ps = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
      Console.WriteLine(m.ReturnType.Name + " " + m.Name + "(" + ps + ")");
    }
    Console.WriteLine("=== Fields/Props of interest ===");
    foreach (var n in new[]{"CustomInts","TextureIndices","TextureIds","Uv","xyz","Indices","VerticesCount","IndicesCount","Rgba","Flags","XyzFaces","CustomFloats","CustomBytes"}) {
      var f = md.GetField(n, BindingFlags.Public|BindingFlags.Instance|BindingFlags.NonPublic);
      var p = md.GetProperty(n, BindingFlags.Public|BindingFlags.Instance|BindingFlags.NonPublic);
      if (f!=null) Console.WriteLine("F " + n + " : " + f.FieldType);
      if (p!=null) Console.WriteLine("P " + n + " : " + p.PropertyType);
    }
  }
}
