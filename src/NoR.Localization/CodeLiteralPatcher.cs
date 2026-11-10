using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace NoR.Localization
{
    /// <summary>Layer 1: replaces Japanese string literals inside game methods with their English translation.</summary>
    internal static class CodeLiteralPatcher
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static int PatchAll(Harmony harmony, string listPath)
        {
            if (!File.Exists(listPath)) { Plugin.Log.LogWarning("code_methods.txt not found"); return 0; }
            var asm = typeof(game_fragmng).Assembly;
            var transpiler = new HarmonyMethod(typeof(CodeLiteralPatcher).GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic));
            int ok = 0, failed = 0;

            foreach (var raw in File.ReadAllLines(listPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                MethodBase m = null;
                try
                {
                    m = Resolve(asm, line);
                    if (m == null) { failed++; Plugin.Log.LogDebug("Method not found: " + line); continue; }
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    harmony.Patch(m, transpiler: transpiler);
                    ok++;
                }
                catch (Exception e)
                {
                    failed++;
                    Plugin.Log.LogWarning("Could not patch " + line + ": " + e.Message);
                }
            }
            if (failed > 0) Plugin.Log.LogWarning("Code literal patching: " + failed + " methods failed or not found");
            return ok;
        }

        // Line format: Namespace.Type/Nested::Name(ParamTypeName,ParamTypeName)
        private static MethodBase Resolve(Assembly asm, string line)
        {
            int sep = line.IndexOf("::", StringComparison.Ordinal);
            string typeName = line.Substring(0, sep).Replace('/', '+');
            string rest = line.Substring(sep + 2);
            int paren = rest.IndexOf('(');
            string name = rest.Substring(0, paren);
            string paramList = rest.Substring(paren + 1, rest.Length - paren - 2);
            string[] ps = paramList.Length == 0 ? new string[0] : paramList.Split(',');

            var type = asm.GetType(typeName, false);
            if (type == null) return null;

            IEnumerable<MethodBase> candidates;
            if (name == ".ctor") candidates = type.GetConstructors(All);
            else if (name == ".cctor") candidates = type.GetConstructors(BindingFlags.Static | BindingFlags.NonPublic);
            else candidates = type.GetMethods(All);

            foreach (var c in candidates)
            {
                if (name != ".ctor" && name != ".cctor" && c.Name != name) continue;
                var pars = c.GetParameters();
                if (pars.Length != ps.Length) continue;
                bool match = true;
                for (int i = 0; i < ps.Length && match; i++)
                    match = CecilName(pars[i].ParameterType) == ps[i];
                if (match) return c;
            }
            return null;
        }

        // Mono.Cecil's TypeReference.Name: "Int32", "List`1", "Single[]", "String&" for by-ref.
        private static string CecilName(Type t)
        {
            if (t.IsByRef) return CecilName(t.GetElementType()) + "&";
            if (t.IsArray) return CecilName(t.GetElementType()) + "[]";
            return t.Name;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ins in instructions)
            {
                if (ins.opcode == OpCodes.Ldstr && ins.operand is string)
                    ins.operand = Plugin.Table.Translate((string)ins.operand);
                yield return ins;
            }
        }
    }
}
