using System.Runtime.InteropServices;
using UnityEngine;

namespace WheelFFB
{
    // Чтение джойстиков через winmm (как joy.cpl), в обход кривого пересчёта осей в Unity 5.0.
    // Оси: 1 = X, 2 = Y, 3 = Z, 4 = R (обычно Rz), 5 = U, 6 = V. Значения 0…65535.
    public static class WinJoy
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct JOYINFOEX
        {
            public int dwSize, dwFlags;
            public int dwXpos, dwYpos, dwZpos, dwRpos, dwUpos, dwVpos;
            public int dwButtons, dwButtonNumber, dwPOV, dwReserved1, dwReserved2;
        }

        [DllImport("winmm.dll")]
        static extern int joyGetPosEx(int uJoyID, ref JOYINFOEX pji);

        const int JOY_RETURNALL = 0xFF;
        public const int MaxDevices = 16;
        public static readonly string[] AxisNames = { "-", "X", "Y", "Z", "R", "U", "V" };

        static readonly JOYINFOEX[] cache = new JOYINFOEX[MaxDevices];
        static readonly bool[] ok = new bool[MaxDevices];
        static readonly int[] frame = new int[MaxDevices];
        static bool dllFailed;

        static void Poll(int id)
        {
            if (frame[id] == Time.frameCount + 1) return;   // уже опрошено в этом кадре
            frame[id] = Time.frameCount + 1;
            if (dllFailed) { ok[id] = false; return; }
            JOYINFOEX ji = new JOYINFOEX();
            ji.dwSize = Marshal.SizeOf(typeof(JOYINFOEX));
            ji.dwFlags = JOY_RETURNALL;
            try { ok[id] = joyGetPosEx(id, ref ji) == 0; }
            catch { dllFailed = true; ok[id] = false; }
            cache[id] = ji;
        }

        public static bool Present(int id)
        {
            if (id < 0 || id >= MaxDevices) return false;
            Poll(id);
            return ok[id];
        }

        // Битовая маска кнопок 1–32 (бит 0 = кнопка 1), −1 если устройства нет.
        public static int Buttons(int id)
        {
            if (!Present(id)) return -1;
            return cache[id].dwButtons;
        }

        // −1, если устройства/оси нет.
        public static int Axis(int id, int axis)
        {
            if (!Present(id)) return -1;
            JOYINFOEX j = cache[id];
            switch (axis)
            {
                case 1: return j.dwXpos;
                case 2: return j.dwYpos;
                case 3: return j.dwZpos;
                case 4: return j.dwRpos;
                case 5: return j.dwUpos;
                case 6: return j.dwVpos;
                default: return -1;
            }
        }
    }
}
