using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
class P {
  static void Main() {
    AppDomain.CurrentDomain.AssemblyResolve += (s,e) => {
      var name = new AssemblyName(e.Name).Name + ".dll";
      foreach (var dir in new[]{@"E:\Vintagestory", @"E:\Vintagestory\Lib", @"E:\Vintagestory\Mods"}) {
        var p = System.IO.Path.Combine(dir, name);
        if (System.IO.File.Exists(p)) return Assembly.LoadFrom(p);
      }
      return null;
    };
    var api = Assembly.LoadFrom(@"E:\Vintagestory\VintagestoryAPI.dll");
    var md = api.GetType("Vintagestory.API.Client.MeshData");
    foreach (var methodName in new[]{"getTextureIndex","AddTextureId","SplitByTextureId"}) {
      var m = md.GetMethod(methodName);
      var il = m.GetMethodBody().GetILAsByteArray();
      var module = md.Module;
      Console.WriteLine("==== " + methodName);
      for (int i=0;i<il.Length && i<120;) {
        byte op = il[i];
        string s = i.ToString("D3") + " " + op.ToString("X2");
        if ((op==0x28||op==0x6F||op==0x7B||op==0x74||op==0x6C||op==0x7E) && i+4<il.Length) {
          int token = BitConverter.ToInt32(il, i+1);
          try { s += " " + module.ResolveMember(token); } catch { s += " t"; }
          Console.WriteLine(s); i+=5; continue;
        }
        Console.WriteLine(s); i++;
      }
    }
  }
}
