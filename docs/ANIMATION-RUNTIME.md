# v1.2.0 新增网格与位移接口

本版仍使用schema_version=1，新增可选action字段：gaze的grid_rows=9、grid_cols=9以及81帧行优先姿势表；hop_left/right的root_x_logical与frames等长，第一项0，方向符号一致、单调、有限值。错误尺寸、帧数、不一致方向、NaN/Infinity会拒绝。缺省保留旧16向及旧散步兼容路径。来源gaze JSON的fps=24是作者预览信息，姿势表不按该数值顺序播放。

注视从鼠标相对脸部的逻辑位置得到[-1,1]坐标，时间常数50ms的指数平滑，采样四个相邻姿势；只对gaze工作，不混合情绪表情。当前尺寸可复用Bitmap/CPU缓冲计入帧预算。首个样本即响应；输入反向不重启确认等待。停止移动后平滑回中心，拖动/暂停/动作切换复位跟随状态。

蹦走与root曲线共用AnimationPlayer.Elapsed，曲线按16/17ms帧时间线插值并按显示比例缩放。单步32逻辑像素，PNG自带竖直跳高。按完整800ms步数决定结束；末步剩余距离不足时缩短，仍完成落地。暂停恢复取消行走并重新安排等待；不追赶旧位移。--legacy保留旧WalkMotion。

计时检查8ms，活动注视绘制不快于16ms；运行期间成对timeBeginPeriod(1)/timeEndPeriod(1)，所有动画仍按单调绝对时间选帧。检查周期、目标60fps与实测显示帧率必须区分。

以下是历史1.1.x实现说明；16向数量、70ms确认及旧散步仅对应兼容路径，旧版12组预乘标记在1.2.0变为10组，左右新素材与注视为straight。

---

# 动作运行格式与实现

运行时读取嵌入的 `animations.json`（schema_version 1），不是直接解释设计稿 JSON。无网络、无第三方库。

## 数据与校验

`logical_size` 固定 192×208，`target_pivot` 为逻辑锚点。每个 action 有唯一 id、label、loop、durations_ms、weight、cooldown_ms、rate_window_ms/rate_limit、pages、frames。每页声明 resource、width、height；每帧声明 page、rect=[x,y,w,h]、pivot=[x,y]。资源按 actionId 和 frameIndex 访问，不依赖固定行列。分页最多 8 帧，406 个 384×416 原始帧；显示时缩放并对齐锚点 [96,196]，源帧锚点 [192,392]。保留透明安全边，锚点是统一参考点，不将跳跃帧强制贴地。

读取使用 DataContractJsonSerializer；校验 schema、空项/唯一 ID、必需动作、16 个注视方向、页资源/PNG 头/几何、矩形边界、锚点、帧时长、权重、频率限制、等待范围和缓存预算。缺资源或不合规时回退内置 sprites.png，并在设置目录写 animation-fallback.txt、弹托盘提示。`--legacy` 显式使用旧版。原图集的 row/column 运算仅保留在 Legacy 适配器与原有自检。

## 时间与优先级

Stopwatch 单调时钟计算帧位置；41/42ms 的累计时长在 48 帧恰为 2000ms。UI Timer=16ms 只采样时间轴，阻塞时允许跳过过时画面，不将检查次数称为动画帧。每帧支持不同 duration；动作非循环完成事件只发送一次。呼吸 12fps。

暂停/隐藏/锁屏/菜单/鼠标按下冻结时间，恢复平移起点并重新安排随机等待，无待播队列。实际拖动取消当前动作。手动动作可打断随机/微动作；随机/眨眼/打哈欠只在待机进入；注视只覆盖待机，不覆盖表情。注视有角度滞回、70ms 确认和 200ms 恢复缓冲。散步默认关闭，按时间积分并在工作区边缘减速、停止。

单击在系统 DoubleClickTime 到期后才招手；双击取消单击，随机尝试惊喜（受限时转开心跳跃）。丢失鼠标捕获时恢复调度，不永久冻结。

## 随机调度与设置

冷却与滚动窗口过滤先于带权抽样：叶片 34、歪头 24、招手 16、跳跃 12、思考 8、惊喜 6。权重在合格池内重归一。排除最近两次随机动作；池耗尽时仅放宽倒数第二次，绝不立即重复最近一次，无候选则推迟 5–10 秒。惊喜冷却≥180000ms，600000ms 窗口最多 2 次，手动也计入。其他手动动作可重复。

正常随机等待从动作结束后开始；标准 20–45 秒、安静 45–90 秒、活泼 12–25 秒。鼠标静止≥3 秒、手动操作后≥5 秒、避开身体及外扩32逻辑像素；眨眼5–12秒检查，打哈欠90–180秒检查且静止≥120秒、概率0.2。中性注视会占用待机画面，眨眼待鼠标安静后触发。

Settings XML 新增 RandomProfile 字段，初始化 standard，旧文件缺字段兼容。频率、尺寸、自动动作等可保存，暂停不持久化。关闭自动动作保留呼吸，暂停冻结全部动作。

## 内存与原生资源

LRU 帧缓存 32MiB（只存当前显示尺寸，换尺寸立即释放）；解码页缓存 16MiB，按需淘汰。临时裁剪 bitmap/Graphics/ImageAttributes 均 using/dispose；不预缓存所有尺寸、所有帧。Bitmap 本身不一定计入 GDI 句柄，测试同时检查缓存字节和工作集。

Win32 绘制先获取屏幕 DC 和兼容 DC，临时 HBITMAP 在 finally 中恢复旧对象后 DeleteObject，DeleteDC 与 ReleaseDC 成对；Form Dispose 释放图缓存、菜单、托盘、Timer，并注销 SystemEvents。

## 修改与复现

编辑 assets/motion/animations.json 与 PNG 后运行 build.ps1，运行 --self-test 验证嵌入资源。无须 Python/Node；预览网页和美术 QA 的生成工具仅为交付验证使用，不是程序依赖。docs/MOTION-PROPOSAL.json 与 MOTION-DESIGN.md 保留原设计；docs/ASSET-MANIFEST.json 记录逐帧文件名、原始文件/解码像素 SHA256、边界和透明洞审计。


## 分页alpha_mode（v1.1.2）

每个pages项可带`"alpha_mode":"straight"`或`"alpha_mode":"premultiplied"`。缺省/空值仍按straight读取，兼容旧JSON。未知值或同一resource被声明为不同模式会在校验时拒绝。该字段描述PNG里RGB的实际存储方式，不描述最终Win32窗口的位图格式。

本次素材检查表明：12组动作的PNG将RGB乘过alpha后写入，16向注视为标准直通PNG。内嵌素材不重写；FrameCache在解码页时，对premultiplied使用整数四舍五入还原RGB，alpha保持，alpha=0的RGB归零。之后照常裁帧、缩放并交给GDI+的PArgb输出。不得将同一预乘存储页当直通素材再次乘alpha；也不得对注视或旧图集盲目做反预乘。

解码临时Bitmap及LockBits在finally中成对释放；仍仅保留当前尺寸帧LRU与页LRU，不预缓存所有尺寸。新回归测试覆盖半透明颜色、黑底合成、透明/不透明像素、未知mode、共享页冲突和缺省直通兼容。

用户的SURPRISE-V2-SOURCE-TIMING.json是来源记录；程序实际读取assets/motion/animations.json。来源中的loop=true用于素材预览，桌宠惊喜仍是一次性动作；24fps时长转换为累计精确2000ms的41/42ms整数时间表。
