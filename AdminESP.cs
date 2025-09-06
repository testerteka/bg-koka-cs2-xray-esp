using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AdminESP;

public sealed partial class AdminESP : BasePlugin, IPluginConfig<Config>
{
    public override string ModuleName => "Admin ESP";
    public override string ModuleAuthor => "AquaVadis";
    public override string ModuleVersion => "1.1.1s";
    public override string ModuleDescription => "Plugin uses code borrowed from CS2Fixes / cs2kz-metamod / hl2sdk / unknown cheats and xstage from CS# discord";

    public bool[] toggleAdminESP = new bool[64];
    public bool togglePlayersGlowing = false;
    public Dictionary<int, Tuple<CBaseModelEntity, CBaseModelEntity>> glowingPlayers = new();
    public List<CCSPlayerController> cachedPlayers = new();
    public Config Config { get; set; } = new();
    private static readonly ConVar? _forceCamera = ConVar.Find("mp_forcecamera");

    public override void Load(bool hotReload)
    {
        RegisterListeners();

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers().Where(p => p is not null && p.IsValid && p.Connected is PlayerConnectedState.PlayerConnected))
            {
                if (!cachedPlayers.Contains(player))
                    cachedPlayers.Add(player);
            }
        }
    }

    public override void Unload(bool hotReload)
    {
        DeregisterListeners();
    }

    [ConsoleCommand("css_esp", "Toggle Admin ESP")]
    [CommandHelper(minArgs: 0, whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnToggleAdminEsp(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid) return;

        if (!AdminManager.PlayerHasPermissions(player, Config.AdminFlag))
        {
            SendMessageToSpecificChat(player, "Admin ESP can only be used from {GREEN}admins{DEFAULT}!", PrintTo.Chat);
            return;
        }

        // Both alive and dead players can use it
        toggleAdminESP[player.Slot] = !toggleAdminESP[player.Slot];

        if (toggleAdminESP[player.Slot])
        {
            if (!togglePlayersGlowing || !AreThereEsperingAdmins())
                SetAllPlayersGlowing();
        }
        else
        {
            if (!togglePlayersGlowing || !AreThereEsperingAdmins())
                RemoveAllGlowingPlayers();
        }

        SendMessageToSpecificChat(player, $"Admin ESP has been " + (toggleAdminESP[player.Slot] ? "{GREEN}enabled!" : "{RED}disabled!"), PrintTo.Chat);
    }

    public void OnConfigParsed(Config config)
    {
        Config = config;
    }

    public bool AreThereEsperingAdmins()
    {
        return toggleAdminESP.Any(t => t);
    }

    public void SetAllPlayersGlowing()
    {
        foreach (var player in cachedPlayers)
        {
            if (player is null || !player.IsValid) continue;
            SetPlayerGlowing(player, (int)player.Team);
        }
        togglePlayersGlowing = true;
    }

    public void RemoveAllGlowingPlayers()
    {
        foreach (var prop in glowingPlayers.Values)
        {
            if (prop.Item1 != null && prop.Item1.IsValid)
                prop.Item1.AcceptInput("Kill", null, null, "", 0);

            if (prop.Item2 != null && prop.Item2.IsValid)
                prop.Item2.AcceptInput("Kill", null, null, "", 0);
        }
        togglePlayersGlowing = false;
        glowingPlayers.Clear();
    }

    public void SetPlayerGlowing(CCSPlayerController player, int team)
    {
        if (player is null || !player.IsValid || player.Connected != PlayerConnectedState.PlayerConnected) return;

        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid) return;

        var modelGlow = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
        var modelRelay = Utilities.CreateEntityByName<CBaseModelEntity>("prop_dynamic");
        if (modelGlow is null || modelRelay is null || !modelGlow.IsValid || !modelRelay.IsValid) return;

        var body = pawn.CBodyComponent;
        if (body is null) return;

        var node = body.SceneNode;
        if (node is null) return;

        string modelName = node.GetSkeletonInstance().ModelState.ModelName;
        modelRelay.SetModel(modelName);
        modelRelay.Spawnflags.Value = 256;
        modelRelay.RenderMode.Value = 10;
        modelRelay.DispatchSpawn();

        modelGlow.SetModel(modelName);
        modelGlow.Spawnflags.Value = 256;
        modelGlow.DispatchSpawn();

        modelGlow.Glow.GlowRange.Value = 5000;
        modelGlow.Glow.GlowTeam.Value = -1;
        modelGlow.Glow.GlowType.Value = 3;
        modelGlow.Glow.GlowRangeMin.Value = 100;

        if (team == (int)CsTeam.CT) modelGlow.Glow.GlowColorOverride = Color.Orange;
        else if (team == (int)CsTeam.T) modelGlow.Glow.GlowColorOverride = Color.SkyBlue;

        modelRelay.AcceptInput("FollowEntity", pawn, modelRelay, "!activator", 0);
        modelGlow.AcceptInput("FollowEntity", modelRelay, modelGlow, "!activator", 0);

        if (glowingPlayers.ContainsKey(player.Slot))
        {
            var old = glowingPlayers[player.Slot];
            old.Item1?.AcceptInput("Kill", null, null, "", 0);
            old.Item2?.AcceptInput("Kill", null, null, "", 0);
            glowingPlayers.Remove(player.Slot);
        }

        glowingPlayers.Add(player.Slot, new Tuple<CBaseModelEntity, CBaseModelEntity>(modelRelay, modelGlow));
    }

    public enum PrintTo
    {
        Chat = 1,
        ChatAll,
        ChatAllNoPrefix,
        ConsoleError,
        ConsoleSucess,
        ConsoleInfo
    }

    public void SendMessageToSpecificChat(CCSPlayerController? handle = null, string msg = "", PrintTo print = PrintTo.Chat)
    {
        string chatPrefix = " " + Config.ChatPrefix + " ≫"; // simplified
        switch (print)
        {
            case PrintTo.Chat:
                handle?.PrintToChat(chatPrefix + " " + msg);
                break;
            case PrintTo.ChatAll:
                Server.PrintToChatAll(chatPrefix + " " + msg);
                break;
            case PrintTo.ChatAllNoPrefix:
                Server.PrintToChatAll(" " + msg);
                break;
            case PrintTo.ConsoleError:
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Debug] " + msg);
                Console.ResetColor();
                break;
            case PrintTo.ConsoleSucess:
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[Debug] " + msg);
                Console.ResetColor();
                break;
            case PrintTo.ConsoleInfo:
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("[Debug] " + msg);
                Console.ResetColor();
                break;
        }
    }

    // TODO: Add listeners and events like OnPlayerSpawn, OnRoundStart, etc., same as before.
}
