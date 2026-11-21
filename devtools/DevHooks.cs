using UnityEngine;

namespace WheelFFB
{
    // Только в ветке dev: горячие клавиши инструментов отладки.
    //   /        PedalAnimDump  — объекты педалей и их FSM
    //   \        PedalRefDump   — кто ссылается на педали
    //   =        PhysDump       — физика текущей машины
    //   Home     ClutchDump     — FSM сцепления и коробки
    //   ; / '    FogTool        — дамп запотевания / режим без запотевания
    public class DevHooks : MonoBehaviour
    {
        void Update()
        {
            PedalAnimDump.Tick();
            PedalRefDump.Tick();
            PhysDump.Tick();
            ClutchDump.Tick();
            FogTool.Tick();
        }
    }
}
