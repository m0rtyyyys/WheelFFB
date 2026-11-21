using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using MSCLoader;
using UnityEngine;

namespace WheelFFB
{
    // Кнопки руля, которые игра не видит (Unity 5.0 знает только кнопки 0–19), читаются через DirectInput
    // (до 128 кнопок; если DirectInput не завёлся — через winmm, до 32) и превращаются в нажатия клавиш.
    // Настройка — файл wheel_buttons.txt в папке игры:
    //     # устройство: номер из списка DInput в окне «,» (или winmm ID, если DirectInput не работает)
    //     device=0
    //     # номер кнопки (как в joy.cpl, с 1) = клавиша
    //     21=H
    //     22=L
    //     23=F5
    // Файл перечитывается сам, как только ты его сохранишь.
    public class ButtonMapper : MonoBehaviour
    {
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);
        const uint KEYEVENTF_KEYUP = 0x0002;


        int device;
        readonly Dictionary<int, byte> map = new Dictionary<int, byte>();   // кнопка (с 1) → виртуальный код клавиши
        readonly HashSet<int> held = new HashSet<int>();                     // какие кнопки сейчас «зажаты» как клавиши
        string status = "";
        DateTime loadedAt;
        float nextCheck;

        string ConfigPath { get { return Path.Combine(Application.dataPath, "../wheel_buttons.txt"); } }

        void Start() { Load(); }

        void Load()
        {
            map.Clear();
            device = 0;
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    File.WriteAllText(ConfigPath,
                        "# Кнопки руля -> клавиши. Номер кнопки как в joy.cpl (с 1, до 128). Мод перечитает файл сам после сохранения.\n" +
                        "# device = номер устройства из списка DInput в окне ',' (в игре).\n" +
                        "# Клавиши: A-Z, 0-9, F1-F12, Space, Enter, Tab, Escape, Backspace, Insert, Delete, Home, End,\n" +
                        "#          PageUp, PageDown, Up, Down, Left, Right, LeftShift, LeftControl, LeftAlt, Keypad0-Keypad9\n" +
                        "device=0\n" +
                        "# 21=H\n");
                }
                foreach (string line0 in File.ReadAllLines(ConfigPath))
                {
                    string line = line0.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    if (k.Equals("device", StringComparison.OrdinalIgnoreCase)) { int.TryParse(v, out device); continue; }
                    int btn;
                    byte vk;
                    if (int.TryParse(k, out btn) && btn >= 1 && btn <= DInput.MaxButtons && TryVk(v, out vk)) map[btn] = vk;
                    else ModConsole.Warning("WheelFFB buttons: не понял строку '" + line0 + "'");
                }
                status = "loaded " + map.Count + " button(s), device " + device;
                loadedAt = File.GetLastWriteTime(ConfigPath);
            }
            catch (Exception e) { status = "error: " + e.Message; }
            ModConsole.Print("WheelFFB buttons: " + status);
        }

        void Update()
        {
            // Перечитать файл, если его сохранили (проверка раз в секунду).
            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + 1f;
                try { DateTime t = File.GetLastWriteTime(ConfigPath); if (t != loadedAt) Load(); } catch { }
            }

            foreach (KeyValuePair<int, byte> kv in map)
            {
                bool down = IsPressed(kv.Key);
                bool was = held.Contains(kv.Key);
                if (down == was) continue;
                keybd_event(kv.Value, (byte)MapVirtualKey(kv.Value, 0), down ? 0u : KEYEVENTF_KEYUP, UIntPtr.Zero);
                if (down) held.Add(kv.Key); else held.Remove(kv.Key);
            }
        }

        // DirectInput, если есть устройство с таким номером; иначе winmm (кнопки 1–32).
        bool IsPressed(int button)
        {
            DInput.Poll();
            if (device >= 0 && device < DInput.Devices.Count && DInput.Devices[device].Ok)
                return DInput.Devices[device].Pressed(button);
            if (button > 32) return false;
            int b = WinJoy.Buttons(device);
            return b > 0 && (b & (1 << (button - 1))) != 0;
        }

        void OnDisable()
        {
            // Отпустить всё, что было зажато.
            foreach (int btn in held)
            {
                byte vk;
                if (map.TryGetValue(btn, out vk)) keybd_event(vk, (byte)MapVirtualKey(vk, 0), KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            held.Clear();
        }

        void OnDestroy() { DInput.Shutdown(); }

        void OnGUI()
        {
            if (!FfbDriver.ShowDebug) return;
            DInput.Poll();
            var sb = new System.Text.StringBuilder("Wheel buttons: " + status + "   DInput: " + DInput.Status);
            for (int i = 0; i < DInput.Devices.Count; i++)
            {
                DInput.Device d = DInput.Devices[i];
                sb.Append("\nDI " + i + (i == device ? " *" : "") + ": " + d.Name + (d.Ok ? "" : " (no data)") + "  pressed:");
                for (int b = 1; b <= DInput.MaxButtons; b++) if (d.Pressed(b)) sb.Append(" " + b);
            }
            int h = 30 + 18 * Math.Max(1, DInput.Devices.Count);
            GUI.Box(new Rect(320, 200, 560, h), "");
            GUI.Label(new Rect(330, 205, 550, h), sb.ToString());
        }

        static bool TryVk(string name, out byte vk)
        {
            vk = 0;
            string n = name.Trim().ToUpperInvariant();
            if (n.Length == 1 && ((n[0] >= 'A' && n[0] <= 'Z') || (n[0] >= '0' && n[0] <= '9'))) { vk = (byte)n[0]; return true; }
            if (n.Length >= 2 && n[0] == 'F')
            {
                int f; if (int.TryParse(n.Substring(1), out f) && f >= 1 && f <= 12) { vk = (byte)(0x70 + f - 1); return true; }
            }
            if (n.StartsWith("KEYPAD") && n.Length == 7 && char.IsDigit(n[6])) { vk = (byte)(0x60 + (n[6] - '0')); return true; }
            switch (n)
            {
                case "SPACE": vk = 0x20; return true;
                case "ENTER": case "RETURN": vk = 0x0D; return true;
                case "TAB": vk = 0x09; return true;
                case "ESCAPE": case "ESC": vk = 0x1B; return true;
                case "BACKSPACE": vk = 0x08; return true;
                case "INSERT": vk = 0x2D; return true;
                case "DELETE": vk = 0x2E; return true;
                case "HOME": vk = 0x24; return true;
                case "END": vk = 0x23; return true;
                case "PAGEUP": vk = 0x21; return true;
                case "PAGEDOWN": vk = 0x22; return true;
                case "UP": vk = 0x26; return true;
                case "DOWN": vk = 0x28; return true;
                case "LEFT": vk = 0x25; return true;
                case "RIGHT": vk = 0x27; return true;
                case "LEFTSHIFT": case "SHIFT": vk = 0xA0; return true;
                case "LEFTCONTROL": case "CTRL": vk = 0xA2; return true;
                case "LEFTALT": case "ALT": vk = 0xA4; return true;
            }
            return false;
        }
    }
}
