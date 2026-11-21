using System.Text;
using UnityEngine;

namespace WheelFFB
{
    // Окно настройки (включается галочкой «Show setup/debug overlay» в настройках мода):
    // все устройства winmm и их оси 0…65535 с минимумом/максимумом — чтобы выбрать ID и оси педалей и руля.
    public class SetupOverlay : MonoBehaviour
    {
        readonly int[] wMin = Fill(WinJoy.MaxDevices * 7, int.MaxValue);
        readonly int[] wMax = Fill(WinJoy.MaxDevices * 7, int.MinValue);
        static int[] Fill(int n, int v) { int[] a = new int[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

        void OnGUI()
        {
            if (!FfbDriver.ShowDebug) return;
            var sb = new StringBuilder("Axes (winmm, 0..65535)   1=X 2=Y 3=Z 4=R 5=U 6=V");
            int lines = 1;
            for (int id = 0; id < WinJoy.MaxDevices; id++)
            {
                if (!WinJoy.Present(id)) continue;
                sb.Append("\nID " + id + ":");
                lines++;
                string moved = "";
                for (int a = 1; a <= 6; a++)
                {
                    int v = WinJoy.Axis(id, a);
                    int k = id * 7 + a;
                    if (v < wMin[k]) wMin[k] = v;
                    if (v > wMax[k]) wMax[k] = v;
                    sb.Append(" " + WinJoy.AxisNames[a] + "=" + v);
                    if (wMax[k] - wMin[k] > 3000) moved += " " + a + "(" + WinJoy.AxisNames[a] + ") " + wMin[k] + ".." + wMax[k] + ";";
                }
                if (moved.Length > 0) { sb.Append("\n   moved:" + moved); lines++; }
            }
            int h = 12 + 18 * lines;
            GUI.Box(new Rect(320, 10, 560, h), "");
            GUI.Label(new Rect(330, 15, 550, h), sb.ToString());
        }
    }
}
