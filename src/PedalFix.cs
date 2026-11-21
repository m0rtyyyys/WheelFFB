using System.Reflection;
using MSCLoader;
using UnityEngine;

namespace WheelFFB
{
    // Читает педали через winmm (полный ход, как в joy.cpl) и подставляет 0…1
    // в CarController машины, в которой сидит игрок.
    public class PedalFix : MonoBehaviour
    {
        // Перезаписываются из настроек мода.
        public static bool Enabled = true;
        public static int Device = 0;                              // ID в winmm, 0…15
        public static int ThrottleAxis = 3, BrakeAxis = 4, ClutchAxis = 0;   // 1=X 2=Y 3=Z 4=R 5=U 6=V, 0 = не трогать
        public static bool ThrottleInvert = true, BrakeInvert = true, ClutchInvert = true;
        public static float ThrottleCurve = 1f, BrakeCurve = 1f;
        public static float Deadzone = 0.03f;

        // Сцепление: ход педали → положение сцепления (CarController ставит clutch = 1 − clutchInput при выкл. Auto clutch).
        public static float ClutchFreeTop = 0.15f;   // первые 15 % хода — сцепление ещё полностью замкнуто
        public static float ClutchOpenAt  = 0.85f;   // с 85 % хода — полностью разомкнуто
        public static float ClutchGrab    = 2.0f;    // кривая схватывания: >1 — мягче в начале (момент сцепления 216 Н·м > мотора 152)

        static FieldInfo fThr, fBrk, fClu;
        int rawT = -1, rawB = -1, rawC = -1;
        float outT, outB, outC;

        void Start()
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            fThr = typeof(CarController).GetField("throttleInput", all);
            fBrk = typeof(CarController).GetField("brakeInput", all);
            fClu = typeof(CarController).GetField("clutchInput", all);
        }

        void LateUpdate() { Apply(); Animate(); }
        void FixedUpdate() { Apply(); }

        // ---------- анимация педалей ----------
        // Игровые FSM 'Throttle'/'Brake' ставят поворот pivot_* в Update по оси игры (отвязанной).
        // Мы в LateUpdate ставим поворот по нашим значениям — он и рисуется.
        Transform animRoot, pivotT, pivotB, pivotC;
        float kT, kB, kC, pedC;
        int axT, axB, axC;   // 0 = X, 1 = Y, 2 = Z

        void Animate()
        {
            if (!Enabled) return;
            CarController cc = FfbDriver.Current;
            if (cc == null) return;
            Transform root = cc.transform.root;
            if (root != animRoot) FindPedals(root);

            if (pivotT != null && ThrottleAxis > 0) pivotT.localRotation = Rot(axT, outT * kT);
            if (pivotB != null && BrakeAxis > 0)    pivotB.localRotation = Rot(axB, outB * kB);
            if (pivotC != null && ClutchAxis > 0)   pivotC.localRotation = Rot(axC, pedC * kC);   // ход педали, не положение сцепления
        }

        static Quaternion Rot(int axis, float a)
        {
            return Quaternion.Euler(axis == 0 ? a : 0f, axis == 1 ? a : 0f, axis == 2 ? a : 0f);
        }

        // Ищем FSM, которые каждый кадр поворачивают педаль по значению газа/тормоза.
        //   Corris: FSM 'Throttle'/'Brake' (GetProperty / CInputGetAxisRaw "Brake"), поворот X.
        //   Gifu:   FSM 'Angle' на Pedals/Throttle, Pedals/Brake, поворот X, множитель −19.
        //   Kekmet: FSM 'Anim' на Brake/Pivot, ThrottleFoot/Pivot, читает throttleInput/brakeInput, поворот Y.
        void FindPedals(Transform root)
        {
            animRoot = root; pivotT = pivotB = pivotC = null;
            foreach (PlayMakerFSM fsm in root.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                Transform pivot = null; float k = 0f; int axis = 0;
                string src = null;       // что читает FSM
                try
                {
                    foreach (HutongGames.PlayMaker.FsmState st in fsm.FsmStates)
                    {
                        if (st.Actions == null) continue;
                        foreach (HutongGames.PlayMaker.FsmStateAction a in st.Actions)
                        {
                            if (a == null) continue;
                            string tn = a.GetType().Name;
                            if (tn == "SetRotation" && pivot == null)
                            {
                                var od = Field(a, "gameObject") as HutongGames.PlayMaker.FsmOwnerDefault;
                                if (od != null && od.OwnerOption == HutongGames.PlayMaker.OwnerDefaultOption.UseOwner) pivot = fsm.transform;
                                else if (od != null && od.GameObject != null && od.GameObject.Value != null) pivot = od.GameObject.Value.transform;
                                string[] ang = { "xAngle", "yAngle", "zAngle" };
                                for (int i = 0; i < 3; i++)
                                {
                                    var f = Field(a, ang[i]) as HutongGames.PlayMaker.FsmFloat;
                                    if (f != null && !string.IsNullOrEmpty(f.Name)) { axis = i; break; }
                                }
                            }
                            else if (tn == "FloatOperator" && k == 0f)
                            {
                                object op = Field(a, "operation");
                                var f2 = Field(a, "float2") as HutongGames.PlayMaker.FsmFloat;
                                if (op != null && op.ToString() == "Multiply" && f2 != null && string.IsNullOrEmpty(f2.Name)) k = f2.Value;
                            }
                            else if (tn == "GetProperty" && src == null)
                            {
                                object tp = Field(a, "targetProperty");
                                object pname = tp != null ? Field(tp, "PropertyName") : null;
                                if (pname != null) src = pname.ToString();
                            }
                            else if ((tn == "CInputGetAxisRaw" || tn == "CInputGetAxis") && src == null)
                            {
                                var an = Field(a, "axisName") as HutongGames.PlayMaker.FsmString;
                                if (an != null) src = an.Value;
                            }
                        }
                    }
                }
                catch { }
                if (pivot == null || k == 0f) continue;   // не поворот педали (рычаги, звук и т.п.)

                bool thr, brk, clu;
                string s = (src ?? "").ToLowerInvariant();
                if (s.Length > 0 && !s.Contains("hand"))
                {
                    thr = s.Contains("throttle");
                    brk = s.Contains("brake");
                    clu = s.Contains("clutch");
                }
                else
                {
                    // Свойство не прочиталось — по именам (Corris: FSM 'Throttle'/'Clutch', Gifu: 'Angle' на объекте Throttle/Brake/Clutch).
                    string on = fsm.gameObject.name;
                    thr = fsm.FsmName == "Throttle" || (fsm.FsmName == "Angle" && on == "Throttle");
                    brk = fsm.FsmName == "Brake"    || (fsm.FsmName == "Angle" && on == "Brake");
                    clu = fsm.FsmName == "Clutch"   || (fsm.FsmName == "Angle" && on == "Clutch");
                }
                string what;
                if (thr && pivotT == null)      { pivotT = pivot; kT = k; axT = axis; what = "throttle"; }
                else if (brk && pivotB == null) { pivotB = pivot; kB = k; axB = axis; what = "brake"; }
                else if (clu && pivotC == null) { pivotC = pivot; kC = k; axC = axis; what = "clutch"; }
                else continue;
                ModConsole.Print("WheelFFB: pedal " + what + " = " + pivot.name + " (FSM '" + fsm.FsmName +
                                 "' on " + fsm.gameObject.name + ", reads " + (src ?? "?") + ", x" + k + ", axis " + "XYZ"[axis] + ")");
            }
            ModConsole.Print("WheelFFB: pedals in " + root.name + ": throttle " + (pivotT != null) + ", brake " + (pivotB != null) + ", clutch " + (pivotC != null));
        }

        static object Field(object o, string name)
        {
            for (System.Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null) return fi.GetValue(o);
                PropertyInfo pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null) return pi.GetValue(o, null);
            }
            return null;
        }

        void Apply()
        {
            if (!Enabled) return;
            CarController cc = FfbDriver.Current;
            if (cc == null) return;

            if (ThrottleAxis > 0 && fThr != null) { outT = Read(ThrottleAxis, ThrottleInvert, ThrottleCurve, out rawT); fThr.SetValue(cc, outT); }
            if (BrakeAxis > 0 && fBrk != null)    { outB = Read(BrakeAxis, BrakeInvert, BrakeCurve, out rawB);          fBrk.SetValue(cc, outB); }
            if (ClutchAxis > 0 && fClu != null)
            {
                float p = Read(ClutchAxis, ClutchInvert, 1f, out rawC);                 // 0 отпущена … 1 выжата
                pedC = p;                                                                // для анимации педали
                float engaged = 1f - Mathf.InverseLerp(ClutchFreeTop, Mathf.Max(ClutchFreeTop + 0.05f, ClutchOpenAt), p);
                float pos = Mathf.Pow(engaged, Mathf.Max(0.2f, ClutchGrab));            // положение сцепления 0…1
                outC = 1f - pos;                                                         // clutchInput: 1 = выжато
                fClu.SetValue(cc, outC);
            }
        }

        static float Read(int axis, bool invert, float curve, out int raw)
        {
            raw = WinJoy.Axis(Device, axis);
            if (raw < 0) return 0f;                 // устройства нет — не газуем
            float x = raw / 65535f;
            if (invert) x = 1f - x;
            x = Mathf.InverseLerp(Deadzone, 1f - Deadzone, x);
            return Mathf.Pow(x, curve);
        }

        static string Line(string name, int axis, bool inv, int raw, float o)
        {
            return "\n" + name + " " + WinJoy.AxisNames[Mathf.Clamp(axis, 0, 6)] + (inv ? " inv" : "") +
                   "  raw " + raw + " -> " + o.ToString("F2");
        }

        void OnGUI()
        {
            if (!FfbDriver.ShowDebug) return;
            GUI.Box(new Rect(10, 190, 300, 75), "");
            GUI.Label(new Rect(20, 195, 290, 70),
                "Pedals " + (Enabled ? "ON" : "OFF") + "  device " + Device + (WinJoy.Present(Device) ? "" : " (NOT FOUND)") +
                Line("Thr", ThrottleAxis, ThrottleInvert, rawT, outT) +
                Line("Brk", BrakeAxis, BrakeInvert, rawB, outB) +
                Line("Clu", ClutchAxis, ClutchInvert, rawC, outC));
        }
    }
}
