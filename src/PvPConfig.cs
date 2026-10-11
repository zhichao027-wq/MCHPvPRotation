using System;
using System.IO;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace MCHPvPRotation;

/// <summary>插件配置。</summary>
[Serializable]
public sealed class PvPConfig : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>总开关。</summary>
    public bool Enabled { get; set; }

    /// <summary>调试日志（写进日志文件，便于排查）。</summary>
    public bool DebugLog { get; set; }

    /// <summary>忽略冷却检查（危险：会狂按技能，只用于排查）。</summary>
    public bool IgnoreCooldown { get; set; }

    // ------------------------------------------------------------ 机工 (MCH) 选项
    /// <summary>启用「野火」爆发。</summary>
    public bool MchUseWildfire { get; set; } = true;

    /// <summary>启用「全金属爆发」(LB)。</summary>
    public bool MchUseLb { get; set; } = true;

    /// <summary>LB 只在目标血量低于这个比例时使用。</summary>
    public float MchLbHpThreshold { get; set; } = 0.35f;

    /// <summary>炮塔「象式浮空炮塔」开关。</summary>
    public bool MchUseTurret { get; set; } = true;

    /// <summary>「分析」强化技开关。</summary>
    public bool MchUseAnalysis { get; set; } = true;

    // ------------------------------------------------------------ 目标过滤
    /// <summary>
    /// 避开开了「必杀剑·地天」(状态 1240) 的武士。
    /// 地天 = 减伤 + 反击伤害，打他等于自己吃亏。
    /// </summary>
    public bool AvoidChiten { get; set; } = true;

    // ------------------------------------------------------------ 日志
    [NonSerialized]
    private static readonly object LogLock = new();

    public void Log(string message)
    {
        try
        {
            var dir = Service.PluginInterface.ConfigDirectory.FullName;
            var path = Path.Combine(dir, "pvp.log");
            lock (LogLock)
            {
                File.AppendAllText(path,
                    $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}",
                    System.Text.Encoding.UTF8);
            }
        }
        catch { }
    }

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
