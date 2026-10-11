using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using MCHPvPRotation.Engine;
using MCHPvPRotation.Jobs;
using MCHPvPRotation.Windows;

namespace MCHPvPRotation;

/// <summary>
/// PvP 自动循环插件（独立实现，不依赖 AEAssist，无需任何验证码）。
///
/// 架构照搬 AEAssist 的 PvP 那套：AcrType.PVP + ISlotResolver(槽) + IRotationEventHandler(钩子)，
/// 但底层直接用 Dalamud 的 ActionManager 执行动作，不使用模拟键盘输入。
/// </summary>
public sealed class PvPPlugin : IDalamudPlugin
{
    private readonly WindowSystem _windowSystem = new("MCHPvPRotation");
    private readonly MainWindow _mainWindow;
    private readonly RotationEngine _engine;

    public PvPConfig Config { get; }
    public string Name => "PvP Auto";

    public PvPPlugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();

        Config = pluginInterface.GetPluginConfig() as PvPConfig ?? new PvPConfig();

        _engine = new RotationEngine(Config);
        _mainWindow = new MainWindow(this, _engine, Config);

        _windowSystem.AddWindow(_mainWindow);
        pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += () => _mainWindow.IsOpen = true;
        pluginInterface.UiBuilder.OpenMainUi += () => _mainWindow.IsOpen = true;

        Service.CommandManager.AddHandler("/pvprot", new CommandInfo(OnCommand)
        {
            HelpMessage = "PvP 自动循环。/pvprot 开界面；/pvprot on|off 总开关；/pvprot job 切换职业循环。",
        });

        ApplyJob();

        Config.Log("插件已加载");
    }

    /// <summary>按当前配置装载职业循环。</summary>
    public void ApplyJob()
    {
        // 目前只实现机工；按本地玩家职业装载（也允许强制）。
        _engine.SetSlots(MchPvP.BuildSlots(Config));
        Config.Log($"已装载机工循环，槽数 {_engine.SlotCount}");
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
                _mainWindow.IsOpen = true;
                break;
            case "on":
                Config.Enabled = true;
                Config.Save();
                Service.ChatGui.Print("[PvP] 自动循环已开启");
                break;
            case "off":
                Config.Enabled = false;
                Config.Save();
                Service.ChatGui.Print("[PvP] 自动循环已关闭");
                break;
            case "job":
                ApplyJob();
                Service.ChatGui.Print("[PvP] 已重新装载循环");
                break;
            default:
                _mainWindow.IsOpen = true;
                break;
        }
    }

    public void Dispose()
    {
        try { Service.CommandManager.RemoveHandler("/pvprot"); } catch { }
        _engine.Dispose();
        _windowSystem.RemoveAllWindows();
        Config.Log("插件已卸载");
    }
}
