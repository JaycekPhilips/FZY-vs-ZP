# 对战模式与 AI 修改说明

游戏已在主菜单的“开始”按钮后加入模式选择：双人对战、操控范志毅挑战赵鹏 AI、操控赵鹏挑战范志毅 AI。重新开始会保持当前模式；返回主菜单后可以重新选择。

AI 每帧根据球的速度和重力预判截球位置，从己方球门一侧靠近，并按球的高度、距离和动作冷却决定跳跃、踢球、头球。它使用游戏原有的移动和动画逻辑，没有直接移动足球或修改比分。

近身争球时，AI 会识别对手也在球旁，持续向进攻方向施压。踢球和头球分别按脚、头的实际位置判断；空中夹球会更积极地跳起争顶。AI 双方的额外推力保持一致。

开球或球重置后，AI 会先在本方侧接应高空落球，并提前刹车。球第一次落到较低位置、被击向一侧，或对手进入近身争球时，AI 再向球靠近；近身争球之外不使用额外推力。

选择模式后，主菜单和选择层会关闭，再进入对战场景。球碰撞音量也会遵守游戏设置中的“音效”滑块；设为 0 时球声静音。

## 文件

- `GameAIMod.cs`：模式菜单与双方 AI 的源码。
- `PatchGame.cs`：将菜单和输入调用接入原游戏程序集的源码。
- `Assembly-CSharp.original.dll`：修改前的原版程序集备份。
- `Assembly-CSharp.patched.dll`：已安装到 `FanZhiYi_Data/Managed` 的修改版副本。

运行时还需要 `FanZhiYi_Data/Managed/GameAIMod.dll`。要恢复原版，把 `Assembly-CSharp.original.dll` 复制回 `FanZhiYi_Data/Managed/Assembly-CSharp.dll`，并移除同目录的 `GameAIMod.dll`。

编译源码可使用 Windows .NET Framework C# 编译器。`PatchGame.cs` 依赖 NuGet 的 Mono.Cecil 0.11.6；游戏运行不需要 Mono.Cecil。
