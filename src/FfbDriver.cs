using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using MSCLoader;
using UnityEngine;

namespace WheelFFB
{
    public class FfbDriver : MonoBehaviour
    {
        [DllImport("UnityForceFeedback")]
        static extern int SetDeviceForcesXY(int x, int y);

        // Значения перезаписываются ползунками из настроек мода.
        public static float Gain = 5f;       // единиц DirectInput на единицу CarDynamics.forceFeedback
        public static float MaxPercent = 80f;  // потолок мягкого насыщения, % от 10000
        public static float SmoothHz = 12f;    // частота среза ФНЧ
        public static float FadeSpeed = 15f;   // км/ч, с которой сила полная
        public static bool ShowDebug;

        // Профили техники (перезаписываются из настроек мода).
        public static float KekmetGain = 2f, KekmetFade = 5f;     // трактор: тяжелее, полная сила уже с 5 км/ч
        public static float GifuGain = 0.15f, GifuFade = 15f;      // грузовик: сила дороги (Mz), возврат даёт центрирование ниже
        public static bool  GifuRoadInvert = true; // у Гифу Mz от физики направлен ОТ центра (по логам: −2.97 на градус против +1.18 у Sorbet)
        public static float GifuParkMul = 2f;      // тугость руля на месте: грузовик тяжелее легковой
        public static float GifuRoadHz = 1.5f;      // доп. сглаживание силы дороги: Mz Гифу шумит на уровне самого сигнала
        public static float GifuCenter = 20f;     // % силы: прямое центрирование по положению руля (без задержки игры)
        public static float GifuCenterWidth = 90f;// ° руля, на которых центрирование выходит на полную силу
        public static float GifuDamping = 15f;    // % силы при вращении 450°/с: демпфер на ходу

        // Тугой руль на месте (трение шин о дорогу): сопротивление вращению руля, пропадает с ростом скорости.
        public static float ParkFriction = 15f;   // % силы: усилие, с которым руль «проворачивается»
        public static float ParkWidth = 15f;      // ° руля: масштаб плавного нарастания усилия (мягкость залипания)
        public static float ParkDamping = 10f;    // % силы при вращении руля 450°/с — гасит качания пружины
        public static float ParkFadeSpeed = 20f;  // км/ч, к которой эффект исчезает
        public static bool ParkInvert;            // если руль на месте «тянет», а не сопротивляется
        public static int WheelDevice = 0;        // winmm ID руля (ось X); −1 = брать steerInput игры
        public const float WheelHalfRange = 450f; // ° от центра до упора (900° в Pit House)
        public static CarController Current;   // машина, в которой сидит игрок (для PedalFix)

        const float MinSpeed = 2f;       // ниже — сила 0
        const float DiMax = 10000f;
        const float RescanEvery = 2f;

        static FieldInfo ffField;        // CarDynamics.forceFeedback
        static FieldInfo startedField;   // ForceFeedback.forceFeedbackEnabled (private)

        readonly HashSet<ForceFeedback> known = new HashSet<ForceFeedback>();
        readonly HashSet<CarController> cars = new HashSet<CarController>();
        readonly Dictionary<CarController, CarDynamics> dyns = new Dictionary<CarController, CarDynamics>();
        readonly Dictionary<CarController, Rigidbody> bodies = new Dictionary<CarController, Rigidbody>();
        HutongGames.PlayMaker.FsmString currentVehicle;   // глобальная PlayerCurrentVehicle, если есть
        bool globalChecked;
        string vehicleVar = "n/a";
        string pickMode = "-";

        float output, lastRaw, peakRaw, speedKmh, nextScan;
        string carName = "-";
        bool dllFailed;

        void Start()
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            ffField = typeof(CarDynamics).GetField("forceFeedback", all);
            startedField = typeof(ForceFeedback).GetField("forceFeedbackEnabled", all);
        }

        // LateUpdate — после всех Update, в т.ч. игровых.
        void LateUpdate()
        {
            if (Time.unscaledTime >= nextScan) { Rescan(); nextScan = Time.unscaledTime + RescanEvery; }
            MuteVanilla();

            CarController cc = PickVehicle();
            float target = 0f, park = 0f;

            if (cc != null && ffField != null && Time.timeScale > 0f)
            {
                CarDynamics cd = Dyn(cc);
                float raw = cd != null ? Convert.ToSingle(ffField.GetValue(cd)) : 0f;
                if (float.IsNaN(raw) || float.IsInfinity(raw)) raw = 0f;
                if (Invert(cc)) raw = -raw;   // тот же знак, что у игры
                bool gifu = cc.transform.root.name.ToUpperInvariant().StartsWith("GIFU");
                if (gifu)
                {
                    if (GifuRoadInvert) raw = -raw;
                    if (roadCar != cc) { roadCar = cc; roadLP = raw; }
                    roadLP += (raw - roadLP) * (1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Max(0.5f, GifuRoadHz) * Time.unscaledDeltaTime));
                    raw = roadLP;
                }
                lastRaw = raw;
                peakRaw = Mathf.Max(Mathf.Abs(raw), peakRaw - 10f * Time.deltaTime);

                Rigidbody rb = Body(cc);
                speedKmh = rb != null ? rb.velocity.magnitude * 3.6f : 0f;
                float gainMul, fadeSpeed, minSpeed;
                Profile(cc, out gainMul, out fadeSpeed, out minSpeed);
                float fade = Mathf.Clamp01((speedKmh - minSpeed) / Mathf.Max(0.1f, fadeSpeed - minSpeed));

                float max = DiMax * MaxPercent / 100f;
                float x = raw * Gain * gainMul * fade;
                target = max * (float)Math.Tanh(x / max);   // линейно в середине, мягко у потолка

                park = ParkForce(cc, ParkMul(cc, gainMul));
                park += DriveAssist(cc, fade);

            }
            else
            {
                lastRaw = 0f;
                speedKmh = 0f;
                steerCar = null;
                if (cc == null) output = 0f;   // не за рулём — отпускаем руль сразу, без плавного спада
            }

            float a = 1f - Mathf.Exp(-2f * Mathf.PI * SmoothHz * Time.unscaledDeltaTime);
            output += (target - output) * a;
            // Демпфер на месте идёт мимо фильтра: задержка в демпфере сама раскачивает руль.
            float lim = DiMax * MaxPercent / 100f;
            lastPark = park;
            lastSent = Mathf.RoundToInt(Mathf.Clamp(output + park, -lim, lim));
            Send(lastSent);

        }

        // ---------- тугой руль на месте ----------
        // Модель «залипания»: руль держится мягкой пружиной за якорь. Усилие плавно растёт с отклонением
        // и выходит на уровень трения; дальше якорь едет за рулём. Сила зависит от положения,
        // а не от знака скорости, поэтому на месте не скачет влево-вправо.
        static FieldInfo steerInputField;
        CarController steerCar;
        float anchor, lastSteer, steerVel, lastPark, wheelPos;
        int lastSent;

        float ReadWheel(CarController cc)
        {
            if (WheelDevice >= 0)
            {
                int x = WinJoy.Axis(WheelDevice, 1);
                if (x >= 0) return x / 65535f * 2f - 1f;          // −1…+1, напрямую с руля
            }
            if (steerInputField == null)
                steerInputField = typeof(CarController).GetField("steerInput", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return steerInputField != null ? Convert.ToSingle(steerInputField.GetValue(cc)) : float.NaN;
        }

        float ParkForce(CarController cc, float gainMul)
        {
            float steer = ReadWheel(cc);
            if (float.IsNaN(steer)) return 0f;
            wheelPos = steer;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            if (steerCar != cc) { steerCar = cc; anchor = lastSteer = steer; steerVel = 0f; }

            float v = (steer - lastSteer) / dt;
            lastSteer = steer;
            // Демпфер должен реагировать без задержки, иначе сам раскачивает: фильтр только от шума оси, 30 Гц.
            steerVel += (v - steerVel) * (1f - Mathf.Exp(-2f * Mathf.PI * 30f * dt));

            float still = 1f - Mathf.Clamp01(speedKmh / Mathf.Max(1f, ParkFadeSpeed));
            if (still <= 0f) { anchor = steer; return 0f; }

            // Плавное насыщение: F·tanh(d/w). Якорь тянется за рулём, когда отклонение больше 2.5w —
            // там кривая уже почти плоская (98 % трения), поэтому никакого излома и «удара».
            float width = Mathf.Max(0.5f, ParkWidth) / WheelHalfRange;
            float lim2 = 2.5f * width;
            float d = steer - anchor;
            if (d > lim2) anchor = steer - lim2;
            else if (d < -lim2) anchor = steer + lim2;
            d = steer - anchor;

            float friction = DiMax * ParkFriction / 100f * (float)Math.Tanh(d / width);
            float damping  = DiMax * ParkDamping / 100f * steerVel;
            float f = (friction + damping) * still * gainMul;
            // Положительная сила возвращает руль из положительного положения — тянет к якорю.
            return ParkInvert ? -f : f;
        }

        // ---------- центрирование и демпфер на ходу (Гифу) ----------
        // Сила от физики (Mz) приходит с задержкой игрового сглаживания руля — у грузовика большой,
        // поэтому при нужной для возврата силе руль раскачивается. Здесь центр берётся прямо с руля.
        float DriveAssist(CarController cc, float fade)
        {
            if (!cc.transform.root.name.ToUpperInvariant().StartsWith("GIFU")) return 0f;
            if (fade <= 0f) return 0f;
            float w = Mathf.Max(5f, GifuCenterWidth) / WheelHalfRange;
            float center = DiMax * GifuCenter / 100f * (float)Math.Tanh(wheelPos / w);
            float damp   = DiMax * GifuDamping / 100f * steerVel;
            float f = (center + damp) * fade;
            return ParkInvert ? -f : f;   // тот же знак, что у тугого руля на месте
        }

        void Rescan()
        {
            known.RemoveWhere(f => f == null);
            // Включённые компоненты на активных объектах; выключенные нами уже лежат в known.
            foreach (ForceFeedback f in FindObjectsOfType<ForceFeedback>())
                if (f != null) known.Add(f);
            cars.RemoveWhere(c => c == null);
            foreach (CarController c in FindObjectsOfType<CarController>())
                if (c != null) cars.Add(c);
        }

        // Знак: с компонента FFB этой машины, иначе с любого найденного (у UnityCar он один на всех).
        bool Invert(CarController cc)
        {
            ForceFeedback f = cc.GetComponent<ForceFeedback>();
            if (f != null) return f.invertForceFeedback;
            foreach (ForceFeedback k in known) if (k != null) return k.invertForceFeedback;
            return true;
        }

        // Выключаем игровой компонент только после его Start (там InitDirectInput).
        // Если игра включит его снова — выключим в следующем кадре.
        void MuteVanilla()
        {
            foreach (ForceFeedback f in known)
            {
                if (f == null || !f.enabled) continue;
                bool started = startedField == null || (bool)startedField.GetValue(f);
                if (started) f.enabled = false;
            }
        }

        // Машина выбирается ТОЛЬКО в режиме вождения (driving mode).
        // Признак: FSM PlayerTrigger при входе в режим прописывает AxisCarController.steerAxis = "Horizontal",
        // при выходе — пустую строку. Работает для любой машины, независимо от её имени.
        static readonly Dictionary<Type, FieldInfo> steerAxisFields = new Dictionary<Type, FieldInfo>();

        static bool IsDriven(CarController c)
        {
            Type t = c.GetType();
            FieldInfo fi;
            if (!steerAxisFields.TryGetValue(t, out fi))
            {
                fi = t.GetField("steerAxis", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                steerAxisFields[t] = fi;
            }
            if (fi == null) return false;
            return !string.IsNullOrEmpty(fi.GetValue(c) as string);
        }

        CarController PickVehicle()
        {
            if (!globalChecked)
            {
                globalChecked = true;
                currentVehicle = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
            }
            vehicleVar = currentVehicle != null ? "'" + (currentVehicle.Value ?? "") + "'" : "n/a";

            // Главный признак — глобальная PlayerCurrentVehicle: при входе в driving mode игра пишет туда
            // имя машины ("Corris"), при выходе — пустую строку (FSM PlayerTrigger, состояние Create player).
            string cur = currentVehicle != null ? (currentVehicle.Value ?? "") : null;
            if (cur != null && cur.Length == 0) { pickMode = "not driving"; return SetCurrent(null); }

            string lv = cur != null ? cur.ToLowerInvariant() : null;
            CarController byName = null, byAxis = null;
            foreach (CarController c in cars)
            {
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                bool driven = IsDriven(c);
                if (lv != null)
                {
                    string ln = c.transform.root.name.ToLowerInvariant();
                    if (ln.Contains(lv) || lv.Contains(ln))
                    {
                        if (driven) { pickMode = "driving"; return SetCurrent(c); }
                        if (byName == null) byName = c;
                    }
                }
                if (driven && byAxis == null) byAxis = c;
            }
            // Имя не совпало с корнем машины — доверяем осям управления, потом имени.
            if (byAxis != null) { pickMode = "driving (axis)"; return SetCurrent(byAxis); }
            if (byName != null) { pickMode = "driving (name)"; return SetCurrent(byName); }
            pickMode = "not driving";
            return SetCurrent(null);
        }

        string profileName = "-";
        CarController roadCar;
        float roadLP;

        void Profile(CarController cc, out float gainMul, out float fadeSpeed, out float minSpeed)
        {
            string n = cc.transform.root.name.ToUpperInvariant();
            if (n.StartsWith("KEKMET"))    { gainMul = KekmetGain; fadeSpeed = KekmetFade; minSpeed = 0.5f; profileName = "Kekmet"; }
            else if (n.StartsWith("GIFU")) { gainMul = GifuGain;   fadeSpeed = GifuFade;   minSpeed = MinSpeed; profileName = "Gifu"; }
            else                           { gainMul = 1f;         fadeSpeed = FadeSpeed;  minSpeed = MinSpeed; profileName = "default"; }
        }

        // Множитель тугости на месте — отдельно от силы дороги (у Гифу сила дороги нарочно слабая).
        static float ParkMul(CarController cc, float gainMul)
        {
            string n = cc.transform.root.name.ToUpperInvariant();
            if (n.StartsWith("GIFU")) return GifuParkMul;
            return gainMul;   // Кекмет — его множитель (тяжёлый трактор), остальные — 1
        }

        CarController SetCurrent(CarController c)
        {
            Current = c;
            carName = c != null ? c.transform.root.name : "-";
            return c;
        }

        CarDynamics Dyn(CarController f)
        {
            CarDynamics cd;
            if (!dyns.TryGetValue(f, out cd) || cd == null) { cd = f.GetComponent<CarDynamics>(); dyns[f] = cd; }
            return cd;
        }

        Rigidbody Body(CarController f)
        {
            Rigidbody rb;
            if (!bodies.TryGetValue(f, out rb) || rb == null) { rb = f.GetComponentInParent<Rigidbody>(); bodies[f] = rb; }
            return rb;
        }

        void Send(int x)
        {
            if (dllFailed) return;
            try { SetDeviceForcesXY(x, 0); }
            catch (Exception e) { dllFailed = true; ModConsole.Error("WheelFFB: " + e.Message); }
        }

        void OnApplicationQuit() { Send(0); }
        void OnDestroy() { output = 0f; Send(0); }

        void OnGUI()
        {
            if (!ShowDebug) return;
            GUI.Box(new Rect(10, 10, 300, 170), "");
            GUI.Label(new Rect(20, 15, 290, 165),
                "Car: " + carName + "   cars: " + cars.Count + "  FFB comps: " + known.Count +
                "\nPlayerCurrentVehicle: " + vehicleVar + "  (" + pickMode + ")" +
                "\nRaw: " + lastRaw.ToString("F1") + "   peak: " + peakRaw.ToString("F1") +
                "\nSpeed: " + speedKmh.ToString("F0") + " km/h" +
                "\nOut: " + (output / DiMax * 100f).ToString("F0") + " %   park: " + (lastPark / DiMax * 100f).ToString("F0") + " %" +
                "\nWheel: " + (wheelPos * WheelHalfRange).ToString("F0") + "°" + (WheelDevice >= 0 ? " (winmm " + WheelDevice + ")" : " (game)") +
                "\nProfile " + profileName + "   Gain " + Gain.ToString("F1") + "  Max " + MaxPercent.ToString("F0") + "%  LP " + SmoothHz.ToString("F0") + " Hz" +
                (dllFailed ? "\nDLL ERROR (see console)" : ""));
        }
    }
}
