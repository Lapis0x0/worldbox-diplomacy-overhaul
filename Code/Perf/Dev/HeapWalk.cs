using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace DiplomacyOverhaul.Perf
{
    // 开发用：从 World.world 和游戏程序集的所有静态字段出发，用反射走遍能到达的托管对象，
    // 按类型统计个数和估算的浅层大小（对象头 + 字段，不含引用的对象）。很慢，只在暂停时用于诊断
    public static class HeapWalk
    {
        class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }

        class TypeInfo
        {
            public FieldInfo[] refs;      // 需要继续走的字段（引用类型或含引用的结构体）
            public long shallow;          // 实例的浅层大小
            public bool array_walk;       // 数组元素需要继续走
            public int elem_size;
        }

        class Stat
        {
            public long count;
            public long bytes;
        }

        static readonly Dictionary<Type, TypeInfo> _infos = new Dictionary<Type, TypeInfo>();
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Run(string pLabel, int pTop)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var visited = new HashSet<object>(new RefEq());
            var stack = new Stack<object>();
            var stats = new Dictionary<Type, Stat>();
            var roots = new Dictionary<string, long>();

            void Push(object o)
            {
                if (o == null || o is Type) return;
                if (visited.Add(o)) stack.Push(o);
            }

            // 根分组：按顺序走，每组只统计前面各组没走到的新对象
            var groups = new List<(string name, List<object> roots)>();
            groups.Add(("world", new List<object> { World.world }));
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = asm.GetName().Name;
                if (an.StartsWith("System") || an == "mscorlib" || an.StartsWith("Mono.") || an.StartsWith("Unity.") || an.StartsWith("UnityEngine")) continue;
                bool per_field = asm == typeof(MapBox).Assembly;
                var list = new List<object>();
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(x => x != null).ToArray(); }
                catch { continue; }
                foreach (Type type in types)
                {
                    if (type.IsGenericTypeDefinition) continue;
                    FieldInfo[] statics;
                    try { statics = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                    catch { continue; }
                    foreach (FieldInfo f in statics)
                    {
                        if (f.FieldType.IsPrimitive || f.IsLiteral) continue;
                        object v;
                        try { v = f.GetValue(null); }
                        catch { continue; }
                        if (per_field) groups.Add(("static " + type.Name + "." + f.Name, new List<object> { v }));
                        else list.Add(v);
                    }
                }
                if (!per_field) groups.Add(("statics:" + an, list));
            }
            groups.Add(("unity_objects", UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Cast<object>().ToList()));
            var by_group = new List<(string, long, long)>();

            long total = 0;
            foreach (var (gname, groots) in groups)
            {
            long g_before = total;
            long g_count = visited.Count;
            foreach (object r in groots) Push(r);
            while (stack.Count > 0)
            {
                object o = stack.Pop();
                Type t = o.GetType();
                TypeInfo info = Info(t);
                long size;
                if (o is Array arr)
                {
                    size = 32 + (long)arr.Length * info.elem_size;
                    if (info.array_walk)
                    {
                        if (o is object[] objs)
                        {
                            for (int i = 0; i < objs.Length; i++) Push(objs[i]);
                        }
                        else
                        {
                            // 含引用的结构体数组（如 Dictionary 的 Entry[]）
                            TypeInfo ei = Info(t.GetElementType());
                            foreach (object e in arr)
                                if (e != null) WalkStruct(e, ei, Push);
                        }
                    }
                }
                else if (o is string s)
                {
                    size = 20 + 2L * s.Length;
                }
                else
                {
                    size = info.shallow;
                    foreach (FieldInfo f in info.refs)
                    {
                        object v;
                        try { v = f.GetValue(o); }
                        catch { continue; }
                        if (v == null) continue;
                        if (f.FieldType.IsValueType) WalkStruct(v, Info(f.FieldType), Push);
                        else Push(v);
                    }
                }
                if (!stats.TryGetValue(t, out Stat st)) stats[t] = st = new Stat();
                st.count++;
                st.bytes += size;
                total += size;
            }
            by_group.Add((gname, total - g_before, visited.Count - g_count));
            }

            var sb = new StringBuilder();
            sb.AppendLine($"heap walk {pLabel}: {visited.Count} objects, ~{total / 1048576}MB shallow, {sw.ElapsedMilliseconds}ms");
            foreach (var (gname, gbytes, gcount) in by_group.OrderByDescending(x => x.Item2).Take(25))
                if (gbytes > 1048576) sb.AppendLine($"  root {gname}: {gbytes / 1048576.0:F1}MB {gcount} objects");
            foreach (var kv in stats.OrderByDescending(kv => kv.Value.bytes))
                sb.AppendLine($"{kv.Value.bytes / 1048576.0,9:F1}MB {kv.Value.count,10}  {Name(kv.Key)}");
            string path = Path.Combine(Dev.Dir, $"heap_{pLabel}.txt");
            File.WriteAllText(path, sb.ToString());
            string[] lines = sb.ToString().Split('\n');
            for (int i = 0; i < Math.Min(lines.Length, pTop + 1); i++) Dev.Log("  " + lines[i]);
            Dev.Log("  full list: " + path);
        }

        static void WalkStruct(object pBoxed, TypeInfo pInfo, Action<object> pPush)
        {
            foreach (FieldInfo f in pInfo.refs)
            {
                object v;
                try { v = f.GetValue(pBoxed); }
                catch { continue; }
                if (v == null) continue;
                if (f.FieldType.IsValueType) WalkStruct(v, Info(f.FieldType), pPush);
                else pPush(v);
            }
        }

        static TypeInfo Info(Type t)
        {
            if (_infos.TryGetValue(t, out TypeInfo info)) return info;
            info = new TypeInfo();
            _infos[t] = info;
            if (t.IsArray)
            {
                Type e = t.GetElementType();
                info.elem_size = e.IsValueType ? ValueSize(e) : 8;
                info.array_walk = !e.IsPrimitive && !e.IsEnum && (!e.IsValueType || HasRefs(e));
                info.refs = Array.Empty<FieldInfo>();
                return info;
            }
            var refs = new List<FieldInfo>();
            long size = 16;
            for (Type c = t; c != null; c = c.BaseType)
            {
                foreach (FieldInfo f in c.GetFields(Inst | BindingFlags.DeclaredOnly))
                {
                    Type ft = f.FieldType;
                    size += ft.IsValueType ? ValueSize(ft) : 8;
                    if (ft.IsPointer || ft.IsPrimitive || ft.IsEnum) continue;
                    if (ft.IsValueType && !HasRefs(ft)) continue;
                    refs.Add(f);
                }
            }
            info.shallow = (size + 7) / 8 * 8;
            info.refs = refs.ToArray();
            return info;
        }

        static readonly Dictionary<Type, bool> _has_refs = new Dictionary<Type, bool>();

        static bool HasRefs(Type t)
        {
            if (_has_refs.TryGetValue(t, out bool r)) return r;
            _has_refs[t] = false;
            r = false;
            foreach (FieldInfo f in t.GetFields(Inst))
            {
                Type ft = f.FieldType;
                if (ft.IsPointer || ft.IsPrimitive || ft.IsEnum) continue;
                if (!ft.IsValueType || HasRefs(ft)) { r = true; break; }
            }
            _has_refs[t] = r;
            return r;
        }

        static readonly Dictionary<Type, int> _sizes = new Dictionary<Type, int>();

        static int ValueSize(Type t)
        {
            if (_sizes.TryGetValue(t, out int s)) return s;
            if (t.IsEnum) t = Enum.GetUnderlyingType(t);
            if (t.IsPrimitive) s = t == typeof(bool) || t == typeof(byte) || t == typeof(sbyte) ? 1 : t == typeof(short) || t == typeof(ushort) || t == typeof(char) ? 2 : t == typeof(int) || t == typeof(uint) || t == typeof(float) ? 4 : 8;
            else
            {
                s = 0;
                foreach (FieldInfo f in t.GetFields(Inst)) s += f.FieldType.IsValueType ? ValueSize(f.FieldType) : 8;
                if (s == 0) s = 1;
            }
            _sizes[t] = s;
            return s;
        }

        static string Name(Type t)
        {
            if (!t.IsGenericType) return t.FullName ?? t.Name;
            string args = string.Join(",", t.GetGenericArguments().Select(Name));
            string n = t.Name;
            int tick = n.IndexOf('`');
            if (tick >= 0) n = n.Substring(0, tick);
            string outer = t.DeclaringType != null ? Name(t.DeclaringType) + "+" : "";
            return outer + n + "<" + args + ">";
        }
    }
}
