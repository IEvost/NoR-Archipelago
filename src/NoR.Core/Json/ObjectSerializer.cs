using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NoR.Core.Json
{
    /// <summary>
    /// Reflection-based serializer that mirrors Unity's serialization rules
    /// (public fields + [SerializeField] private fields, no [NonSerialized]).
    /// Used to dump MonoBehaviour configuration for research.
    /// </summary>
    public static class ObjectSerializer
    {
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();

        public static int MaxDepth = 4;
        public static int MaxListItems = 512;

        public static void WriteFields(JsonWriter w, object obj, int depth = 0)
        {
            w.BeginObject();
            foreach (var f in GetSerializedFields(obj.GetType()))
            {
                object v;
                try { v = f.GetValue(obj); }
                catch (Exception e) { w.Prop(f.Name, "<error: " + e.GetType().Name + ">"); continue; }
                w.Name(f.Name);
                WriteValue(w, v, depth + 1);
            }
            w.EndObject();
        }

        public static FieldInfo[] GetSerializedFields(Type type)
        {
            FieldInfo[] result;
            if (FieldCache.TryGetValue(type, out result)) return result;

            var list = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour) && t != typeof(Component) && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(InstanceFields))
                {
                    if (f.IsNotSerialized || f.IsInitOnly || f.IsLiteral) continue;
                    if (f.IsPublic || f.GetCustomAttributes(typeof(SerializeField), true).Length > 0)
                        list.Add(f);
                }
            }
            result = list.ToArray();
            FieldCache[type] = result;
            return result;
        }

        public static void WriteValue(JsonWriter w, object v, int depth)
        {
            if (v == null) { w.Null(); return; }

            // Unity "fake null" for destroyed objects.
            var uo = v as UnityEngine.Object;
            if (uo != null || v is UnityEngine.Object)
            {
                WriteUnityRef(w, uo);
                return;
            }

            var t = v.GetType();
            if (v is string) { w.Value((string)v); return; }
            if (v is bool) { w.Value((bool)v); return; }
            if (t.IsEnum) { w.Value(v.ToString()); return; }
            if (v is int || v is long || v is short || v is byte || v is sbyte || v is ushort || v is uint) { w.Value(Convert.ToInt64(v)); return; }
            if (v is float || v is double || v is decimal) { w.Value(Convert.ToDouble(v)); return; }
            if (v is char) { w.Value(v.ToString()); return; }

            if (v is Vector2) { var x = (Vector2)v; w.BeginObject().Prop("x", x.x).Prop("y", x.y).EndObject(); return; }
            if (v is Vector3) { var x = (Vector3)v; w.BeginObject().Prop("x", x.x).Prop("y", x.y).Prop("z", x.z).EndObject(); return; }
            if (v is Vector4) { var x = (Vector4)v; w.BeginObject().Prop("x", x.x).Prop("y", x.y).Prop("z", x.z).Prop("w", x.w).EndObject(); return; }
            if (v is Quaternion) { var x = (Quaternion)v; w.BeginObject().Prop("x", x.x).Prop("y", x.y).Prop("z", x.z).Prop("w", x.w).EndObject(); return; }
            if (v is Color) { var x = (Color)v; w.BeginObject().Prop("r", x.r).Prop("g", x.g).Prop("b", x.b).Prop("a", x.a).EndObject(); return; }
            if (v is Rect) { var x = (Rect)v; w.BeginObject().Prop("x", x.x).Prop("y", x.y).Prop("w", x.width).Prop("h", x.height).EndObject(); return; }

            if (depth > MaxDepth) { w.Value("<max depth>"); return; }

            var arr = v as Array;
            if (arr != null && arr.Rank > 1)
            {
                WriteMultiDim(w, arr, depth);
                return;
            }

            var list = v as IList;
            if (list != null)
            {
                w.BeginArray();
                int n = Math.Min(list.Count, MaxListItems);
                for (int i = 0; i < n; i++) WriteValue(w, list[i], depth + 1);
                if (list.Count > n) w.Value("<truncated " + (list.Count - n) + ">");
                w.EndArray();
                return;
            }

            if (t.IsSerializable || t.IsValueType)
            {
                WriteFields(w, v, depth);
                return;
            }

            w.Value("<" + t.FullName + ">");
        }

        private static void WriteMultiDim(JsonWriter w, Array arr, int depth)
        {
            // Rank-2 arrays are written as nested arrays (rows); higher ranks are flattened.
            if (arr.Rank == 2)
            {
                w.BeginArray();
                for (int i = 0; i < arr.GetLength(0); i++)
                {
                    w.BeginArray();
                    for (int j = 0; j < arr.GetLength(1); j++) WriteValue(w, arr.GetValue(i, j), depth + 1);
                    w.EndArray();
                }
                w.EndArray();
                return;
            }
            w.BeginArray();
            foreach (var x in arr) WriteValue(w, x, depth + 1);
            w.EndArray();
        }

        public static void WriteUnityRef(JsonWriter w, UnityEngine.Object o)
        {
            if (o == null) { w.Null(); return; }
            w.BeginObject();
            w.Prop("$type", o.GetType().Name);
            w.Prop("name", o.name);
            GameObject go = o as GameObject;
            if (go == null && o is Component) go = ((Component)o).gameObject;
            if (go != null)
            {
                bool inScene = go.scene.IsValid();
                w.Prop("inScene", inScene);
                if (inScene) w.Prop("path", Hierarchy.GetPath(go.transform));
            }
            w.EndObject();
        }
    }
}
