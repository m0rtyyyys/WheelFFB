using MSCLoader;
using UnityEngine;

namespace WheelFFB
{
    public class WheelFFB : Mod
    {
        public override string ID => "WheelFFB";
        public override string Name => "Wheel FFB";
        public override string Author => "mortyyyys";
        public override string Version => "1.0";
        public override string Description =>
            "Proper force feedback for direct-drive wheels, heavy steering at standstill, " +
            "full-range pedals and analog clutch via winmm, wheel buttons above 20 via DirectInput.";
        public override Game SupportedGames => Game.MyWinterCar;

        // Общее
        SettingsCheckBox cOverlay;
        // FFB
        SettingsSlider sGain, sMax, sSmooth, sFade, sWheelDev;
        // Тугой руль на месте
        SettingsSlider sParkFric, sParkWidth, sParkDamp, sParkFade;
        SettingsCheckBox cParkInv;
        // Машины
        SettingsSlider sKekGain, sKekFade;
        SettingsSlider sGifuGain, sGifuRoadHz, sGifuFade, sGifuPark, sGifuCenter, sGifuCenterW, sGifuDamp;
        SettingsCheckBox cGifuRoadInv;
        // Педали
        SettingsCheckBox cPedals, cInvThr, cInvBrk, cInvClu;
        SettingsSlider sDev, sThrAx, sBrkAx, sCluAx, sThrCurve, sBrkCurve, sDead, sCluFree, sCluOpen, sCluGrab;

        public override void ModSetup()
        {
            SetupFunction(Setup.OnLoad, Mod_OnLoad);
            SetupFunction(Setup.Update, Mod_Update);
            SetupFunction(Setup.ModSettings, Mod_Settings);
        }

        private void Mod_Settings()
        {
            Settings.AddHeader("General");
            cOverlay = Settings.AddCheckBox("overlay", "Show setup/debug overlay (axes, buttons, forces)", false);

            Settings.AddHeader("Force feedback");
            sGain     = Settings.AddSlider("gain",     "Gain (road force per unit)",           0f, 30f, 5f, null, 1);
            sMax      = Settings.AddSlider("max",      "Max force, %",                         20f, 100f, 80f, null, 0);
            sSmooth   = Settings.AddSlider("smooth",   "Smoothing, Hz (lower = softer)",       1f, 30f, 6f, null, 0);
            sFade     = Settings.AddSlider("fade",     "Full force from, km/h",                3f, 40f, 15f, null, 0);
            sWheelDev = Settings.AddSlider("wheelDev", "Wheel device ID (winmm, -1 = game)",   -1f, 15f, 0f, null, 0);

            Settings.AddHeader("Heavy steering when standing");
            sParkFric  = Settings.AddSlider("parkFric",   "Friction, %",                        0f, 50f, 15f, null, 0);
            sParkWidth = Settings.AddSlider("parkWidth3", "Softness, deg (bigger = smoother)",  5f, 45f, 15f, null, 0);
            sParkDamp  = Settings.AddSlider("parkDamp3",  "Anti-shake damping, %",              0f, 40f, 10f, null, 0);
            sParkFade  = Settings.AddSlider("parkFade",   "Fades out by, km/h",                 2f, 60f, 20f, null, 0);
            cParkInv   = Settings.AddCheckBox("parkInv",  "Invert (if wheel pulls instead of resisting)", false);

            Settings.AddHeader("Kekmet");
            sKekGain = Settings.AddSlider("kekGain", "Force multiplier",          0.5f, 5f, 2f, null, 1);
            sKekFade = Settings.AddSlider("kekFade", "Full force from, km/h",     1f, 20f, 5f, null, 0);

            Settings.AddHeader("Gifu");
            sGifuGain    = Settings.AddSlider("gifuGain3",   "Road force multiplier",                0f, 1f, 0.15f, null, 2);
            cGifuRoadInv = Settings.AddCheckBox("gifuRoadInv", "Flip road force (physics pushes away from center)", true);
            sGifuRoadHz  = Settings.AddSlider("gifuRoadHz2", "Road force smoothing, Hz",             0.5f, 10f, 1.5f, null, 1);
            sGifuFade    = Settings.AddSlider("gifuFade",    "Full force from, km/h",                1f, 40f, 15f, null, 0);
            sGifuCenter  = Settings.AddSlider("gifuCenter",  "Centering, %",                         0f, 60f, 20f, null, 0);
            sGifuCenterW = Settings.AddSlider("gifuCenterW", "Centering width, deg",                 20f, 270f, 90f, null, 0);
            sGifuDamp    = Settings.AddSlider("gifuDamp",    "Damping, %",                           0f, 50f, 15f, null, 0);
            sGifuPark    = Settings.AddSlider("gifuPark",    "Heaviness when standing, x",           0.5f, 4f, 2f, null, 1);

            Settings.AddHeader("Pedals");
            cPedals   = Settings.AddCheckBox("pedals", "Pedal fix (read pedals via winmm)", true);
            sDev      = Settings.AddSlider("dev",      "Pedal device ID (winmm)",                     0f, 15f, 0f, null, 0);
            sThrAx    = Settings.AddSlider("thrAx",    "Throttle axis (1X 2Y 3Z 4R 5U 6V, 0 = off)",  0f, 6f, 3f, null, 0);
            cInvThr   = Settings.AddCheckBox("invThr", "Invert throttle", true);
            sThrCurve = Settings.AddSlider("thrCurve", "Throttle curve (1 = linear)",                 1f, 3f, 1f, null, 1);
            sBrkAx    = Settings.AddSlider("brkAx",    "Brake axis (0 = off)",                        0f, 6f, 4f, null, 0);
            cInvBrk   = Settings.AddCheckBox("invBrk", "Invert brake", true);
            sBrkCurve = Settings.AddSlider("brkCurve", "Brake curve (1 = linear)",                    1f, 3f, 1f, null, 1);
            sDead     = Settings.AddSlider("dead",     "Pedal deadzone, %",                           0f, 10f, 3f, null, 0);

            Settings.AddHeader("Clutch (turn off Auto clutch in game options)");
            sCluAx   = Settings.AddSlider("cluAx",   "Clutch axis (0 = off)",              0f, 6f, 0f, null, 0);
            cInvClu  = Settings.AddCheckBox("invClu", "Invert clutch", true);
            sCluFree = Settings.AddSlider("cluFree", "Free travel at top, %",              0f, 50f, 15f, null, 0);
            sCluOpen = Settings.AddSlider("cluOpen", "Fully open from, %",                 50f, 100f, 85f, null, 0);
            sCluGrab = Settings.AddSlider("cluGrab", "Bite softness (1 = linear)",         1f, 4f, 2f, null, 1);
        }

        private void Mod_OnLoad()
        {
            GameObject go = new GameObject("WheelFFB_Driver");
            go.AddComponent<FfbDriver>();
            go.AddComponent<PedalFix>();
            go.AddComponent<ButtonMapper>();
            go.AddComponent<SetupOverlay>();
            go.AddComponent<DevHooks>();   // только dev: горячие клавиши инструментов отладки
        }

        private void Mod_Update()
        {
            if (sGain == null) return;

            FfbDriver.ShowDebug = cOverlay.GetValue();

            FfbDriver.Gain        = sGain.GetValue();
            FfbDriver.MaxPercent  = sMax.GetValue();
            FfbDriver.SmoothHz    = sSmooth.GetValue();
            FfbDriver.FadeSpeed   = sFade.GetValue();
            FfbDriver.WheelDevice = Mathf.RoundToInt(sWheelDev.GetValue());

            FfbDriver.ParkFriction  = sParkFric.GetValue();
            FfbDriver.ParkWidth     = sParkWidth.GetValue();
            FfbDriver.ParkDamping   = sParkDamp.GetValue();
            FfbDriver.ParkFadeSpeed = sParkFade.GetValue();
            FfbDriver.ParkInvert    = cParkInv.GetValue();

            FfbDriver.KekmetGain = sKekGain.GetValue();
            FfbDriver.KekmetFade = sKekFade.GetValue();

            FfbDriver.GifuGain        = sGifuGain.GetValue();
            FfbDriver.GifuRoadInvert  = cGifuRoadInv.GetValue();
            FfbDriver.GifuRoadHz      = sGifuRoadHz.GetValue();
            FfbDriver.GifuFade        = sGifuFade.GetValue();
            FfbDriver.GifuCenter      = sGifuCenter.GetValue();
            FfbDriver.GifuCenterWidth = sGifuCenterW.GetValue();
            FfbDriver.GifuDamping     = sGifuDamp.GetValue();
            FfbDriver.GifuParkMul     = sGifuPark.GetValue();

            PedalFix.Enabled        = cPedals.GetValue();
            PedalFix.Device         = Mathf.RoundToInt(sDev.GetValue());
            PedalFix.ThrottleAxis   = Mathf.RoundToInt(sThrAx.GetValue());
            PedalFix.ThrottleInvert = cInvThr.GetValue();
            PedalFix.ThrottleCurve  = sThrCurve.GetValue();
            PedalFix.BrakeAxis      = Mathf.RoundToInt(sBrkAx.GetValue());
            PedalFix.BrakeInvert    = cInvBrk.GetValue();
            PedalFix.BrakeCurve     = sBrkCurve.GetValue();
            PedalFix.Deadzone       = sDead.GetValue() / 100f;
            PedalFix.ClutchAxis     = Mathf.RoundToInt(sCluAx.GetValue());
            PedalFix.ClutchInvert   = cInvClu.GetValue();
            PedalFix.ClutchFreeTop  = sCluFree.GetValue() / 100f;
            PedalFix.ClutchOpenAt   = sCluOpen.GetValue() / 100f;
            PedalFix.ClutchGrab     = sCluGrab.GetValue();
        }
    }
}
