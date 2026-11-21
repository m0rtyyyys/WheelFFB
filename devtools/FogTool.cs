using System.Collections.Generic;
using System.IO;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WheelFFB
{
    // ; — найти переменные запотевания и записать в fog_dump.txt (папка игры).
    // ' — вкл/выкл режим «без запотевания»: найденные переменные держатся на 0.
    public static class FogTool
    {
        static readonly string[] Keys = { "fog", "mist", "condens", "humid", "moist", "steam", "frost", "ice" };
        static readonly List<FsmFloat> targets = new List<FsmFloat>();
        public static bool NoFog;

        static bool Match(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            foreach (string k in Keys) if (n.Contains(k)) return true;
            return false;
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        public static void Scan()
        {
            targets.Clear();
            var sb = new StringBuilder();

            sb.AppendLine("== GLOBAL ==");
            foreach (FsmFloat v in FsmVariables.GlobalVariables.FloatVariables)
                if (Match(v.Name)) { targets.Add(v); sb.AppendLine("float " + v.Name + " = " + v.Value); }

            sb.AppendLine("== FSM ==");
            foreach (PlayMakerFSM fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
            {
                if (fsm == null || fsm.gameObject == null) continue;
                bool fsmHit = Match(fsm.FsmName) || Match(fsm.gameObject.name);
                string head = PathOf(fsm.transform) + " [" + fsm.FsmName + "] state=" + fsm.ActiveStateName;
                bool printed = false;

                foreach (FsmFloat v in fsm.FsmVariables.FloatVariables)
                {
                    if (!Match(v.Name) && !fsmHit) continue;
                    if (!printed) { sb.AppendLine(head); printed = true; }
                    sb.AppendLine("    float " + v.Name + " = " + v.Value);
                    if (Match(v.Name)) targets.Add(v);
                }
                if (fsmHit && !printed) sb.AppendLine(head);
            }

            sb.AppendLine("== RENDERERS ==");
            foreach (Renderer r in Resources.FindObjectsOfTypeAll<Renderer>())
                if (r != null && Match(r.name))
                    sb.AppendLine(PathOf(r.transform) + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.name : "-")
                                  + " enabled=" + r.enabled);

            sb.AppendLine("targets: " + targets.Count);
            File.WriteAllText(Path.Combine(Application.dataPath, "../fog_dump.txt"), sb.ToString());
        }

        public static void Tick()
        {
            if (Input.GetKeyDown(KeyCode.Semicolon)) Scan();
            if (Input.GetKeyDown(KeyCode.Quote))
            {
                NoFog = !NoFog;
                if (NoFog && targets.Count == 0) Scan();
            }
            if (!NoFog) return;
            foreach (FsmFloat v in targets) if (v != null) v.Value = 0f;
        }
    }
}
