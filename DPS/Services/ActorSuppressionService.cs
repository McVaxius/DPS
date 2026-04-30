using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using DalamudObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace DPS.Services;

public sealed class ActorSuppressionService
{
    private readonly HashSet<uint> hiddenEntityIds = new();
    private const VisibilityFlags HiddenFlags = VisibilityFlags.Model | VisibilityFlags.Nameplate;

    public int HiddenPlayers { get; private set; }
    public int HiddenPets { get; private set; }
    public int HiddenChocobos { get; private set; }
    public int HiddenMinions { get; private set; }
    public string Status { get; private set; } = "Suppression idle.";

    public string Summary => "Render-flag suppression for non-party actors.";

    public unsafe void Update(Configuration configuration)
    {
        if (!configuration.PluginEnabled)
        {
            ShowAll();
            ResetCounters();
            Status = "Suppression disabled.";
            return;
        }

        if (!configuration.HideNonPartyPlayers
         && !configuration.HideNonPartyPets
         && !configuration.HideNonPartyChocobos
         && !configuration.HideNonPartyMinions)
        {
            ShowAll();
            ResetCounters();
            Status = "Suppression idle.";
            return;
        }

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null)
        {
            Status = "Suppression waiting for local player.";
            return;
        }

        var hiddenPlayers = 0;
        var hiddenPets = 0;
        var hiddenChocobos = 0;
        var hiddenMinions = 0;
        var targetId = configuration.KeepCurrentTargetVisible ? Plugin.TargetManager.Target?.EntityId ?? 0 : 0;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj == null || obj.Address == nint.Zero || obj.EntityId == 0 || obj.EntityId == localPlayer.EntityId)
            {
                continue;
            }

            var character = (Character*)obj.Address;
            switch (obj.ObjectKind)
            {
                case DalamudObjectKind.Pc:
                {
                    var shouldShow = !configuration.HideNonPartyPlayers
                                     || IsPartyMember(obj.EntityId)
                                     || obj.EntityId == targetId;
                    ProcessCharacter(character, shouldShow, ref hiddenPlayers);
                    break;
                }

                case DalamudObjectKind.BattleNpc when character->GameObject.SubKind == 2:
                {
                    var shouldShow = character->GameObject.OwnerId == localPlayer.EntityId ||
                                     IsPartyMember(character->GameObject.OwnerId) ||
                                     obj.EntityId == targetId ||
                                     !configuration.HideNonPartyPets;
                    ProcessCharacter(character, shouldShow, ref hiddenPets);
                    break;
                }

                case DalamudObjectKind.BattleNpc when character->GameObject.SubKind == 3:
                {
                    var shouldShow = character->GameObject.OwnerId == localPlayer.EntityId ||
                                     IsPartyMember(character->GameObject.OwnerId) ||
                                     obj.EntityId == targetId ||
                                     !configuration.HideNonPartyChocobos;
                    ProcessCharacter(character, shouldShow, ref hiddenChocobos);
                    break;
                }

                case DalamudObjectKind.Companion:
                {
                    var shouldShow = character->CompanionOwnerId == localPlayer.EntityId ||
                                     IsPartyMember(character->CompanionOwnerId) ||
                                     obj.EntityId == targetId ||
                                     !configuration.HideNonPartyMinions;
                    ProcessCharacter(character, shouldShow, ref hiddenMinions);
                    break;
                }
            }
        }

        HiddenPlayers = hiddenPlayers;
        HiddenPets = hiddenPets;
        HiddenChocobos = hiddenChocobos;
        HiddenMinions = hiddenMinions;
        var totalHidden = hiddenPlayers + hiddenPets + hiddenChocobos + hiddenMinions;
        Status = totalHidden > 0
            ? $"Suppression active: {totalHidden} hidden."
            : "Suppression active: nothing currently hidden.";
    }

    public unsafe void ShowAll()
    {
        foreach (var obj in Plugin.ObjectTable.Where(obj => obj != null && obj.Address != nint.Zero && hiddenEntityIds.Contains(obj.EntityId)))
        {
            var character = (Character*)obj.Address;
            character->GameObject.EnableDraw();
            character->GameObject.RenderFlags &= ~HiddenFlags;
        }

        hiddenEntityIds.Clear();
        Status = "All hidden actors restored.";
    }

    private unsafe void ProcessCharacter(Character* character, bool shouldShow, ref int counter)
    {
        var entityId = character->GameObject.EntityId;
        if (shouldShow)
        {
            if (hiddenEntityIds.Remove(entityId))
            {
                character->GameObject.EnableDraw();
                character->GameObject.RenderFlags &= ~HiddenFlags;
            }
            return;
        }

        counter++;
        if (!hiddenEntityIds.Contains(entityId))
        {
            hiddenEntityIds.Add(entityId);
            character->GameObject.DisableDraw();
            character->GameObject.RenderFlags |= HiddenFlags;
            return;
        }

        character->GameObject.DisableDraw();
        character->GameObject.RenderFlags |= HiddenFlags;
    }

    private void ResetCounters()
    {
        HiddenPlayers = 0;
        HiddenPets = 0;
        HiddenChocobos = 0;
        HiddenMinions = 0;
    }

    private static unsafe bool IsPartyMember(uint entityId)
    {
        var groupManager = GroupManager.Instance();
        if (groupManager != null && groupManager->MainGroup.MemberCount > 0 && groupManager->MainGroup.IsEntityIdInParty(entityId))
            return true;

        var crossRealm = InfoProxyCrossRealm.Instance();
        if (crossRealm == null || !crossRealm->IsInCrossRealmParty)
            return false;

        foreach (var group in crossRealm->CrossRealmGroups)
        {
            for (var index = 0; index < group.GroupMembers.Length; index++)
            {
                if (group.GroupMembers[index].EntityId == entityId)
                    return true;
            }
        }

        return false;
    }
}
