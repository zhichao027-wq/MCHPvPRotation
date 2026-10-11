using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace MCHPvPRotation.Engine;

/// <summary>
/// PvP 循环引擎：每帧构建上下文 → 按优先级问各职业循环的槽 → 执行第一个命中的槽。
///
/// 设计照搬 AEAssist 的 AILoop_PVP + ISlotResolver + Slot 那套思路，
/// 但底层直接用 Dalamud / ActionManager，不依赖 AEAssist，也不需要任何验证码。
/// </summary>
public sealed unsafe class RotationEngine : IDisposable
{
    private readonly PvPConfig _config;
    private readonly List<IRotationSlot> _slots = new();

    private long _lastExecuteTick;
    private long _lastGcdTick;
    private string _lastActionDesc = string.Empty;

    /// <summary>失败退避：actionId -> 下次允许重试的时间戳（毫秒）。</summary>
    private readonly Dictionary<uint, long> _failBackoff = new();

    /// <summary>同一动作失败后，至少等这么久才允许再试，避免请求洪水导致角色抽搐。</summary>
    private const int FailBackoffMs = 250;

    /// <summary>两次动作之间至少间隔。PvP 战技 GCD 约 2.5s，这里取小值，
    /// 真正的节流靠冷却检查 + 失败退避。</summary>
    private const int MinActionIntervalMs = 150;

    public RotationEngine(PvPConfig config)
    {
        _config = config;
        Service.Framework.Update += OnUpdate;
    }

    /// <summary>最近一次执行的动作描述（给界面显示）。</summary>
    public string LastAction => _lastActionDesc;

    /// <summary>当前注册的槽数量。</summary>
    public int SlotCount => _slots.Count;

    public void SetSlots(IEnumerable<IRotationSlot> slots)
    {
        _slots.Clear();
        _slots.AddRange(slots.OrderBy(s => s.Priority));
    }

    public void Dispose()
    {
        try { Service.Framework.Update -= OnUpdate; } catch { }
    }

    private void OnUpdate(IFramework framework)
    {
        if (!_config.Enabled) return;
        if (_slots.Count == 0) return;

        try
        {
            if (!Service.ClientState.IsPvP) return;

            var ctx = BuildContext();
            if (ctx == null || ctx.IsDead) return;

            // 节流：动作之间至少间隔一点，避免同一帧猛点。
            var now = Environment.TickCount64;
            if (now - _lastExecuteTick < MinActionIntervalMs) return;

            foreach (var slot in _slots)
            {
                if (!SafeCheck(slot, ctx)) continue;

                var s = new Slot();
                try { slot.Build(s, ctx); }
                catch (Exception ex) { ctx.Log($"[{slot.Name}] Build 异常: {ex.Message}"); continue; }

                if (s.IsEmpty) continue;

                if (TryExecute(s, ctx))
                {
                    _lastExecuteTick = now;
                    var amForDump = ActionManager.Instance();
                    if (amForDump != null) DumpCooldowns(amForDump, ctx);
                    return;   // 一帧只做一个槽
                }
            }
        }
        catch (Exception ex)
        {
            _config.Log($"引擎异常: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 低频把关键技能的冷却查询结果写进日志（每 3 秒一次）。
    /// 用途：确认 GetRecastTime 对 PvP 技能到底有没有返回值 ——
    /// 如果一直是 0，说明冷却检查是失效的，节流只能靠失败退避。
    /// </summary>
    private long _lastCooldownDumpTick;

    private void DumpCooldowns(ActionManager* am, PvPContext ctx)
    {
        if (!_config.DebugLog) return;

        var now = Environment.TickCount64;
        if (now - _lastCooldownDumpTick < 3000) return;
        _lastCooldownDumpTick = now;

        var ids = new uint[] { 29403, 29402, 29404, 29405, 29407, 29408, 29409, 29412, 29414, 29415, 41469 };
        var parts = new List<string>();
        foreach (var id in ids)
        {
            try
            {
                var total = am->GetRecastTime(ActionType.Action, id);
                var elapsed = am->GetRecastTimeElapsed(ActionType.Action, id);
                parts.Add($"{id}:{total:F1}/{elapsed:F1}");
            }
            catch { parts.Add($"{id}:err"); }
        }

        ctx.Log($"冷却快照(总/已过秒) {string.Join(" ", parts)}");
        ctx.Log($"位置 自身=({ctx.PlayerPosition.X:F1},{ctx.PlayerPosition.Y:F1},{ctx.PlayerPosition.Z:F1}) " +
                $"目标=({ctx.TargetPosition.X:F1},{ctx.TargetPosition.Y:F1},{ctx.TargetPosition.Z:F1}) 距离={ctx.TargetDistance:F1}");
    }

    private static bool SafeCheck(IRotationSlot slot, PvPContext ctx)
    {
        try { return slot.Check(ctx); }
        catch { return false; }
    }

    /// <summary>执行一个槽里的动作；返回是否真的动作了。</summary>
    private bool TryExecute(Slot slot, PvPContext ctx)
    {
        // 先算出这个槽里 GCD 类动作的最短剩余冷却，用于节流：
        // GCD 没好就不用再试这一槽的 GCD 动作了。
        var gcdRemain = 0f;
        foreach (var a in slot.Actions)
        {
            if (a.Kind != SlotActionKind.Gcd) continue;
            var r = ctx.GetCooldown(a.ActionId);
            if (gcdRemain <= 0f || (r > 0f && r < gcdRemain)) gcdRemain = r;
        }

        foreach (var action in slot.Actions)
        {
            if (action.Kind == SlotActionKind.Wait)
                return false;   // 等待 = 本帧不做任何事

            if (action.Kind == SlotActionKind.Gcd)
            {
                // GCD 类：自己或同一槽里任一 GCD 还在转，就整槽跳过。
                if (gcdRemain > 0.05f) continue;
            }

            if (!CanUse(action, ctx))
                continue;

            if (DoAction(action, ctx))
            {
                _lastActionDesc = $"{action.ActionId}";
                _failBackoff.Remove(action.ActionId);
                if (_config.DebugLog)
                    _config.Log($"执行 {action.Kind} {action.ActionId}");
                return true;
            }

            // 失败：记录退避，别在同一秒里反复重试同一个动作。
            // 反复重试会让客户端不断被拒绝、动作被打断，表现为角色抽搐。
            _failBackoff[action.ActionId] = Environment.TickCount64 + FailBackoffMs;
        }

        return false;
    }

    private bool CanUse(SlotAction action, PvPContext ctx)
    {
        if (action.ActionId == 0) return false;

        // 失败退避期内跳过（防止请求洪水）
        if (_failBackoff.TryGetValue(action.ActionId, out var until) && Environment.TickCount64 < until)
            return false;

        // 【关键】动画锁 / 读条中不要出手。
        // 放完技能客户端会进入约 0.6s 动画锁，此时再调 UseAction 会被拒或打断当前动作，
        // GCD 技能表现得最明显 —— 角色抽搐。PvP 里冷却查询常返回 0，所以这一层必须自己判。
        if (action.Kind == SlotActionKind.Gcd)
        {
            if (ctx.AnimationLock > 0.05f) return false;
            if (ctx.IsCasting) return false;
        }

        var remain = ctx.GetCooldown(action.ActionId);
        if (!_config.IgnoreCooldown && remain > 0.05f) return false;
        return true;
    }

    private bool DoAction(SlotAction action, PvPContext ctx)
    {
        var am = ActionManager.Instance();
        if (am == null) return false;

        // 用上下文里的目标 ID（槽的 Check 已经保证是合法敌对目标）
        var targetId = ctx.TargetId;

        bool result;

        if (action.Kind == SlotActionKind.GroundAbility)
        {
            // 地面目标技能必须走 UseActionLocation。
            // 之前用普通 UseAction 会让游戏进入「选择地面」模式，把循环卡住。
            // 落点取目标当前位置；坐标非法时退回自身脚下，避免传零向量。
            var loc = ctx.TargetPosition;
            if (loc == Vector3.Zero) loc = ctx.PlayerPosition;

            result = am->UseActionLocation(
                ActionType.Action,
                action.ActionId,
                targetId,
                &loc,
                0,
                0);
        }
        else
        {
            result = am->UseAction(
                ActionType.Action,
                action.ActionId,
                targetId,
                0,
                ActionManager.UseActionMode.None,
                0);
        }

        if (!result && _config.DebugLog)
            _config.Log($"{(action.Kind == SlotActionKind.GroundAbility ? "UseActionLocation" : "UseAction")}({action.ActionId}) 返回 false，目标={targetId}");

        return result;
    }

    // ------------------------------------------------------------------ 上下文
    /// <summary>目标开着这些状态时不要打（0 = 关闭该条）。
    /// 默认包含武士的「必杀剑·地天」(1240)：减伤 + 反击，打他亏。</summary>
    private static readonly uint[] DefaultBlockedTargetStatuses = { 1240 };

    private PvPContext? BuildContext()
    {
        var player = Service.ObjectTable.LocalPlayer;
        if (player == null) return null;

        var am = ActionManager.Instance();
        if (am == null) return null;

        var target = Service.TargetManager.Target;

        var targetObj = target as Dalamud.Game.ClientState.Objects.Types.IGameObject;
        var targetChara = target as Dalamud.Game.ClientState.Objects.Types.ICharacter;
        var hasHostile = targetChara != null
                         && targetChara.GameObjectId != player.GameObjectId
                         && targetChara.IsTargetable;

        var targetGo = targetChara != null
            ? (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)targetChara.Address
            : null;

        // 目标身上是否挂着「不该打」的状态（地天等）。
        // 命中时 HasHostileTarget 置 false，所有依赖它的伤害槽都会自动跳过。
        var targetProtected = false;
        if (hasHostile && targetGo != null && _config.AvoidChiten)
        {
            foreach (var blocked in DefaultBlockedTargetStatuses)
            {
                if (TargetHasStatus(targetGo, blocked))
                {
                    targetProtected = true;
                    break;
                }
            }
        }

        var effectiveHostile = hasHostile && !targetProtected;

        return new PvPContext
        {
            InPvP = Service.ClientState.IsPvP,
            InCombat = Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat],
            IsMounted = Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted],
            IsDead = player.IsDead,
            JobId = (uint)player.ClassJob.RowId,
            CurrentHp = player.CurrentHp,
            MaxHp = player.MaxHp,
            TargetId = target?.GameObjectId ?? 0,
            TargetPosition = target?.Position ?? Vector3.Zero,
            PlayerPosition = player.Position,
            TargetDistance = target != null ? Vector3.Distance(player.Position, target.Position) : 999f,
            TargetHp = targetChara?.CurrentHp ?? 0,
            TargetMaxHp = targetChara?.MaxHp ?? 0,
            HasHostileTarget = effectiveHostile,
            TargetIsProtected = () => targetProtected,
            TargetHasStatus = id => targetGo != null && TargetHasStatus(targetGo, id),
            GcdRemain = GetGcdRemain(am),
            AnimationLock = am->AnimationLock,
            IsCasting = Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Casting],
            GetCooldown = id => GetActionCooldown(am, id),
            HasStatus = HasStatus,
            GetStatusRemain = GetStatusRemain,
            Log = _config.Log,
        };
    }

    /// <summary>GCD 剩余：用 ActionManager 的全局冷却字段估算。</summary>
    private static float GetGcdRemain(ActionManager* am)
    {
        try
        {
            // ActionManager 没有直接暴露 GCD，这里用「最近一次动作后的固定节流」代替：
            // 真正的节流由 UseAction 自己拒绝未就绪的动作来兜底。
            return 0f;
        }
        catch { return 0f; }
    }

    /// <summary>动作剩余冷却（秒）。0 表示可用。</summary>
    private static float GetActionCooldown(ActionManager* am, uint actionId)
    {
        try
        {
            var total = am->GetRecastTime(ActionType.Action, actionId);
            if (total <= 0f) return 0f;

            var elapsed = am->GetRecastTimeElapsed(ActionType.Action, actionId);
            var remain = total - elapsed;
            return remain > 0f ? remain : 0f;
        }
        catch { return 0f; }
    }

    // ------------------------------------------------------------------ 状态查询
    private static bool HasStatus(uint statusId) => GetStatusRemain(statusId) > 0f;

    private static float GetStatusRemain(uint statusId)
    {
        try
        {
            var player = Service.ObjectTable.LocalPlayer;
            if (player == null) return 0f;

            foreach (var st in player.StatusList)
            {
                if (st.StatusId == statusId)
                    return st.RemainingTime;
            }
        }
        catch { }
        return 0f;
    }

    /// <summary>
    /// 读任意角色身上的状态。
    /// ICharacter 没有 StatusList（只有 IPlayerCharacter 有），所以目标必须直接读内存：
    /// GameObject 指针 → Character 指针 → GetStatusManager()。
    /// </summary>
    private static unsafe bool TargetHasStatus(FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* go, uint statusId)
    {
        try
        {
            if (go == null) return false;

            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)go;
            var statusManager = character->GetStatusManager();
            if (statusManager == null) return false;

            var count = statusManager->NumValidStatuses;
            if (count > 60) count = 60;   // Status 数组固定 60 槽，越界保护

            var span = statusManager->Status;
            for (var i = 0; i < count; i++)
            {
                if (span[i].StatusId == statusId) return true;
            }
        }
        catch { }
        return false;
    }
}
