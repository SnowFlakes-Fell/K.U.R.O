using System;
using Godot;
using Kuros.Core;
using Kuros.Items;
using Kuros.Items.World;
using Kuros.Managers;
using Kuros.Systems.Inventory;
using Kuros.UI;
using Kuros.Utils;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 负责处理玩家与背包物品之间的放置/投掷交互。
    /// </summary>
    public partial class PlayerItemInteractionComponent : Node
    {
        private enum DropDisposition
        {
            Place,
            Throw
        }

        [Export] public PlayerInventoryComponent? InventoryComponent { get; private set; }
        [Export] public Vector2 DropOffset = new Vector2(32, 0);
        [Export] public Vector2 ThrowOffset = new Vector2(48, -10);
        [Export(PropertyHint.Range, "0,2000,1")] public float ThrowImpulse = 800f;
        [Export] public bool EnableInput = true;
        [Export] public string ThrowStateName { get; set; } = "Throw";

        private GameActor? _actor;
        private bool _pickupToBackpack;

        public override void _Ready()
        {
            base._Ready();

            _actor = GetParent() as GameActor ?? GetOwner() as GameActor;
            InventoryComponent ??= GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            InventoryComponent ??= FindChildComponent<PlayerInventoryComponent>(GetParent());

            if (InventoryComponent == null)
            {
                GameLogger.Error(nameof(PlayerItemInteractionComponent), $"{Name} 未能找到 PlayerInventoryComponent。");
            }

            SetProcess(true);
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (!EnableInput || InventoryComponent?.Backpack == null)
            {
                return;
            }

            if (Input.IsActionJustPressed("put_down"))
            {
                TryHandleDrop(DropDisposition.Place);
            }

            if (Input.IsActionJustPressed("throw"))
            {
                TryHandleDrop(DropDisposition.Throw);
            }

            if (Input.IsActionJustPressed("item_select_right"))
            {
                InventoryComponent?.SelectNextBackpackSlot();
            }

            if (Input.IsActionJustPressed("item_select_left"))
            {
                InventoryComponent?.SelectPreviousBackpackSlot();
            }

            if (Input.IsActionJustPressed("take_up"))
            {
                TriggerPickupState();
            }
        }

        public bool TryTriggerThrowAfterAnimation()
        {
            return TryHandleDrop(DropDisposition.Throw, skipAnimation: true);
        }

        private bool TryHandleDrop(DropDisposition disposition)
        {
            return TryHandleDrop(disposition, skipAnimation: false);
        }

        private bool TryHandleDrop(DropDisposition disposition, bool skipAnimation)
        {
            if (InventoryComponent == null)
            {
                return false;
            }

            // 檢查是否是 SamplePlayer，以便從左手槽位獲取物品
            if (_actor is not SamplePlayer player)
            {
                GD.Print("[TryHandleDrop] Actor is not SamplePlayer, cannot drop left hand item");
                return false;
            }

            // 檢查左手槽位索引是否有效（1-4）
            if (player.LeftHandSlotIndex < 1 || player.LeftHandSlotIndex > 4)
            {
                GD.Print($"[TryHandleDrop] Invalid left hand slot index: {player.LeftHandSlotIndex}");
                return false;
            }

            // 從快捷欄的左手槽位獲取物品
            if (InventoryComponent.QuickBar == null)
            {
                GD.Print("[TryHandleDrop] QuickBar is null");
                return false;
            }

            var selectedStack = InventoryComponent.QuickBar.GetStack(player.LeftHandSlotIndex);
            
            // 檢查槽位是否有有效物品（排除空白道具）
            if (selectedStack == null || selectedStack.IsEmpty || selectedStack.Item.ItemId == "empty_item")
            {
                GD.Print($"[TryHandleDrop] No valid item in left hand slot {player.LeftHandSlotIndex}");
                return false;
            }

            if (!skipAnimation && disposition == DropDisposition.Throw)
            {
                if (TryTriggerThrowState())
                {
                    return false;
                }

                return TryHandleDrop(disposition, skipAnimation: true);
            }

            // 先檢查物品是否可以生成世界物品場景（避免移除後無法生成導致無限循環）
            var itemToRemove = selectedStack.Item;
            var worldScene = WorldItemSpawner.ResolveScene(itemToRemove);
            if (worldScene == null)
            {
                GD.PrintErr($"[TryHandleDrop] Item '{itemToRemove.DisplayName}' (ID: {itemToRemove.ItemId}) has no world scene configured, cannot drop.");
                return false;
            }

            // 從快捷欄的左手槽位移除物品
            int quantityToRemove = selectedStack.Quantity;
            int removed = InventoryComponent.QuickBar.RemoveItemFromSlot(player.LeftHandSlotIndex, quantityToRemove);
            
            if (removed <= 0)
            {
                GD.Print($"[TryHandleDrop] Failed to remove item from left hand slot {player.LeftHandSlotIndex}");
                return false;
            }

            GD.Print($"[TryHandleDrop] Removed {removed} x {itemToRemove.DisplayName} from left hand slot {player.LeftHandSlotIndex}");

            // 創建提取的物品堆疊
            var extracted = new InventoryItemStack(itemToRemove, removed);

            var spawnPosition = ComputeSpawnPosition(disposition);
            var entity = WorldItemSpawner.SpawnFromStack(this, extracted, spawnPosition);

            if (entity == null)
            {
                // Recovery path: spawn failed, try to return extracted items to inventory
                GD.PrintErr($"[TryHandleDrop] Failed to spawn world item for {itemToRemove.DisplayName}");
                
                int originalQuantity = extracted.Quantity;
                int totalRecovered = 0;

                // Step 1: Try to return items to the left hand slot first
                int addedBack = InventoryComponent.QuickBar.TryAddItemToSlot(itemToRemove, extracted.Quantity, player.LeftHandSlotIndex);
                if (addedBack > 0)
                {
                    totalRecovered += addedBack;
                    extracted.Remove(addedBack);
                }

                // Step 2: If there are remaining items, try to add them to any available inventory slot
                if (!extracted.IsEmpty && InventoryComponent.Backpack != null)
                {
                    int remainingQuantity = extracted.Quantity;
                    int addedToBackpack = InventoryComponent.Backpack.AddItem(extracted.Item, remainingQuantity);

                    if (addedToBackpack > 0)
                    {
                        totalRecovered += addedToBackpack;
                        int safeRemove = Math.Min(addedToBackpack, extracted.Quantity);
                        if (safeRemove > 0)
                        {
                            extracted.Remove(safeRemove);
                        }
                    }
                }

                // Step 3: Handle any remaining items that couldn't be recovered
                if (!extracted.IsEmpty)
                {
                    int lostQuantity = extracted.Quantity;
                    GameLogger.Error(
                        nameof(PlayerItemInteractionComponent),
                        $"[Item Recovery] Failed to recover {lostQuantity}x '{extracted.Item?.ItemId ?? "unknown"}' " +
                        $"(recovered {totalRecovered}/{originalQuantity}). Items lost due to spawn failure and full inventory.");

                    extracted.Remove(lostQuantity);
                }

                // 同步更新左手物品
                player.SyncLeftHandItemFromSlot();
                player.UpdateHandItemVisual();
                UpdateBattleHUDQuickBar(player);
                return false;
            }

            if (disposition == DropDisposition.Throw)
            {
                entity.ApplyThrowImpulse(GetFacingDirection() * ThrowImpulse);
            }

            // 在移除物品後添加空白道具到左手槽位
            var updatedStack = InventoryComponent.QuickBar.GetStack(player.LeftHandSlotIndex);
            if (updatedStack == null || updatedStack.IsEmpty)
            {
                var emptyItem = GD.Load<ItemDefinition>("res://data/EmptyItem.tres");
                if (emptyItem != null)
                {
                    InventoryComponent.QuickBar.TryAddItemToSlot(emptyItem, 1, player.LeftHandSlotIndex);
                    GD.Print($"[TryHandleDrop] Added empty item to slot {player.LeftHandSlotIndex}");
                }
            }

            // 同步更新左手物品狀態
            player.SyncLeftHandItemFromSlot();
            player.UpdateHandItemVisual();

            // 更新 BattleHUD 快捷欄顯示
            UpdateBattleHUDQuickBar(player);

            InventoryComponent.NotifyItemRemoved(itemToRemove.ItemId);
            GD.Print($"[TryHandleDrop] Successfully {(disposition == DropDisposition.Throw ? "threw" : "dropped")} {itemToRemove.DisplayName}");
            return true;
        }

        /// <summary>
        /// 更新 BattleHUD 快捷欄顯示
        /// </summary>
        private void UpdateBattleHUDQuickBar(SamplePlayer player)
        {
            BattleHUD? battleHUD = null;
            if (UIManager.Instance != null)
            {
                battleHUD = UIManager.Instance.GetUI<BattleHUD>("BattleHUD");
            }
            
            if (battleHUD == null && _actor != null)
            {
                // 備用方案：通過場景樹查找
                battleHUD = _actor.GetTree().GetFirstNodeInGroup("ui") as BattleHUD;
            }
            
            if (battleHUD != null)
            {
                GD.Print("[TryHandleDrop] Found BattleHUD, requesting quickbar refresh");
                // 更新所有快捷欄槽位的顯示
                battleHUD.CallDeferred("UpdateQuickBarDisplay");
                // 保持當前的左手選擇狀態
                int leftHandSlot = player.LeftHandSlotIndex >= 1 && player.LeftHandSlotIndex < 5 ? player.LeftHandSlotIndex : -1;
                battleHUD.CallDeferred("UpdateHandSlotHighlight", leftHandSlot, 0);
            }
        }

        private Vector2 ComputeSpawnPosition(DropDisposition disposition)
        {
            var origin = _actor?.GlobalPosition ?? Vector2.Zero;
            var direction = GetFacingDirection();
            var offset = disposition == DropDisposition.Throw ? ThrowOffset : DropOffset;
            return origin + new Vector2(direction.X * offset.X, offset.Y);
        }

        internal bool ExecutePickupAfterAnimation() => TryHandlePickup(pickupToBackpack: _pickupToBackpack);

        private void TriggerPickupState()
        {
            // 即使左手有物品也允许拾取，物品会被放入物品栏
            // 检查左手是否有物品，用于决定拾取目标位置
            bool hasLeftHandItem = HasLeftHandItem();
            if (hasLeftHandItem)
            {
                GD.Print("[F键] 左手已有物品，拾取的物品将放入物品栏");
            }

            if (_actor?.StateMachine == null)
            {
                TryHandlePickup(pickupToBackpack: hasLeftHandItem);
                return;
            }

            if (_actor.StateMachine.HasState("PickUp"))
            {
                // 存储是否应该拾取到背包的状态，供状态机使用
                _pickupToBackpack = hasLeftHandItem;
                _actor.StateMachine.ChangeState("PickUp");
            }
            else
            {
                GameLogger.Warn(nameof(PlayerItemInteractionComponent), "StateMachine 中未找到 'PickUp' 状态，直接执行拾取逻辑。");
                TryHandlePickup(pickupToBackpack: hasLeftHandItem);
            }
        }

        /// <summary>
        /// 检查左手槽位是否有物品
        /// </summary>
        private bool HasLeftHandItem()
        {
            if (_actor is not SamplePlayer player)
            {
                // 如果不是 SamplePlayer，回退到原来的背包检查
                return InventoryComponent?.HasSelectedItem == true;
            }
            
            // 检查左手槽位索引是否有效
            if (player.LeftHandSlotIndex < 1 || player.LeftHandSlotIndex > 4)
            {
                return false;
            }
            
            // 检查快捷栏对应槽位是否有物品
            if (InventoryComponent?.QuickBar == null)
            {
                return false;
            }
            
            var stack = InventoryComponent.QuickBar.GetStack(player.LeftHandSlotIndex);
            // 检查槽位是否有有效物品（排除空白道具）
            return stack != null && !stack.IsEmpty && stack.Item.ItemId != "empty_item";
        }

        private bool TryHandlePickup(bool pickupToBackpack = false)
        {
            if (_actor == null)
            {
                return false;
            }

            var area = _actor.GetNodeOrNull<Area2D>("SpineCharacter/AttackArea");
            if (area == null)
            {
                return false;
            }

            // 方式1：检测 WorldItemEntity（CharacterBody2D 类型）
            var bodies = area.GetOverlappingBodies();
            foreach (var body in bodies)
            {
                if (body is WorldItemEntity entity)
                {
                    if (entity.TryPickupByActor(_actor, pickupToBackpack))
                    {
                        return true;
                    }
                }
            }

            // 方式2：检测 Area2D 类型的可拾取物品（如 DroppableExampleProperty）
            var areas = area.GetOverlappingAreas();
            foreach (var overlappingArea in areas)
            {
                // 检查该 Area2D 的父节点是否是可拾取物品
                var parent = overlappingArea.GetParent();
                if (parent is PickupProperty pickupProperty)
                {
                    pickupProperty._TriggerPickup(_actor, pickupToBackpack);
                    return true;
                }
            }

            return false;
        }

        private Vector2 GetFacingDirection()
        {
            if (_actor == null)
            {
                return Vector2.Right;
            }

            return _actor.FacingRight ? Vector2.Right : Vector2.Left;
        }

        private bool TryTriggerThrowState()
        {
            if (_actor?.StateMachine == null)
            {
                return false;
            }

            if (!_actor.StateMachine.HasState(ThrowStateName))
            {
                return false;
            }

            _actor.StateMachine.ChangeState(ThrowStateName);
            return true;
        }

        private static T? FindChildComponent<T>(Node? root) where T : Node
        {
            if (root == null)
            {
                return null;
            }

            foreach (Node child in root.GetChildren())
            {
                if (child is T typed)
                {
                    return typed;
                }

                if (child.GetChildCount() > 0)
                {
                    var nested = FindChildComponent<T>(child);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }
    }
}
