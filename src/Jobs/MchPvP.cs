using System;
using System.Collections.Generic;
using MCHPvPRotation.Engine;

namespace MCHPvPRotation.Jobs;

/// <summary>
/// 机工士（MCH）战场 PvP 循环。
///
/// 技能 ID 全部来自游戏数据表导出（Action.IsPvP=true + 职业=机工）：
///
///   GCD/连击
///     29402 蓄力冲击    2.5s   （热冲击的后续）
///     29403 热冲击      1.5s   （基础 GCD）
///     41468 烈焰弹      1.5s   （附带伤害）
///     29404 霰弹枪      16s    （大伤害）
///
///   能力技
///     29405 钻头        10s 2层
///     29406 毒菌冲击    10s 2层
///     29407 空气锚      10s 2层
///     29408 回转飞锯    10s 2层
///     29415 魔弹射手    10s
///     29409 野火        24s   （核心爆发）
///     29412 象式浮空炮塔 30s
///     29414 分析        20s 2层
///     41469 全金属爆发   30s   （LB）
///     41470 起爆        1s
///
/// 通用 PvP 动作（所有职业都有）
///     29711 防御（Guard）
///     29712 全力恢复（Recuperate）
/// </summary>
public static class MchPvP
{
    // ---------------------------------------------------------------- 技能 ID
    public const uint 热冲击 = 29403;
    public const uint 蓄力冲击 = 29402;
    public const uint 霰弹枪 = 29404;
    public const uint 钻头 = 29405;
    public const uint 毒菌冲击 = 29406;
    public const uint 空气锚 = 29407;
    public const uint 回转飞锯 = 29408;
    public const uint 野火 = 29409;
    public const uint 象式浮空炮塔 = 29412;
    public const uint 分析 = 29414;
    public const uint 魔弹射手 = 29415;
    public const uint 全金属爆发 = 41469;
    public const uint 起爆 = 41470;
    public const uint 烈焰弹 = 41468;

    // 通用 PvP 动作
    public const uint 防御 = 29711;
    public const uint 全力恢复 = 29712;

    // 状态 ID
    public const uint 状态_野火 = 1323;     // 野火挂在目标身上
    public const uint 状态_分析 = 2558;     // 分析强化（数值待实测校正）

    /// <summary>构建机工的槽列表（按优先级排序）。</summary>
    public static List<IRotationSlot> BuildSlots(PvPConfig config) => new()
    {
        new SurvivalSlot(),                    // 0  保命最高优先
        new WildfireSlot(config),              // 10 爆发
        new LbSlot(config),                    // 20 LB 斩杀
        new TurretSlot(config),                // 30 炮塔
        new AnalysisSlot(config),              // 40 分析
        new ShotgunSlot(),                     // 50 霰弹枪
        new DrillSlot(),                       // 60 核心能力技
        new GcdSlot(),                         // 90 GCD 填充
    };

    // ================================================================== 保命
    private sealed class SurvivalSlot : IRotationSlot
    {
        public string Name => "保命";
        public int Priority => 0;

        public bool Check(PvPContext ctx) => ctx.InPvP && ctx.HpPercent < 0.45f;

        public void Build(Slot slot, PvPContext ctx)
        {
            // 血少先恢复，再考虑防御
            if (ctx.HpPercent < 0.30f) slot.AddAbility(全力恢复);
            else slot.AddAbility(防御);
        }
    }

    // ================================================================== 野火
    private sealed class WildfireSlot : IRotationSlot
    {
        private readonly PvPConfig _config;
        public WildfireSlot(PvPConfig config) => _config = config;

        public string Name => "野火";
        public int Priority => 10;

        public bool Check(PvPContext ctx)
            => _config.MchUseWildfire
               && ctx.HasHostileTarget
               && ctx.TargetHpPercent > 0.25f
               && ctx.TargetDistance <= 25f
               && ctx.GetCooldown(野火) <= 0.05f;

        public void Build(Slot slot, PvPContext ctx)
        {
            // 野火 → 立刻接分析强化，再打一轮能力技把伤害灌进去
            slot.AddAbility(野火);
            if (_config.MchUseAnalysis && ctx.GetCooldown(分析) <= 0.05f)
                slot.AddAbility(分析);
            if (ctx.GetCooldown(空气锚) <= 0.05f) slot.AddAbility(空气锚);
            if (ctx.GetCooldown(回转飞锯) <= 0.05f) slot.AddAbility(回转飞锯);
            if (ctx.GetCooldown(钻头) <= 0.05f) slot.AddAbility(钻头);
        }
    }

    // ================================================================== LB
    private sealed class LbSlot : IRotationSlot
    {
        private readonly PvPConfig _config;
        public LbSlot(PvPConfig config) => _config = config;

        public string Name => "全金属爆发";
        public int Priority => 20;

        public bool Check(PvPContext ctx)
            => _config.MchUseLb
               && ctx.HasHostileTarget
               && ctx.TargetHpPercent <= _config.MchLbHpThreshold
               && ctx.TargetDistance <= 25f
               && ctx.GetCooldown(全金属爆发) <= 0.05f;

        public void Build(Slot slot, PvPContext ctx) => slot.AddAbility(全金属爆发);
    }

    // ================================================================== 炮塔
    private sealed class TurretSlot : IRotationSlot
    {
        private readonly PvPConfig _config;
        public TurretSlot(PvPConfig config) => _config = config;

        public string Name => "象式浮空炮塔";
        public int Priority => 30;

        public bool Check(PvPContext ctx)
            => _config.MchUseTurret
               && ctx.InCombat
               && ctx.HasHostileTarget
               && ctx.GetCooldown(象式浮空炮塔) <= 0.05f;

        // 象式浮空炮塔是【地面目标技能】：必须走 UseActionLocation，
        // 用普通 UseAction 会让游戏进入「选择地面」模式，把循环卡死。
        public void Build(Slot slot, PvPContext ctx) => slot.AddGroundAbility(象式浮空炮塔);
    }

    // ================================================================== 分析
    private sealed class AnalysisSlot : IRotationSlot
    {
        private readonly PvPConfig _config;
        public AnalysisSlot(PvPConfig config) => _config = config;

        public string Name => "分析";
        public int Priority => 40;

        public bool Check(PvPContext ctx)
            => _config.MchUseAnalysis
               && ctx.HasHostileTarget
               && ctx.GetCooldown(分析) <= 0.05f
               && !ctx.HasStatus(状态_分析);

        public void Build(Slot slot, PvPContext ctx) => slot.AddAbility(分析);
    }

    // ================================================================== 霰弹枪
    private sealed class ShotgunSlot : IRotationSlot
    {
        public string Name => "霰弹枪";
        public int Priority => 50;

        public bool Check(PvPContext ctx)
            => ctx.HasHostileTarget
               && ctx.TargetDistance <= 12f
               && ctx.GetCooldown(霰弹枪) <= 0.05f;

        public void Build(Slot slot, PvPContext ctx) => slot.Add(霰弹枪);
    }

    // ================================================================== 核心能力技
    private sealed class DrillSlot : IRotationSlot
    {
        public string Name => "钻头/毒菌/空气锚/回转";
        public int Priority => 60;

        public bool Check(PvPContext ctx)
            => ctx.HasHostileTarget
               && (ctx.GetCooldown(钻头) <= 0.05f
                   || ctx.GetCooldown(空气锚) <= 0.05f
                   || ctx.GetCooldown(回转飞锯) <= 0.05f
                   || ctx.GetCooldown(毒菌冲击) <= 0.05f);

        public void Build(Slot slot, PvPContext ctx)
        {
            // 优先级：钻头 > 空气锚 > 回转飞锯 > 毒菌冲击
            if (ctx.GetCooldown(钻头) <= 0.05f) slot.AddAbility(钻头);
            if (ctx.GetCooldown(空气锚) <= 0.05f) slot.AddAbility(空气锚);
            if (ctx.GetCooldown(回转飞锯) <= 0.05f) slot.AddAbility(回转飞锯);
            if (ctx.GetCooldown(毒菌冲击) <= 0.05f) slot.AddAbility(毒菌冲击);
            if (ctx.GetCooldown(魔弹射手) <= 0.05f) slot.AddAbility(魔弹射手);
        }
    }

    // ================================================================== GCD 填充
    private sealed class GcdSlot : IRotationSlot
    {
        public string Name => "GCD";
        public int Priority => 90;

        public bool Check(PvPContext ctx)
            => ctx.HasHostileTarget && ctx.TargetDistance <= 25f;

        public void Build(Slot slot, PvPContext ctx)
        {
            // PvP 里热冲击是基础 GCD；蓄力冲击在连击窗口内可用。
            // 这里先简单做：能用蓄力冲击就打，否则热冲击。
            if (ctx.GetCooldown(蓄力冲击) <= 0.05f)
                slot.Add(蓄力冲击);
            else
                slot.Add(热冲击);
        }
    }
}
