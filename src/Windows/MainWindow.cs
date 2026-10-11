using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using MCHPvPRotation.Engine;

namespace MCHPvPRotation.Windows;

/// <summary>主界面：总开关、状态显示、职业选项、调试。</summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly PvPPlugin _plugin;
    private readonly RotationEngine _engine;
    private readonly PvPConfig _config;

    public MainWindow(PvPPlugin plugin, RotationEngine engine, PvPConfig config)
        : base("PvP 自动循环###MCHPvPRotationMain")
    {
        _plugin = plugin;
        _engine = engine;
        _config = config;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 300),
            MaximumSize = new Vector2(900, 900),
        };
    }

    public override void Draw()
    {
        try
        {
            DrawHeader();
            ImGui.Separator();
            DrawStatus();
            ImGui.Separator();
            DrawJobOptions();
            ImGui.Separator();
            DrawDebug();
        }
        catch (Exception ex)
        {
            Service.PluginLog.Error(ex, "PvP 界面绘制失败");
            ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f), "界面绘制出错，详见日志。");
        }
    }

    private void DrawHeader()
    {
        var enabled = _config.Enabled;
        if (ImGui.Checkbox("启用自动循环", ref enabled))
        {
            _config.Enabled = enabled;
            _config.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("重新装载循环"))
            _plugin.ApplyJob();
    }

    private void DrawStatus()
    {
        ImGui.TextColored(Accent, "状态");

        var inPvp = Service.ClientState.IsPvP;
        ImGui.TextUnformatted($"PvP 地图: {(inPvp ? "是" : "否")}");

        var player = Service.ObjectTable.LocalPlayer;
        ImGui.TextUnformatted($"职业: {player?.ClassJob.Value.Abbreviation.ToString() ?? "-"}");
        ImGui.TextUnformatted($"槽数量: {_engine.SlotCount}");
        ImGui.TextUnformatted($"最近动作: {(_engine.LastAction.Length == 0 ? "-" : _engine.LastAction)}");
    }

    private void DrawJobOptions()
    {
        ImGui.TextColored(Accent, "机工选项");

        Check("野火爆发", _config.MchUseWildfire, v => _config.MchUseWildfire = v);
        Check("全金属爆发 (LB)", _config.MchUseLb, v => _config.MchUseLb = v);

        var lbHp = _config.MchLbHpThreshold * 100f;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("LB 血量阈值 (%)", ref lbHp, 5f, 100f, "%.0f"))
            Save(() => _config.MchLbHpThreshold = lbHp / 100f);

        Check("象式浮空炮塔", _config.MchUseTurret, v => _config.MchUseTurret = v);
        Check("分析", _config.MchUseAnalysis, v => _config.MchUseAnalysis = v);

        ImGui.Separator();
        ImGui.TextColored(Accent, "目标过滤");
        Check("避开有「地天」的武士", _config.AvoidChiten, v => _config.AvoidChiten = v);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("武士的必杀剑·地天（状态 1240）= 减伤 + 反击伤害。\n开启后只要目标身上有它，就不对他出手。");
    }

    private void DrawDebug()
    {
        ImGui.TextColored(Warn, "调试");
        Check("调试日志（写 pvp.log）", _config.DebugLog, v => _config.DebugLog = v);
        Check("忽略冷却检查（危险）", _config.IgnoreCooldown, v => _config.IgnoreCooldown = v);
        ImGui.TextWrapped("日志位置：插件配置目录 \\PvPPlugin\\pvp.log");
    }

    // ------------------------------------------------------------------ 小工具
    private static readonly Vector4 Accent = new(0.55f, 0.78f, 0.95f, 1f);
    private static readonly Vector4 Warn = new(0.95f, 0.75f, 0.35f, 1f);

    private void Check(string label, bool current, Action<bool> setter)
    {
        var v = current;
        if (ImGui.Checkbox(label, ref v))
            Save(() => setter(v));
    }

    private void Save(Action apply)
    {
        apply();
        _config.Save();
    }

    public void Dispose() { }
}
