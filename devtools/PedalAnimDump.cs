using System;
using System.IO;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WheelFFB
{
    // / (слэш, русская точка) — записать pedal_anim.txt: всё в CORRIS, что похоже на педали,
    // со всеми компонентами, FSM, состояниями, действиями и их полями.
    public static class PedalAnimDump
    {
        static readonly string[] Keys = { "pedal", "throttle", "brake", "clutch", "gas" };
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Tick()
        {
            if (Input.GetKeyDown(KeyCode.Slash)) Run();
        }

        static bool Match(string s)
        {
            string n = s.ToLowerInvariant();
            foreach (string k in Keys) if (n.Contains(k)) return true;
            return false;
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        public static void Run()
        {
            var sb = new StringBuilder();
            // Машина, в которой сидит игрок; если не в машине — CORRIS.
            GameObject car = FfbDriver.Current != null ? FfbDriver.Current.transform.root.gameObject : GameObject.Find("CORRIS");
            if (car == null) { sb.AppendLine("vehicle not found"); Write(sb); return; }
            sb.AppendLine("vehicle: " + car.name);

            foreach (Transform t in car.GetComponentsInChildren<Transform>(true))
            {
                bool nameHit = Match(t.name);
                PlayMakerFSM[] fsms = t.GetComponents<PlayMakerFSM>();
                bool fsmHit = false;
                foreach (PlayMakerFSM f in fsms) if (FsmMentions(f)) fsmHit = true;
                if (!nameHit && !fsmHit) continue;

                sb.AppendLine("=== " + PathOf(t) + "  active=" + t.gameObject.activeInHierarchy);
                sb.AppendLine("    localRot=" + t.localEulerAngles + "  localPos=" + t.localPosition);
                foreach (Component c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    sb.AppendLine("  [" + c.GetType().Name + "]");
                    if (c is PlayMakerFSM) DumpFsm((PlayMakerFSM)c, sb);
                    else if (!(c is Transform) && !(c is Renderer) && !(c is MeshFilter) && !(c is Collider))
                        DumpFields(c, sb, "      ");
                }
            }
            Write(sb);
        }

        static bool FsmMentions(PlayMakerFSM f)
        {
            try
            {
                if (Match(f.FsmName)) return true;
                foreach (FsmState st in f.FsmStates)
                {
                    if (st.Actions == null) continue;
                    foreach (FsmStateAction a in st.Actions)
                        if (a != null && ActionText(a).ToLowerInvariant().Contains("throttle")) return true;
                }
            }
            catch { }
            return false;
        }

        static void DumpFsm(PlayMakerFSM f, StringBuilder sb)
        {
            sb.AppendLine("      FSM '" + f.FsmName + "' enabled=" + f.enabled + " state=" + f.ActiveStateName);
            try
            {
                foreach (FsmFloat v in f.FsmVariables.FloatVariables) sb.AppendLine("      float " + v.Name + " = " + v.Value);
                foreach (FsmString v in f.FsmVariables.StringVariables) sb.AppendLine("      string " + v.Name + " = " + v.Value);
                foreach (FsmState st in f.FsmStates)
                {
                    sb.AppendLine("      STATE " + st.Name);
                    if (st.Actions != null)
                        foreach (FsmStateAction a in st.Actions)
                            if (a != null) sb.AppendLine("        " + ActionText(a));
                    foreach (FsmTransition tr in st.Transitions)
                        sb.AppendLine("          -> " + tr.EventName + " => " + tr.ToState);
                }
            }
            catch (Exception e) { sb.AppendLine("      ERR " + e.Message); }
        }

        static string ActionText(FsmStateAction a)
        {
            var s = new StringBuilder(a.GetType().Name);
            foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                object v;
                try { v = fi.GetValue(a); } catch { continue; }
                s.Append(" | " + fi.Name + "=" + Show(v));
            }
            return s.ToString();
        }

        static string Show(object v)
        {
            if (v == null) return "null";
            NamedVariable nv = v as NamedVariable;
            if (nv != null) return (string.IsNullOrEmpty(nv.Name) ? "" : "{" + nv.Name + "}") + Val(nv);
            FsmOwnerDefault od = v as FsmOwnerDefault;
            if (od != null) return od.OwnerOption.ToString() + (od.GameObject != null && od.GameObject.Value != null ? ":" + od.GameObject.Value.name : "");
            return v.ToString();
        }

        static string Val(NamedVariable nv)
        {
            try
            {
                PropertyInfo p = nv.GetType().GetProperty("Value");
                object o = p != null ? p.GetValue(nv, null) : null;
                UnityEngine.Object uo = o as UnityEngine.Object;
                if (uo != null) return uo.name;
                return o != null ? o.ToString() : "null";
            }
            catch { return "?"; }
        }

        static void DumpFields(Component c, StringBuilder sb, string ind)
        {
            for (Type t = c.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour) && t != typeof(Component); t = t.BaseType)
                foreach (FieldInfo fi in t.GetFields(All | BindingFlags.DeclaredOnly))
                {
                    object v;
                    try { v = fi.GetValue(c); } catch { continue; }
                    UnityEngine.Object uo = v as UnityEngine.Object;
                    string s = uo != null ? uo.name : (v != null ? v.ToString() : "null");
                    sb.AppendLine(ind + t.Name + "." + fi.Name + " = " + s);
                }
        }

        static void Write(StringBuilder sb)
        {
            string name = FfbDriver.Current != null ? FfbDriver.Current.transform.root.name : "CORRIS";
            foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            File.WriteAllText(Path.Combine(Application.dataPath, "../pedal_anim_" + name + ".txt"), sb.ToString());
        }
    }
}
