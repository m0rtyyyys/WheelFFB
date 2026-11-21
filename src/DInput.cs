using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MSCLoader;
using UnityEngine;

namespace WheelFFB
{
    // Минимальный DirectInput 8 через таблицы функций COM (без COM-интеропа Mono).
    // Читает кнопки всех игровых устройств, до 128 кнопок, нумерация как в joy.cpl (с 1).
    public static class DInput
    {
        public class Device
        {
            public string Name;
            public Guid Instance;
            public IntPtr Ptr;
            public readonly byte[] State = new byte[MaxButtons];
            public bool Ok;
            public bool Pressed(int button) { return button >= 1 && button <= MaxButtons && (State[button - 1] & 0x80) != 0; }
        }

        public const int MaxButtons = 128;
        public static readonly List<Device> Devices = new List<Device>();
        public static string Status = "not started";

        // ---- WinAPI ----
        [DllImport("dinput8.dll")]
        static extern int DirectInput8Create(IntPtr hinst, uint version, ref Guid riid, out IntPtr ppv, IntPtr outer);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(IntPtr name);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

        static Guid IID_IDirectInput8W = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700");
        const uint DIRECTINPUT_VERSION = 0x0800;
        const uint DI8DEVCLASS_GAMECTRL = 4;
        const uint DIEDFL_ATTACHEDONLY = 1;
        const uint DISCL_NONEXCLUSIVE = 0x2, DISCL_BACKGROUND = 0x8;

        // ---- сигнатуры методов COM (первый аргумент — this) ----
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumCallback(IntPtr lpddi, IntPtr pvRef);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DI_EnumDevices(IntPtr self, uint devType, EnumCallback cb, IntPtr pvRef, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DI_CreateDevice(IntPtr self, ref Guid guid, out IntPtr dev, IntPtr outer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Dev_NoArgs(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Dev_SetDataFormat(IntPtr self, IntPtr format);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Dev_SetCoop(IntPtr self, IntPtr hwnd, uint flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Dev_GetState(IntPtr self, uint size, [Out] byte[] data);

        // Индексы в vtable
        const int DI_ENUMDEVICES = 4, DI_CREATEDEVICE = 3;
        const int DEV_RELEASE = 2, DEV_ACQUIRE = 7, DEV_UNACQUIRE = 8, DEV_GETSTATE = 9, DEV_SETFORMAT = 11, DEV_SETCOOP = 13, DEV_POLL = 25;

        static IntPtr di;
        static IntPtr dataFormat;
        static EnumCallback enumCb;           // держим ссылку, чтобы GC не собрал колбэк
        static readonly List<KeyValuePair<Guid, string>> found = new List<KeyValuePair<Guid, string>>();
        static int polledFrame = -1;
        static bool started, failed;

        static T Fn<T>(IntPtr obj, int index) where T : class
        {
            IntPtr vtbl = Marshal.ReadIntPtr(obj);
            IntPtr fn = Marshal.ReadIntPtr(vtbl, index * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(fn, typeof(T)) as T;
        }

        public static void Start()
        {
            if (started) return;
            started = true;
            try
            {
                int hr = DirectInput8Create(GetModuleHandle(IntPtr.Zero), DIRECTINPUT_VERSION, ref IID_IDirectInput8W, out di, IntPtr.Zero);
                if (hr < 0 || di == IntPtr.Zero) { Fail("DirectInput8Create 0x" + hr.ToString("X8")); return; }

                dataFormat = BuildButtonFormat();

                found.Clear();
                enumCb = OnDevice;
                hr = Fn<DI_EnumDevices>(di, DI_ENUMDEVICES)(di, DI8DEVCLASS_GAMECTRL, enumCb, IntPtr.Zero, DIEDFL_ATTACHEDONLY);
                if (hr < 0) { Fail("EnumDevices 0x" + hr.ToString("X8")); return; }

                IntPtr hwnd = GetForegroundWindow();
                foreach (KeyValuePair<Guid, string> kv in found)
                {
                    var d = new Device { Name = kv.Value, Instance = kv.Key };
                    Guid g = kv.Key;
                    IntPtr dev;
                    hr = Fn<DI_CreateDevice>(di, DI_CREATEDEVICE)(di, ref g, out dev, IntPtr.Zero);
                    if (hr < 0 || dev == IntPtr.Zero) { ModConsole.Warning("WheelFFB DInput: CreateDevice " + d.Name + " 0x" + hr.ToString("X8")); continue; }
                    d.Ptr = dev;
                    hr = Fn<Dev_SetDataFormat>(dev, DEV_SETFORMAT)(dev, dataFormat);
                    if (hr < 0) ModConsole.Warning("WheelFFB DInput: SetDataFormat " + d.Name + " 0x" + hr.ToString("X8"));
                    hr = Fn<Dev_SetCoop>(dev, DEV_SETCOOP)(dev, hwnd, DISCL_NONEXCLUSIVE | DISCL_BACKGROUND);
                    if (hr < 0) ModConsole.Warning("WheelFFB DInput: SetCooperativeLevel " + d.Name + " 0x" + hr.ToString("X8"));
                    Fn<Dev_NoArgs>(dev, DEV_ACQUIRE)(dev);
                    Devices.Add(d);
                }
                Status = Devices.Count + " device(s)";
                for (int i = 0; i < Devices.Count; i++) ModConsole.Print("WheelFFB DInput " + i + ": " + Devices[i].Name);
            }
            catch (Exception e) { Fail(e.GetType().Name + ": " + e.Message); }
        }

        static int OnDevice(IntPtr lpddi, IntPtr pvRef)
        {
            // DIDEVICEINSTANCEW: dwSize(4) guidInstance(16) guidProduct(16) dwDevType(4) tszInstanceName[260] tszProductName[260] …
            byte[] g = new byte[16];
            Marshal.Copy(new IntPtr(lpddi.ToInt64() + 4), g, 0, 16);
            string product = Marshal.PtrToStringUni(new IntPtr(lpddi.ToInt64() + 4 + 16 + 16 + 4 + 520));
            found.Add(new KeyValuePair<Guid, string>(new Guid(g), product));
            return 1;   // DIENUM_CONTINUE
        }

        // Свой формат данных: 128 кнопок, каждая байт; кнопка N (с 1) → смещение N-1, привязка к экземпляру кнопки N-1.
        static IntPtr BuildButtonFormat()
        {
            int ps = IntPtr.Size;
            int objSize = ps == 8 ? 24 : 16;                 // DIOBJECTDATAFORMAT { GUID* pguid; DWORD ofs; DWORD type; DWORD flags; }
            int fmtSize = ps == 8 ? 32 : 24;                 // DIDATAFORMAT { 5×DWORD; DIOBJECTDATAFORMAT* rgodf; }
            IntPtr objs = Marshal.AllocHGlobal(objSize * MaxButtons);
            for (int i = 0; i < MaxButtons; i++)
            {
                IntPtr o = new IntPtr(objs.ToInt64() + i * objSize);
                Marshal.WriteIntPtr(o, 0, IntPtr.Zero);                                     // pguid = NULL (любая кнопка)
                Marshal.WriteInt32(o, ps, i);                                               // dwOfs
                uint type = 0x0000000C | ((uint)i << 8) | 0x80000000;                       // DIDFT_BUTTON | INSTANCE(i) | OPTIONAL
                Marshal.WriteInt32(o, ps + 4, unchecked((int)type));
                Marshal.WriteInt32(o, ps + 8, 0);                                           // dwFlags
            }
            IntPtr f = Marshal.AllocHGlobal(fmtSize);
            Marshal.WriteInt32(f, 0, fmtSize);          // dwSize
            Marshal.WriteInt32(f, 4, objSize);          // dwObjSize
            Marshal.WriteInt32(f, 8, 0);                // dwFlags
            Marshal.WriteInt32(f, 12, MaxButtons);      // dwDataSize (кратно 4)
            Marshal.WriteInt32(f, 16, MaxButtons);      // dwNumObjs
            Marshal.WriteIntPtr(f, ps == 8 ? 24 : 20, objs);
            return f;
        }

        // Опрос всех устройств — раз за кадр.
        public static void Poll()
        {
            if (!started) Start();
            if (failed || polledFrame == Time.frameCount) return;
            polledFrame = Time.frameCount;
            foreach (Device d in Devices)
            {
                if (d.Ptr == IntPtr.Zero) continue;
                try
                {
                    Fn<Dev_NoArgs>(d.Ptr, DEV_POLL)(d.Ptr);
                    int hr = Fn<Dev_GetState>(d.Ptr, DEV_GETSTATE)(d.Ptr, MaxButtons, d.State);
                    if (hr < 0)
                    {
                        Fn<Dev_NoArgs>(d.Ptr, DEV_ACQUIRE)(d.Ptr);   // потеряли доступ — захватываем снова
                        hr = Fn<Dev_GetState>(d.Ptr, DEV_GETSTATE)(d.Ptr, MaxButtons, d.State);
                    }
                    d.Ok = hr >= 0;
                    if (!d.Ok) Array.Clear(d.State, 0, d.State.Length);
                }
                catch (Exception e) { d.Ok = false; Fail(e.GetType().Name + ": " + e.Message); return; }
            }
        }

        public static void Shutdown()
        {
            foreach (Device d in Devices)
            {
                if (d.Ptr == IntPtr.Zero) continue;
                try { Fn<Dev_NoArgs>(d.Ptr, DEV_UNACQUIRE)(d.Ptr); Fn<Dev_NoArgs>(d.Ptr, DEV_RELEASE)(d.Ptr); } catch { }
                d.Ptr = IntPtr.Zero;
            }
            Devices.Clear();
            if (di != IntPtr.Zero) { try { Fn<Dev_NoArgs>(di, DEV_RELEASE)(di); } catch { } di = IntPtr.Zero; }
            started = false;
        }

        static void Fail(string msg)
        {
            failed = true;
            Status = "error: " + msg;
            ModConsole.Error("WheelFFB DInput: " + msg);
        }
    }
}
