# Mojo 萝卜桌宠 · Mayday Bobo

独立运行的 Windows 绒毛萝卜桌宠。**完全离线，无需 ChatGPT、Codex、账号或 API Key。** 全部图片嵌入程序。

![招手预览](docs/images/waving.gif)

## 下载与安装

- **[下载安装包 v1.0.0](https://github.com/Charliesmzsm/mayday_bobo/releases/download/v1.0.0/MojoCarrot-Setup-1.0.0.exe)**：推荐，双击安装，含快捷方式与卸载入口。
- **[下载便携版 v1.0.0](https://github.com/Charliesmzsm/mayday_bobo/releases/download/v1.0.0/MojoCarrot-Portable-1.0.0.zip)**：解压后双击 MojoCarrot.exe。
- [下载完整源码 v1.0.0](https://github.com/Charliesmzsm/mayday_bobo/releases/download/v1.0.0/MojoCarrot-Source-1.0.0.zip)：包含 C# 代码、图片素材、编译脚本和文档，可离线编译。
- [全部版本](https://github.com/Charliesmzsm/mayday_bobo/releases) · [SHA256 校验文件](https://github.com/Charliesmzsm/mayday_bobo/releases/download/v1.0.0/SHA256SUMS.txt)

面向 Windows 10 / 11，需要 .NET Framework 4.8 系统组件；无需 Python、Node.js 或开发工具。当前版本在 Windows 11 实机验证。

1. 下载并运行安装包。
2. 选择桌面快捷方式、安装后启动选项。
3. 点击“安装”，仅安装至当前用户，无需管理员权限。
4. 右键桌宠或托盘图标打开菜单。

安装包尚未数字签名，Windows 可能提示“未知发布者”或 SmartScreen。请核对来源和 SHA256，不需要关闭系统防护。

```powershell
Get-FileHash .\MojoCarrot-Setup-1.0.0.exe -Algorithm SHA256
```

![安装界面](docs/images/setup.png)

## 源码开发版本 v1.1.2（Release 尚未发布）

本仓库已包含新动作系统和用户提供的惊喜 v2 修整素材。**公开安装包下载仍为上面的 v1.0.0**，新版安装器可从源码离线构建；不将源码提交视为新版 Release 发布。

- 12种动作390帧，加16个注视姿势。星星眼惊喜使用主交付v2，48帧/2秒，修整嘴形、嘴舌和星形刺绣；F08/F46的细横线已替换为小椭圆口。
- `alpha_mode`明确区分预乘RGB和直通alpha：12组动作在解码时还原预乘存储，避免透明边缘被重复乘alpha；注视与旧图集保持直通。内嵌PNG原像素不重写，可选去边变体没有混入主交付。
- Stopwatch单调时间轴及可变41/42ms帧时长近似24fps（呼吸12fps）；16ms检查周期不增加或复制素材帧。
- 手动动作、带权随机情绪、微动作与注视分优先级；情绪脸不被中性注视覆盖。拖动即时取消，暂停/隐藏/模拟锁屏恢复不追赶过期队列。
- 抽样前过滤冷却与次数，尽量排除最近两次；动作结束重新等待。安静45–90秒、标准20–45秒、活泼12–25秒。惊喜至少180秒，滚动10分钟最多2次，手动也计数。
- 当前尺寸帧缓存32MiB，图集页16MiB，按需加载与淘汰。单击延迟判定，双击不会先招手，兼容旧版设置缺省字段。
- [v2接入与验收](docs/ART-V2.md) · [美术修整提示词](docs/ART-RETOUCH-PROMPTS.md) · [动作格式](docs/ANIMATION-RUNTIME.md) · [素材来源](docs/ASSET-MANIFEST.json)。

![惊喜v2正常速度预览](docs/images/surprise-v2.gif)

## 操作

- 单击：等待系统双击判定时间后招手；双击：开心跳跃，30% 概率尝试惊喜，受冷却限制。
- 拖动移动并取消动作。右键桌宠或托盘，选择“做个动作”“随机动作频率”或桌宠大小。
- 大小：60%、75%、100%、125%、150%、200%；100% 画布为 192×208。
- “自动情绪与眨眼”关闭后保留轻呼吸；“暂停全部动画”冻结所有帧。默认不散步、不穿透、不登录启动。
- 双击托盘显示桌宠并关闭穿透；“召回到鼠标所在屏幕”可找回桌宠。
- 旧版 settings.xml 可直接读取，缺少新字段时默认标准随机频率。

## 安装位置与卸载

- 程序：%LOCALAPPDATA%\Programs\MojoCarrotDesktop
- 设置：%APPDATA%\MojoCarrotDesktop\settings.xml
- 卸载：Windows 设置 → 应用，或开始菜单的卸载入口。
- 卸载保留设置和程序文件夹内用户额外放入的文件。清除设置前请先退出程序。
- 安装版与便携版共享当前用户设置，同一登录会话只运行一只。
- 便携版若启用登录启动，请保持 EXE 路径不变。
- 卸载器从临时目录运行以移除自身；小型临时副本留待系统清理，不会自动再次运行。

## 源码编译

C#、WinForms、Win32 分层窗口、.NET Framework；无 NuGet 或网络构建依赖。

本仓库就是独立 Windows 桌宠的完整源码，主程序入口位于 [src/Pet.cs](src/Pet.cs)，安装器位于 [src/Setup.cs](src/Setup.cs)。普通用户直接下载上面的安装版或便携版即可使用。

```powershell
git clone https://github.com/Charliesmzsm/mayday_bobo.git
cd mayday_bobo
powershell.exe -NoProfile -File .\build.ps1
```

脚本调用 Windows .NET Framework 的 C# 编译器，优先 Framework64、回退 Framework。若受执行策略限制，请按组织允许的方式执行，不必全局关闭安全策略。

输出位于 build/：MojoCarrot.exe、Uninstall.exe、MojoCarrot-Setup-1.1.2.exe。

```text
src/Pet.cs              程序入口、旧图集兼容、设置、Win32 原生绘制
src/PetForm.cs          交互、菜单、优先级、拖动与注视
src/AnimationModel.cs   JSON 读取、校验与旧图集适配
src/AnimationEngine.cs  单调时间轴、带权调度、点击仲裁与边界移动
src/FrameCache.cs       当前尺寸帧缓存与图集页 LRU
src/MotionTests.cs      可控时钟/种子与资源测试
src/NativeSmoke.cs      原生透明窗口正常速度播放及交互冒烟
src/Setup.cs            安装器与隔离测试
src/Uninstall.cs        卸载入口
src/InstallCommon.cs    安装路径、快捷方式与注册
assets/motion/          实际运行的 JSON 和分页 PNG
assets/sprites.png     保留的 v1.0.0 兼容图集
docs/                  设计原稿、素材清单、运行格式与验收说明
build.ps1              完全离线构建
```

## 验证与限制

运行格式与修改方式见 [动作运行格式](docs/ANIMATION-RUNTIME.md)，素材逐帧来源见 [素材清单](docs/ASSET-MANIFEST.json)。设计原稿 [MOTION-PROPOSAL.json](docs/MOTION-PROPOSAL.json) 是建议规格，运行程序实际读取和校验的是 [assets/motion/animations.json](assets/motion/animations.json)。修改后需要重新构建；不支持未经校验的外部热加载。

```powershell
.\build\MojoCarrot.exe --self-test .\build\motion-test
.\build\MojoCarrot.exe --smoke-test .\build\native-smoke.txt
.\build\MojoCarrot.exe --legacy-smoke-test .\build\legacy-smoke.txt
.\build\MojoCarrot-Setup-1.1.2.exe --test-install .\build\installer-test
```

GUI 子系统 EXE 在 PowerShell 中请使用 `Start-Process -Wait -PassThru` 等待退出并检查 ExitCode。旧自检也包含在新版 `--self-test` 内。`--legacy` 可强制以旧素材启动；新资源缺失或配置无效时自动回退，并写出本机兼容模式说明。

当前 Windows 11 已测试模型、全部资源、原生分层窗口、点击/双击、尺寸、穿透恢复与隔离安装/卸载，详见 [验证说明](docs/VALIDATION.md)。尚未验证其他电脑、ARM64、真实锁屏/解锁与所有多显示器 DPI 组合。惊喜v2已修整薄线嘴与材质；F12–F13/F42–F43舌头色阶、共享叶冠浅边仍可细化。循环运动曲线本次没有改动，完整范围见v2验收记录；没有新增或重复姿势凑帧。

## 隐私与素材

无网络请求、遥测、自动更新、聊天或大模型功能，设置仅保存在本机。

角色素材由项目发起者提供。暂未声明开源许可证；公开源码不等于授权第三方商用或再分发，使用前请联系仓库所有者确认。

问题反馈：[Issues](https://github.com/Charliesmzsm/mayday_bobo/issues)。

