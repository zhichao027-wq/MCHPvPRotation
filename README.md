# 机工 PvP 循环（MCHPvPRotation）

为**机工士（MCH）**提供战场 PvP 的自动循环。

## ⚠️ 风险声明（务必先读）

**本插件属于战斗自动化，可能违反游戏服务条款。**

- 自动战斗是 Square Enix 严厉打击的行为，**使用存在封号风险**
- PvP 场景尤其敏感，因为会影响其他玩家的体验
- 作者不对使用本插件造成的任何后果负责

**请自行判断是否使用。** 如果你不愿意承担这个风险，请不要安装。

## 功能

| 槽位 | 内容 |
| --- | --- |
| 保命 | 血量低于阈值时使用「全力恢复」/「防御」 |
| 野火 | 野火爆发窗口：野火 → 分析 → 空气锚 → 回转飞锯 → 钻头 |
| LB | 目标血量低于阈值时使用「全金属爆发」 |
| 炮塔 | 召唤「象式浮空炮塔」（地面目标技能） |
| 分析 | 补充「分析」增益 |
| 霰弹枪 | 近距离使用 |
| 核心技 | 钻头 / 空气锚 / 回转飞锯 / 毒菌冲击 / 魔弹射手 |
| 目标过滤 | 自动避开开着「必杀剑·地天」的目标 |

## 安装

1. 在卫月的插件安装器里添加第三方库：
   ```
   https://raw.githubusercontent.com/zhichao027-wq/MCHPvPRotation/main/pluginmaster.json
   ```
2. 搜索「机工 PvP 循环」并安装
3. `/pvprot` 打开界面

## 指令

| 指令 | 作用 |
| --- | --- |
| `/pvprot` | 打开界面 |
| `/pvprot on` | 开启自动循环 |
| `/pvprot off` | 关闭自动循环 |
| `/pvprot job` | 重新装载循环 |

## 实现说明

本插件**完全独立实现**，只使用 Dalamud 提供的接口：

- 动作执行：`ActionManager.UseAction` / `UseActionLocation`
- 技能数据：从游戏数据表导出后内置于代码
- **不使用模拟键盘输入**

架构参考了 AEAssist 的 PvP 循环设计（槽位 + 优先级），但底层完全自主实现，**不依赖任何需要验证码的运行时**。

## 构建

需要 .NET SDK 与 Dalamud 开发环境：

```powershell
# 设置 Dalamud 目录（国服路径）
$env:DALAMUD_HOME = "$env:APPDATA\XIVLauncherCN\addon\Hooks\dev"

# 构建 + 打包（输出 dist\latest.zip）
.\tools\Build-Release.ps1

# 顺便升版本号
.\tools\Build-Release.ps1 -BumpVersion
```

## 发布流程

1. `.\tools\Build-Release.ps1 -BumpVersion`
2. 把 `dist\latest.zip` 上传到 GitHub Release（tag 用版本号，如 `1.0.0.1`）
3. 提交并推送 `pluginmaster.json`（脚本已自动更新版本与时间戳）

## 目录结构

```
MCHPvPRotation/
├── pluginmaster.json          第三方库清单（卫月读取这个）
├── src/                       插件源码
│   ├── Engine/                循环引擎（调度、槽、动作执行）
│   ├── Jobs/                  职业循环（目前只有机工）
│   ├── Windows/               界面
│   └── MCHPvPRotation.csproj
├── tools/Build-Release.ps1    构建打包脚本
└── dist/latest.zip            发布产物（构建后生成）
```

## 后续计划

- 更多职业的 PvP 循环（诗人、武士、暗黑骑士等）
- 目标选择逻辑优化
