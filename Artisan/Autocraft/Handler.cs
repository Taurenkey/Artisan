﻿using Artisan.CraftingLogic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface.Components;
using Dalamud.Logging;
using Dalamud.Memory;
using Dalamud.Utility.Signatures;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ECommons.GenericHelpers;

namespace Artisan.Autocraft
{
    internal unsafe class Handler
    {
        /*delegate IntPtr BeginSynthesis(IntPtr a1, IntPtr a2, IntPtr a3, int a4);
        [Signature("40 55 53 41 54 41 55 48 8B EC", DetourName = nameof(BeginSynthesisDetour), Fallibility = Fallibility.Infallible)]
        static Hook<BeginSynthesis>? BeginSynthesisHook;*/

        internal static bool Enable = false;
        internal static List<int>? HQData = null;
        internal static int RecipeID = 0;

        internal static void Init()
        {
            SignatureHelper.Initialise(new Handler());
            //BeginSynthesisHook.Enable();
            Svc.Framework.Update += Framework_Update;
            Svc.Toasts.ErrorToast += Toasts_ErrorToast;
        }

        /*internal static IntPtr BeginSynthesisDetour(IntPtr a1, IntPtr a2, IntPtr a3, int a4)
        {
            var ret = BeginSynthesisHook.Original(a1, a2, a3, 4);
            var recipeId = *(int*)(a1 + 528);
            PluginLog.Debug($"Crafting recipe: {recipeId}");
            return ret;
        }*/

        private static void Toasts_ErrorToast(ref Dalamud.Game.Text.SeStringHandling.SeString message, ref bool isHandled)
        {
            if (Enable)
            {
                if(message.ToString().ContainsAny("Unable to craft.", "You do not have"))
                {
                    Enable = false;
                }
            }
        }

        internal static void Dispose()
        {
            //BeginSynthesisHook?.Disable();
            //BeginSynthesisHook?.Dispose();
            Svc.Framework.Update -= Framework_Update;
            Svc.Toasts.ErrorToast -= Toasts_ErrorToast;
        }

        private static void Framework_Update(Dalamud.Game.Framework framework)
        {
            if (Enable)
            {
                if(!Throttler.Throttle(0))
                {
                    return;
                }
                if (Svc.Condition[ConditionFlag.Occupied39])
                {
                    Throttler.Rethrottle(1000);
                }
                PluginLog.Verbose("Throttle success");
                if (HQData == null)
                {
                    DuoLog.Error("HQ data is null");
                    Enable = false;
                    return;
                }
                PluginLog.Verbose("HQ not null");
                if(Service.Configuration.Repair && RepairManager.ProcessRepair(false))
                {
                    if (TryGetAddonByName<AtkUnitBase>("RecipeNote", out var addon) && addon->IsVisible && Svc.Condition[ConditionFlag.Crafting])
                    {
                        if (Throttler.Throttle(1000))
                        {
                            //PluginLog.Verbose("Closing crafting log");
                            CommandProcessor.ExecuteThrottled("/clog");
                        }
                    }
                    else
                    {
                        if (!Svc.Condition[ConditionFlag.Crafting]) RepairManager.ProcessRepair(true);
                    }
                    return;
                }
                PluginLog.Verbose("Repair ok");
                if (!ConsumableChecker.CheckConsumables(false))
                {
                    if(TryGetAddonByName<AtkUnitBase>("RecipeNote", out var addon) && addon->IsVisible && Svc.Condition[ConditionFlag.Crafting])
                    {
                        if (Throttler.Throttle(1000))
                        {
                            //PluginLog.Verbose("Closing crafting log");
                            CommandProcessor.ExecuteThrottled("/clog");
                        }
                    }
                    else
                    {
                        if(!Svc.Condition[ConditionFlag.Crafting]) ConsumableChecker.CheckConsumables(true);
                    }
                    return;
                }
                PluginLog.Verbose("Consumables success");
                {
                    if (TryGetAddonByName<AtkUnitBase>("RecipeNote", out var addon) && addon->IsVisible)
                    {
                        PluginLog.Verbose("Addon visible");
                        if (addon->UldManager.NodeListCount >= 87 && !addon->UldManager.NodeList[87]->GetAsAtkTextNode()->AtkResNode.IsVisible)
                        {
                            PluginLog.Verbose("Error text not visible");
                            if (!HQManager.RestoreHQData(HQData, out var fin) || !fin)
                            {
                                return;
                            }
                            //PluginLog.Verbose("HQ data restored");
                            CurrentCraft.RepeatActualCraft();
                        }
                    }
                    else
                    {
                        if (!Svc.Condition[ConditionFlag.Crafting])
                        {
                            //PluginLog.Verbose("Addon invisible");
                            if (Throttler.Throttle(1000))
                            {
                                //PluginLog.Verbose("Opening crafting log");
                                if (RecipeID == 0)
                                {
                                    CommandProcessor.ExecuteThrottled("/clog");
                                }
                                else
                                {
                                    AgentRecipeNote.Instance()->OpenRecipeByRecipeIdInternal((uint)RecipeID);
                                }
                            }
                        }
                    }
                }
            }
        }

        internal static void Draw()
        {
            ImGui.Checkbox("Enable Endurance Mode", ref Enable);
            ImGuiComponents.HelpMarker("In order to begin endurance crafting you should first select the recipe and NQ/HQ material distribution in the crafting menu.");
            if (!Enable)
            {
                if(HQManager.TryGetCurrent(out var d))
                {
                    HQData = d;
                }
                RecipeID = 0;
                if(TryGetAddonByName<AtkUnitBase>("RecipeNote", out var addon) && addon->IsVisible && addon->UldManager.NodeListCount >= 87
                    && !addon->UldManager.NodeList[87]->GetAsAtkTextNode()->AtkResNode.IsVisible && addon->UldManager.NodeList[76]->GetAsAtkTextNode()->NodeText.ToString() == "History")
                {
                    var text = addon->UldManager.NodeList[49]->GetAsAtkTextNode()->NodeText;
                    var str = MemoryHelper.ReadSeString(&text);
                    foreach (var payload in str.Payloads)
                    {
                        if(payload is TextPayload tp)
                        {
                            /*
                             *  0	3	2	Woodworking
                                1	1	5	Smithing
                                2	3	1	Armorcraft
                                3	2	4	Goldsmithing
                                4	3	4	Leatherworking
                                5	2	5	Clothcraft
                                6	4	6	Alchemy
                                7	5	6	Cooking

                                8	carpenter
                                9	blacksmith
                                10	armorer
                                11	goldsmith
                                12	leatherworker
                                13	weaver
                                14	alchemist
                                15	culinarian
                                (ClassJob - 8)
                             * 
                             * */
                            if (Svc.Data.GetExcelSheet<Recipe>().TryGetFirst(x => x.ItemResult.Value.Name.ToString() == tp.Text && x.CraftType.Value.RowId + 8 == Svc.ClientState.LocalPlayer?.ClassJob.Id, out var id))
                            {
                                RecipeID = id.Number;
                                break;
                            }
                        }
                    }
                }
            }
            ImGuiEx.Text($"HQ ingredients: {HQData?.Select(x => x.ToString()).Join(", ")}, Recipe ID: {RecipeID}");
            {
                ImGuiEx.TextV("Maintain food buff:");
                ImGui.SameLine(150f.Scale());
                ImGuiEx.SetNextItemFullWidth();
                if (ImGui.BeginCombo("##foodBuff", ConsumableChecker.Food.TryGetFirst(x => x.Id == Service.Configuration.Food, out var item) ? $"{(Service.Configuration.FoodHQ ? " " : "")}{item.Name}" : $"{(Service.Configuration.Food == 0 ? "Disabled" : $"{(Service.Configuration.FoodHQ ? " " : "")}{Service.Configuration.Food}")}"))
                {
                    if (ImGui.Selectable("Disable"))
                    {
                        Service.Configuration.Food = 0;
                    }
                    foreach (var x in ConsumableChecker.GetFood(true))
                    {
                        if (ImGui.Selectable($"{x.Name}"))
                        {
                            Service.Configuration.Food = x.Id;
                            Service.Configuration.FoodHQ = false;
                        }
                    }
                    foreach (var x in ConsumableChecker.GetFood(true, true))
                    {
                        if (ImGui.Selectable($" {x.Name}"))
                        {
                            Service.Configuration.Food = x.Id;
                            Service.Configuration.FoodHQ = true;
                        }
                    }
                    ImGui.EndCombo();
                }
            }

            {
                ImGuiEx.TextV("Maintain potion buff:");
                ImGui.SameLine(150f.Scale());
                ImGuiEx.SetNextItemFullWidth();
                if (ImGui.BeginCombo("##potBuff", ConsumableChecker.Pots.TryGetFirst(x => x.Id == Service.Configuration.Potion, out var item) ? $"{(Service.Configuration.PotHQ ? " " : "")}{item.Name}" : $"{(Service.Configuration.Potion == 0 ? "Disabled" : $"{(Service.Configuration.PotHQ ? " " : "")}{Service.Configuration.Potion}")}"))
                {
                    if (ImGui.Selectable("Disable"))
                    {
                        Service.Configuration.Potion = 0;
                    }
                    foreach (var x in ConsumableChecker.GetPots(true))
                    {
                        if (ImGui.Selectable($"{x.Name}"))
                        {
                            Service.Configuration.Potion = x.Id;
                            Service.Configuration.PotHQ = false;
                        }
                    }
                    foreach (var x in ConsumableChecker.GetPots(true, true))
                    {
                        if (ImGui.Selectable($" {x.Name}"))
                        {
                            Service.Configuration.Potion = x.Id;
                            Service.Configuration.PotHQ = true;
                        }
                    }
                    ImGui.EndCombo();
                }
            }
            ImGui.Checkbox("Stop autocrafting if food/medicine is not found", ref Service.Configuration.AbortIfNoFoodPot);
            ImGui.Checkbox("Auto-repair gear once it falls below %", ref Service.Configuration.Repair);
            if (Service.Configuration.Repair)
            {
                ImGui.SameLine();
                ImGuiEx.SetNextItemFullWidth();
                ImGui.SliderInt("##repairp", ref Service.Configuration.RepairPercent, 10, 100, $"{Service.Configuration.RepairPercent}%%");
            }
        }
    }
}