using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace MCHPvPRotation.Engine;

/// <summary>一个待执行的技能槽。</summary>
public sealed class Slot
{
    public readonly List<SlotAction> Actions = new();

    public Slot Add(uint actionId, SlotActionKind kind = SlotActionKind.Gcd)
    {
        Actions.Add(new SlotAction(actionId, kind));
        return this;
    }

    /// <summary>能力技：插在 GCD 之间，不占用 GCD 时间。</summary>
    public Slot AddAbility(uint actionId) => Add(actionId, SlotActionKind.Ability);

    /// <summary>
    /// 地面目标技能（需要选落点，例如机工的「象式浮空炮塔」）。
    /// 必须走 UseActionLocation，用普通 UseAction 会让游戏进入选点模式并卡住循环。
    /// </summary>
    public Slot AddGroundAbility(uint actionId)
    {
        Actions.Add(new SlotAction(actionId, SlotActionKind.GroundAbility));
        return this;
    }

    /// <summary>等待型动作（等 GCD 转好、等状态消失等）。</summary>
    public Slot AddWait(int milliseconds, string reason = "")
    {
        Actions.Add(new SlotAction(0, SlotActionKind.Wait) { WaitMs = milliseconds, Reason = reason });
        return this;
    }

    public bool IsEmpty => Actions.Count == 0;
}

public enum SlotActionKind
{
    /// <summary>战技/魔法，占 GCD。</summary>
    Gcd,
    /// <summary>能力技，不占 GCD。</summary>
    Ability,
    /// <summary>地面目标技能，需要落点坐标。</summary>
    GroundAbility,
    /// <summary>等待。</summary>
    Wait,
}

public sealed class SlotAction
{
    public uint ActionId { get; }
    public SlotActionKind Kind { get; }
    public int WaitMs { get; set; }
    public string Reason { get; set; } = string.Empty;

    public SlotAction(uint actionId, SlotActionKind kind)
    {
        ActionId = actionId;
        Kind = kind;
    }

    public override string ToString()
        => Kind == SlotActionKind.Wait
            ? $"Wait({WaitMs}ms {Reason})"
            : $"{Kind}:{ActionId}";
}

/// <summary>
/// 职业循环接口。照搬 AEAssist 的 ISlotResolver 形状：
/// Check 决定「这个槽现在可不可用」，Build 决定「放什么」。
/// </summary>
public interface IRotationSlot
{
    /// <summary>槽名，用于调试显示。</summary>
    string Name { get; }

    /// <summary>优先级，数字越小越先检查。</summary>
    int Priority { get; }

    /// <summary>返回 true 表示这个槽当前可用，引擎会调用 Build。</summary>
    bool Check(PvPContext ctx);

    /// <summary>把要放的东西塞进 slot。</summary>
    void Build(Slot slot, PvPContext ctx);
}

/// <summary>每帧传给循环的只读上下文。</summary>
public sealed class PvPContext
{
    public bool InPvP { get; init; }
    public bool InCombat { get; init; }
    public bool IsMounted { get; init; }
    public bool IsDead { get; init; }

    public uint JobId { get; init; }
    public uint CurrentHp { get; init; }
    public uint MaxHp { get; init; }
    public float HpPercent => MaxHp == 0 ? 0f : (float)CurrentHp / MaxHp;

    /// <summary>当前目标（可为空）。</summary>
    public ulong TargetId { get; init; }
    public Vector3 TargetPosition { get; init; }
    public float TargetDistance { get; init; }
    public uint TargetHp { get; init; }
    public uint TargetMaxHp { get; init; }
    public float TargetHpPercent => TargetMaxHp == 0 ? 0f : (float)TargetHp / TargetMaxHp;

    /// <summary>玩家自己当前位置（地面技能落点兜底用）。</summary>
    public Vector3 PlayerPosition { get; init; }

    /// <summary>
    /// 动画锁剩余秒数。放完技能客户端会锁定约 0.6 秒；
    /// 这期间再发动作请求会被拒或打断当前动作（表现为角色抽搐），必须等它归零。
    /// </summary>
    public float AnimationLock { get; init; }

    /// <summary>是否正在读条。</summary>
    public bool IsCasting { get; init; }

    /// <summary>
    /// 查询目标身上是否有某个状态。
    /// 用于「目标开了某个反伤/免伤状态时不要打他」这类判断。
    /// </summary>
    public Func<uint, bool> TargetHasStatus { get; init; } = _ => false;

    /// <summary>
    /// 目标是否有「不该打」的状态（例如武士的必杀剑·地天 1240，会反击伤害）。
    /// true = 应跳过该目标。
    /// </summary>
    public Func<bool> TargetIsProtected { get; init; } = () => false;

    /// <summary>是否有合法敌对目标。</summary>
    public bool HasHostileTarget { get; init; }

    /// <summary>GCD 剩余时间（秒）。</summary>
    public float GcdRemain { get; init; }

    /// <summary>动作剩余冷却查询：actionId -> 剩余秒数。</summary>
    public Func<uint, float> GetCooldown { get; init; } = _ => 0f;

    /// <summary>身上是否有某个状态。</summary>
    public Func<uint, bool> HasStatus { get; init; } = _ => false;

    /// <summary>状态剩余时间（秒）。</summary>
    public Func<uint, float> GetStatusRemain { get; init; } = _ => 0f;

    /// <summary>调试写一行日志。</summary>
    public Action<string> Log { get; init; } = _ => { };
}
