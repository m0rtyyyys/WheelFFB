# Wheel FFB — мод для My Winter Car (MSCLoader)

Нормальный force feedback для DD-рулей (проверено на Moza R3), тугой руль на месте,
педали на всём ходу и аналоговое сцепление через winmm, кнопки руля выше 20-й через DirectInput.

## Ветки
- `main` — релиз: только то, что нужно игроку, без горячих клавиш.
- `dev` — `main` + инструменты отладки в `devtools/` и их горячие клавиши (`DevHooks.cs`).

Новые фичи делаются в `dev`; в `main` переносятся через `git merge dev`, кроме коммитов с инструментами
(или через `git cherry-pick`).

## Сборка
```
dotnet build -c Release
```
Проект (`WheelFFB.csproj`) ссылается на `UnityEngine.dll`, `MSCLoader.dll`, `PlayMaker.dll`, `Assembly-CSharp.dll`
из `mywintercar_Data\Managed`.

## Настройка
Mods → Wheel FFB → Settings. Окно настройки/отладки — галочка «Show setup/debug overlay».
Кнопки руля → клавиши: файл `wheel_buttons.txt` в папке игры (перечитывается сам после сохранения).
