using System;
using System.Linq;
using System.Reflection;
class P {
  static void Main() {
    AppDomain.CurrentDomain.AssemblyResolve += (s,e) => {
      var name = new AssemblyName(e.Name).Name + ".dll";
      foreach (var dir in new[]{@"E:\Vintagestory", @"E:\Vintagestory\Lib"}) {
        var p = System.IO.Path.Combine(dir, name);
        if (System.IO.File.Exists(p)) return Assembly.LoadFrom(p);
      }
      return null;
    };
    var t = Assembly.LoadFrom(@"E:\Vintagestory\VintagestoryAPI.dll").GetType("Vintagestory.API.Common.IBlockAccessor");
    foreach (var m in t.GetMethods().Where(x => x.Name.Contains("Neighbour") || x.Name.Contains("Neighbor") || x.Name.Contains("Trigger")))
      Console.WriteLine(m);
  }
}
