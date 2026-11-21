using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace WheelFFB
{
    // = (равно) — записать phys_<машина>.txt: все поля физики текущей машины
    // (CarController, AxisCarController, CarDynamics, Drivetrain, Axles, SteeringWheel, все Wheel).
    public static class PhysDump
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static readonly string[] Types = { "CarController", "AxisCarController", "CarDynamics", "Drivetrain", "Axles",
                                           "SteeringWheel", "Wheel", "Rigidbody", "ForceFeedback", "Setup", "Aerodynamics" };

        public static void Tick()
        {
            if (Input.GetKeyDown(KeyCode.Equals)) Run();
        }

        static bool Wanted(Type t)
        {
            for (; t != null; t = t.BaseType)
                foreach (string n in Types) if (t.Name == n) return true;
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
            CarController cc = FfbDriver.Current;
            Transform root = cc != null ? cc.transform.root : null;
            if (root == null) { GameObject g = GameObject.Find("CORRIS"); if (g != null) root = g.transform; }
            if (root == null) return;

            var sb = new StringBuilder();
            sb.AppendLine("vehicle: " + root.name + "   time " + Time.time.ToString("F2"));
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || !Wanted(c.GetType())) continue;
                sb.AppendLine("\n=== " + PathOf(c.transform) + " [" + c.GetType().Name + "]");

                Rigidbody rb = c as Rigidbody;
                if (rb != null)
                {
                    sb.AppendLine("  mass=" + rb.mass + " drag=" + rb.drag + " angularDrag=" + rb.angularDrag +
                                  " centerOfMass=" + rb.centerOfMass + " inertiaTensor=" + rb.inertiaTensor +
                                  " velocity=" + rb.velocity.magnitude.ToString("F2"));
                    continue;
                }
                for (Type t = c.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    foreach (FieldInfo fi in t.GetFields(All))
                    {
                        object v; try { v = fi.GetValue(c); } catch { continue; }
                        sb.AppendLine("  " + t.Name + "." + fi.Name + " = " + Show(v));
                    }
            }

            string name = root.name;
            foreach (char ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            File.WriteAllText(Path.Combine(Application.dataPath, "../phys_" + name + ".txt"), sb.ToString());
        }

        static string Show(object v)
        {
            if (v == null) return "null";
            UnityEngine.Object uo = v as UnityEngine.Object;
            if (uo != null) return uo.name + " (" + uo.GetType().Name + ")";
            Array a = v as Array;
            if (a != null)
            {
                var s = new StringBuilder("[" + a.Length + "] ");
                int n = 0;
                foreach (object e in a) { if (n++ >= 12) { s.Append("…"); break; } s.Append(Show(e)).Append("; "); }
                return s.ToString();
            }
            AnimationCurve curve = v as AnimationCurve;
            if (curve != null)
            {
                var s = new StringBuilder("curve ");
                foreach (Keyframe k in curve.keys) s.Append("(" + k.time.ToString("G4") + "," + k.value.ToString("G4") + ") ");
                return s.ToString();
            }
            return v.ToString();
        }
    }
}
