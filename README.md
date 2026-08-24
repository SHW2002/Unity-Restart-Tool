# Unity Restart Tool

面向 Windows 的 Unity / 团结编辑器安全重启工具。程序会自动发现具有主窗口与有效项目路径的编辑器实例，排除 AssetImportWorker 等后台进程，并提供手动批量重启、每日定时、项目级计划、托盘驻留和运行日志。

## 安全规则

每次重启前，项目内的 Editor companion 会完成以下检查：

- 编辑器不处于 Play、编译、导入或构建状态。
- 场景、Prefab Stage 与已加载的项目资源没有未保存修改；持久化资源会比较内存与磁盘序列化内容，避免插件重复标记 Dirty 造成误报。
- 清空 Console 后等待 3 秒，期间没有重新出现 error、warning 或普通 log。
- 退出前再次执行相同门禁，状态变化时拒绝退出。

任何检查无法完成时都会跳过该实例。工具不会强制结束 Unity 或团结进程。

## 使用

1. 启动程序并在实例列表中选择项目。
2. 首次使用某个项目时，点击“安装 / 升级”安装 Editor companion。
3. 等待 Companion 状态变为“就绪”，然后点击“立即重启”。
4. 如需每日计划，勾选项目的“纳入定时”，设置时间并启用每日定时。

companion 会复制到项目的 `LocalPackages/com.shw.unity-restart-companion`，并在 `Packages/manifest.json` 中加入相对 `file:` 引用。安装器会生成完整性记录；检测到用户修改时拒绝覆盖或删除。旧版 `com.wepie.unity-restart-companion` 安装会在升级时自动迁移。

桌面程序要求 Companion 1.0.1 或更高版本；旧版心跳会显示为版本不兼容，安装 / 升级并在编辑器中完成资源刷新后才能重启。

`Window-Title-Renamer` 2.0.0 或更高版本运行时，本工具会通过当前用户命名管道迁移“持续保持”标题规则。GUI 会持续显示标题工具的运行版本与协议兼容状态；标题工具不可用时仍执行重启，但不恢复标题。

## 布局与任务栏边界

工具会保存并恢复顶层窗口的位置、大小和最大化状态。Unity 内部面板布局依赖编辑器正常退出后自行恢复。

Windows 没有受支持的普通窗口任务栏精确排序 API。工具采用逆序关闭、原顺序启动来尽力保持 Unity 实例之间的相对顺序，但不能在所有任务栏配置下保证绝对一致。

## 构建与发布

```powershell
dotnet build
dotnet test tests/UnityRestartTool.Tests.csproj
dotnet publish -c Release -r win-x64
```

每次发布会自动在 `bin/Release-Archives/` 生成 `Unity-Restart-Tool-win-self-contained-yymmdd-hhmmss.zip`。
