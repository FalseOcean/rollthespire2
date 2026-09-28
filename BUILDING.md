# Build from source / 从源码构建

Requires Windows, .NET 9 SDK (or a compatible newer SDK), and a local Slay the Spire 2 0.111.0 installation. The project targets .NET 9 and uses Godot.NET.Sdk 4.5.1, restored through NuGet. Game assemblies are not distributed here.

需要 Windows、.NET 9 SDK（或兼容的新版本 SDK）及本地游戏 0.111.0。依赖由 NuGet 还原；游戏程序集需由自己的游戏安装提供。

Run from this directory, with game deployment disabled:

```powershell
dotnet build -c Release -p:Rt2ModsDir= -m:1
```

The project discovers the installed game automatically. If discovery fails, specify the directory containing `sts2.dll` and `0Harmony.dll`:

```powershell
dotnet build -c Release -p:Rt2ModsDir= -m:1 "-p:Sts2ReferenceDir=D:\path\to\data_sts2_windows_x86_64"
```

Output: `.godot/mono/temp/bin/Release/RolltheSpire2.dll`. Copy that DLL and the root `RolltheSpire2.json` into `<game>/mods/RolltheSpire2/`. No PCK is required. Do not redistribute the game DLLs or copy them into the mod package.

输出 DLL 与根目录的 `RolltheSpire2.json` 一起放入游戏的 `mods/RolltheSpire2/`。不需要 PCK，也不要将游戏 DLL 放入安装包。

Without `-p:Rt2ModsDir=`, the existing build can automatically deploy to the discovered game mod directory. Keep this option for verification builds. A successful build does not establish in-game compatibility with newer game versions.

省略 `-p:Rt2ModsDir=` 时，构建可能自动部署到游戏目录。仅验证编译时请保留该参数。编译成功不代表已经通过游戏运行验证。

This public snapshot includes production source and embedded runtime resources. Private development history, internal research, raw experiments and test tools are not part of this distribution. Embedded encyclopedia/developer-note texts and map probability tables, where present, are runtime resources and must be retained.
