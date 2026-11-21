using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WheelFFB
{
    // \ (обратный слэш) — записать pedal_refs_<машина>.txt:
    // 1) все предки каждой педали (с компонентами и FSM),
    // 2) все FSM и скрипты в игре, которые ссылаются на педаль или её предков.
    public static class PedalRefDump
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static void Tick()
        {
            if (Input.GetKeyDown(KeyCode.Backslash)) Run();
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
            Transform root = FfbDriver.Current != null ? FfbDriver.Current.transform.root : null;
            if (root == null) { GameObject k = GameObject.Find("KEKMET(350-400psi)"); if (k != null) root = k.transform; }
            if (root == null) { sb.AppendLine("vehicle not found"); Write(sb, "none"); return; }
            sb.AppendLine("vehicle: " + root.name);

            // Педали и их предки (до корня, не включая его).
            var targets = new HashSet<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.ToLowerInvariant().IndexOf("pedal", StringComparison.Ordinal) < 0) continue;
                sb.AppendLine("\n##### PEDAL " + PathOf(t));
                for (Transform p = t; p != null && p != root; p = p.parent)
                {
                    targets.Add(p.gameObject);
                    sb.AppendLine("  -- " + p.name + "  localRot=" + p.localEulerAngles + "  localPos=" + p.localPosition);
                    foreach (Component c in p.GetComponents<Component>())
                    {
                        if (c == null) continue;
                        sb.AppendLine("     [" + c.GetType().Name + "]");
                        if (c is PlayMakerFSM) DumpFsm((PlayMakerFSM)c, sb);
                        else if (c is Animation)
                            foreach (AnimationState s in (Animation)c) sb.AppendLine("        clip " + s.name + " len=" + s.length);
                    }
                }
            }

            // Кто ещё на них ссылается.
            sb.AppendLine("\n##### FSM REFERENCES");
            foreach (PlayMakerFSM f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
            {
                if (f == null || targets.Contains(f.gameObject)) continue;
                if (!FsmRefers(f, targets)) continue;
                sb.AppendLine("=== " + PathOf(f.transform));
                DumpFsm(f, sb);
            }

            sb.AppendLine("\n##### SCRIPT REFERENCES");
            foreach (MonoBehaviour m in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (m == null || m is PlayMakerFSM) continue;
                for (Type t = m.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    foreach (FieldInfo fi in t.GetFields(All | BindingFlags.DeclaredOnly))
                    {
                        object v; try { v = fi.GetValue(m); } catch { continue; }
                        GameObject g = v as GameObject;
                        if (g == null && v is Component) g = ((Component)v).gameObject;
                        if (g != null && targets.Contains(g))
                            sb.AppendLine(PathOf(m.transform) + " [" + m.GetType().Name + "]." + fi.Name + " -> " + g.name);
                    }
            }

            Write(sb, root.name);
        }

        static bool FsmRefers(PlayMakerFSM f, HashSet<GameObject> targets)
        {
            try
            {
                foreach (FsmGameObject g in f.FsmVariables.GameObjectVariables)
                    if (g.Value != null && targets.Contains(g.Value)) return true;
                foreach (FsmState st in f.FsmStates)
                {
                    if (st.Actions == null) continue;
                    foreach (FsmStateAction a in st.Actions)
                    {
                        if (a == null) continue;
                        foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                        {
                            object v; try { v = fi.GetValue(a); } catch { continue; }
                            GameObject g = null;
                            FsmOwnerDefault od = v as FsmOwnerDefault;
                            if (od != null) g = od.OwnerOption == OwnerDefaultOption.UseOwner ? f.gameObject : (od.GameObject != null ? od.GameObject.Value : null);
                            FsmGameObject fg = v as FsmGameObject;
                            if (fg != null) g = fg.Value;
                            if (g != null && targets.Contains(g)) return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        static void DumpFsm(PlayMakerFSM f, StringBuilder sb)
        {
            sb.AppendLine("        FSM '" + f.FsmName + "' enabled=" + f.enabled + " state=" + f.ActiveStateName);
            try
            {
                foreach (FsmFloat v in f.FsmVariables.FloatVariables) sb.AppendLine("        float " + v.Name + " = " + v.Value);
                foreach (FsmState st in f.FsmStates)
                {
                    sb.AppendLine("        STATE " + st.Name);
                    if (st.Actions != null)
                        foreach (FsmStateAction a in st.Actions)
                            if (a != null) sb.AppendLine("          " + ActionText(a, f));
                    foreach (FsmTransition tr in st.Transitions)
                        sb.AppendLine("            -> " + tr.EventName + " => " + tr.ToState);
                }
            }
            catch (Exception e) { sb.AppendLine("        ERR " + e.Message); }
        }

        static string ActionText(FsmStateAction a, PlayMakerFSM f)
        {
            var s = new StringBuilder(a.GetType().Name);
            foreach (FieldInfo fi in a.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                object v; try { v = fi.GetValue(a); } catch { continue; }
                s.Append(" | " + fi.Name + "=" + Show(v, f));
            }
            return s.ToString();
        }

        static string Show(object v, PlayMakerFSM f)
        {
            if (v == null) return "null";
            FsmOwnerDefault od = v as FsmOwnerDefault;
            if (od != null)
                return od.OwnerOption == OwnerDefaultOption.UseOwner ? "Owner(" + f.gameObject.name + ")"
                     : (od.GameObject != null && od.GameObject.Value != null ? od.GameObject.Value.name : "null");
            NamedVariable nv = v as NamedVariable;
            if (nv != null)
            {
                object o = null;
                try { PropertyInfo p = nv.GetType().GetProperty("Value"); o = p != null ? p.GetValue(nv, null) : null; } catch { }
                UnityEngine.Object uo = o as UnityEngine.Object;
                return (string.IsNullOrEmpty(nv.Name) ? "" : "{" + nv.Name + "}") + (uo != null ? uo.name : (o != null ? o.ToString() : "null"));
            }
            // FsmProperty: объект и имя свойства
            FieldInfo to = v.GetType().GetField("TargetObject"), pn = v.GetType().GetField("PropertyName");
            if (to != null && pn != null)
            {
                FsmObject fo = to.GetValue(v) as FsmObject;
                return "prop(" + (fo != null && fo.Value != null ? fo.Value.GetType().Name : "?") + "." + pn.GetValue(v) + ")";
            }
            return v.ToString();
        }

        static void Write(StringBuilder sb, string name)
        {
            foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            File.WriteAllText(Path.Combine(Application.dataPath, "../pedal_refs_" + name + ".txt"), sb.ToString());
        }
    }
}
