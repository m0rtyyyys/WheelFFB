using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WheelFFB
{
    // PageUp — записать scene_search.txt: все объекты сцены (включая выключенные), в имени которых
    // или в имени FSM которых есть слова из списка. Для каждого — путь, активность, компоненты,
    // FSM (имя, текущее состояние, список состояний, глобальные события).
    // Слова можно поменять в файле scene_search_words.txt в папке игры (по одному в строке).
    public static class SceneSearch
    {
        static readonly string[] DefaultWords =
        {
            "petamo", "cigar", "tupak", "smoke", "savuk",
            "phone", "puhelin", "posti", "post", "paketti", "package", "parcel", "order", "tilaus",
            "teimo", "shop", "kauppa"
        };

        public static void Tick()
        {
            if (Input.GetKeyDown(KeyCode.PageUp)) Run();
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }

        static string[] Words()
        {
            string path = Path.Combine(Application.dataPath, "../scene_search_words.txt");
            try
            {
                if (!File.Exists(path)) File.WriteAllText(path, string.Join("\n", DefaultWords) + "\n");
                var list = new List<string>();
                foreach (string l in File.ReadAllLines(path)) { string w = l.Trim().ToLowerInvariant(); if (w.Length > 0 && !w.StartsWith("#")) list.Add(w); }
                return list.ToArray();
            }
            catch { return DefaultWords; }
        }

        static string Hit(string s, string[] words)
        {
            string n = s.ToLowerInvariant();
            foreach (string w in words) if (n.Contains(w)) return w;
            return null;
        }

        public static void Run()
        {
            string[] words = Words();
            var sb = new StringBuilder();
            var seen = new HashSet<Transform>();
            int count = 0;

            foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || seen.Contains(t)) continue;
                // только объекты сцены (у ассетов/префабов hideFlags и нет корня в сцене)
                if (t.gameObject.hideFlags != HideFlags.None) continue;

                string why = Hit(t.name, words);
                PlayMakerFSM[] fsms = t.GetComponents<PlayMakerFSM>();
                if (why == null)
                    foreach (PlayMakerFSM f in fsms) { string h = Hit(f.FsmName, words); if (h != null) { why = "fsm:" + h; break; } }
                if (why == null) continue;

                seen.Add(t);
                count++;
                sb.AppendLine("=== " + PathOf(t) + "   [" + why + "]  active=" + t.gameObject.activeInHierarchy + "  children=" + t.childCount);
                var comps = new StringBuilder("    ");
                foreach (Component c in t.GetComponents<Component>())
                    if (c != null && !(c is Transform)) comps.Append(c.GetType().Name).Append(' ');
                sb.AppendLine(comps.ToString());
                foreach (PlayMakerFSM f in fsms)
                {
                    try
                    {
                        var states = new StringBuilder();
                        foreach (FsmState st in f.FsmStates) states.Append(st.Name).Append(" | ");
                        var glob = new StringBuilder();
                        foreach (FsmTransition tr in f.Fsm.GlobalTransitions) glob.Append(tr.EventName).Append(' ');
                        sb.AppendLine("    FSM '" + f.FsmName + "' state=" + f.ActiveStateName + "  states: " + states + (glob.Length > 0 ? "  global: " + glob : ""));
                    }
                    catch (Exception e) { sb.AppendLine("    FSM '" + f.FsmName + "' ERR " + e.Message); }
                }
                if (count > 3000) { sb.AppendLine("... обрезано на 3000 объектах"); break; }
            }

            sb.Insert(0, "words: " + string.Join(", ", words) + "\nfound: " + count + "\n\n");
            File.WriteAllText(Path.Combine(Application.dataPath, "../scene_search.txt"), sb.ToString());
        }
    }
}
