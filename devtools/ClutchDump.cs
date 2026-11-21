using System;
using System.IO;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WheelFFB
{
    // ' (апостроф, русская Э) занят FogTool, поэтому — клавиша Home.
    // Записывает clutch_fsm_<машина>.txt: все FSM текущей машины, которые
    // читают ось Clutch, шлют/ловят CLUTCHON/CLUTCHOFF, или называются Clutch/AutoClutch/TestClutch/Wear/Gearbox*.
    // Для каждого действия выводятся имена свойств (SetProperty/GetProperty) и адресаты событий.
    public static class ClutchDump
    {
        public static void Tick()
        {
            if (Input.GetKeyDown(KeyCode.Home)) Run();
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        static bool Interesting(PlayMakerFSM f)
        {
            string n = f.FsmName;
            if (n == "Clutch" || n == "AutoClutch" || n == "TestClutch" || n == "Wear" || n.StartsWith("Gear") || n == "Shift" || n == "Gearbox")
                return true;
            try
            {
                foreach (FsmEvent e in f.FsmEvents)
                    if (e != null && (e.Name == "CLUTCHON" || e.Name == "CLUTCHOFF")) return true;
                foreach (FsmState st in f.FsmStates)
                {
                    if (st.Actions == null) continue;
                    foreach (FsmStateAction a in st.Actions)
                        if (a != null && ActionText(a, f).IndexOf("Clutch", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch { }
            return false;
        }

        public static void Run()
        {
            CarController cc = FfbDriver.Current;
            Transform root = cc != null ? cc.transform.root : null;
            if (root == null) { GameObject g = GameObject.Find("CORRIS"); if (g != null) root = g.transform; }
            if (root == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("vehicle: " + root.name);
            Drivetrain dt = root.GetComponent<Drivetrain>();
            if (dt != null)
                sb.AppendLine("Drivetrain: autoClutch=" + dt.autoClutch + " gear=" + dt.gear + " rpm=" + dt.rpm.ToString("F0") +
                              " clutchPos=" + (dt.clutch != null ? dt.clutch.GetClutchPosition().ToString("F3") : "null") +
                              " clutch.maxTorque=" + (dt.clutch != null ? dt.clutch.maxTorque.ToString("F0") : "-") +
                              " shifter=" + dt.shifter + " automatic=" + dt.automatic + " shiftTime=" + dt.shiftTime);

            foreach (PlayMakerFSM f in root.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (f == null || !Interesting(f)) continue;
                sb.AppendLine("\n=== " + PathOf(f.transform) + "  FSM '" + f.FsmName + "' enabled=" + f.enabled + " state=" + f.ActiveStateName);
                try
                {
                    foreach (FsmFloat v in f.FsmVariables.FloatVariables) sb.AppendLine("  float " + v.Name + " = " + v.Value);
                    foreach (FsmBool v in f.FsmVariables.BoolVariables) sb.AppendLine("  bool " + v.Name + " = " + v.Value);
                    foreach (FsmInt v in f.FsmVariables.IntVariables) sb.AppendLine("  int " + v.Name + " = " + v.Value);
                    foreach (FsmString v in f.FsmVariables.StringVariables) sb.AppendLine("  string " + v.Name + " = " + v.Value);
                    foreach (FsmTransition tr in f.Fsm.GlobalTransitions) sb.AppendLine("  GLOBAL " + tr.EventName + " => " + tr.ToState);
                    foreach (FsmState st in f.FsmStates)
                    {
                        sb.AppendLine("  STATE " + st.Name);
                        if (st.Actions != null)
                            foreach (FsmStateAction a in st.Actions)
                                if (a != null) sb.AppendLine("    " + ActionText(a, f));
                        foreach (FsmTransition tr in st.Transitions)
                            sb.AppendLine("      -> " + tr.EventName + " => " + tr.ToState);
                    }
                }
                catch (Exception e) { sb.AppendLine("  ERR " + e.Message); }
            }

            string name = root.name;
            foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            File.WriteAllText(Path.Combine(Application.dataPath, "../clutch_fsm_" + name + ".txt"), sb.ToString());
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

            FsmEventTarget et = v as FsmEventTarget;
            if (et != null)
            {
                string go = et.gameObject != null
                    ? (et.gameObject.OwnerOption == OwnerDefaultOption.UseOwner ? "Owner" :
                       (et.gameObject.GameObject != null && et.gameObject.GameObject.Value != null ? et.gameObject.GameObject.Value.name : "null"))
                    : "-";
                string fsmName = et.fsmName != null ? et.fsmName.Value : "-";
                string comp = et.fsmComponent != null ? PathOf(et.fsmComponent.transform) + "[" + et.fsmComponent.FsmName + "]" : "-";
                return "target(" + et.target + " go=" + go + " fsm=" + fsmName + " comp=" + comp + " children=" + et.sendToChildren + ")";
            }

            FsmEvent ev = v as FsmEvent;
            if (ev != null) return "event:" + ev.Name;

            // FsmProperty: объект и имя свойства
            FieldInfo to = v.GetType().GetField("TargetObject"), pn = v.GetType().GetField("PropertyName");
            if (to != null && pn != null)
            {
                FsmObject fo = to.GetValue(v) as FsmObject;
                string tgt = fo != null && fo.Value != null ? fo.Value.GetType().Name + "(" + fo.Value.name + ")" : "?";
                string extra = "";
                foreach (string fld in new[] { "FloatParameter", "BoolParameter", "IntParameter", "StringParameter" })
                {
                    FieldInfo pf = v.GetType().GetField(fld);
                    NamedVariable pv = pf != null ? pf.GetValue(v) as NamedVariable : null;
                    if (pv != null && (!string.IsNullOrEmpty(pv.Name) || pf.Name == "FloatParameter" || pf.Name == "BoolParameter"))
                    {
                        PropertyInfo vp = pv.GetType().GetProperty("Value");
                        extra += " " + fld + "=" + (string.IsNullOrEmpty(pv.Name) ? "" : "{" + pv.Name + "}") + (vp != null ? vp.GetValue(pv, null) : "?");
                    }
                }
                return "prop(" + tgt + "." + pn.GetValue(v) + extra + ")";
            }

            NamedVariable nv = v as NamedVariable;
            if (nv != null)
            {
                object o = null;
                try { PropertyInfo p = nv.GetType().GetProperty("Value"); o = p != null ? p.GetValue(nv, null) : null; } catch { }
                UnityEngine.Object uo = o as UnityEngine.Object;
                return (string.IsNullOrEmpty(nv.Name) ? "" : "{" + nv.Name + "}") + (uo != null ? uo.name : (o != null ? o.ToString() : "null"));
            }
            return v.ToString();
        }
    }
}
