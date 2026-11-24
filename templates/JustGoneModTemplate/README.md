# MyJustGoneMod (Just Gone SDK Template)

## Build

```powershell
dotnet build MyJustGoneMod.csproj -c Release -p:GameDir="F:\Steam\steamapps\common\Just Go"
```

If you extracted `JustGone-v0.1.0-SDK.zip` and copied this folder elsewhere, also pass `-p:JustGoneSdkDir="<path-to-sdk>\lib\net472"`.

## Install

Copy `bin\Release\MyJustGoneMod.dll` to `<Just Go>\BepInEx\plugins\MyJustGoneMod\MyJustGoneMod.dll` (requires `JustGone.dll` installed in `<Just Go>\BepInEx\plugins\JustGone\`).
