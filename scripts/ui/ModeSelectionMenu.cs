using Godot;
using Kuros.Utils;

namespace Kuros.UI
{
    /// <summary>
    /// 模式选择菜单 - 选择游戏模式
    /// </summary>
    public partial class ModeSelectionMenu : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button StoryModeButton { get; private set; } = null!;
        [Export] public Button ArcadeModeButton { get; private set; } = null!;
        [Export] public Button EndlessModeButton { get; private set; } = null!;
        [Export] public Button TestLoadingButton { get; private set; } = null!;
        [Export] public Button BackButton { get; private set; } = null!;
        [Export] public Label TitleLabel { get; private set; } = null!;

        // 信号
        [Signal] public delegate void ModeSelectedEventHandler(string modeName);
        [Signal] public delegate void BackRequestedEventHandler();
        [Signal] public delegate void TestLoadingRequestedEventHandler();

        public override void _Ready()
        {
            // 自动查找节点
            if (TitleLabel == null)
            {
                TitleLabel = GetNodeOrNull<Label>("MenuPanel/VBoxContainer/Title");
            }

            if (StoryModeButton == null)
            {
                StoryModeButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/StoryModeButton");
            }

            if (ArcadeModeButton == null)
            {
                ArcadeModeButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/ArcadeModeButton");
            }

            if (EndlessModeButton == null)
            {
                EndlessModeButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/EndlessModeButton");
            }

            if (TestLoadingButton == null)
            {
                TestLoadingButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/TestLoadingButton");
            }

            if (BackButton == null)
            {
                BackButton = GetNodeOrNull<Button>("MenuPanel/VBoxContainer/BackButton");
            }

            // 连接按钮信号
            if (StoryModeButton != null)
            {
                StoryModeButton.Pressed += () => OnModeSelected("Story");
            }

            if (ArcadeModeButton != null)
            {
                ArcadeModeButton.Pressed += () => OnModeSelected("Arcade");
            }

            if (EndlessModeButton != null)
            {
                EndlessModeButton.Pressed += () => OnModeSelected("Endless");
            }

            if (TestLoadingButton != null)
            {
                TestLoadingButton.Pressed += OnTestLoadingPressed;
            }

            if (BackButton != null)
            {
                BackButton.Pressed += OnBackPressed;
            }
        }

        private void OnModeSelected(string modeName)
        {
            EmitSignal(SignalName.ModeSelected, modeName);
            GameLogger.Info(nameof(ModeSelectionMenu), $"选择了模式: {modeName}");
        }

        private void OnBackPressed()
        {
            EmitSignal(SignalName.BackRequested);
        }
        
        private void OnTestLoadingPressed()
        {
            EmitSignal(SignalName.TestLoadingRequested);
        }

        public override void _Input(InputEvent @event)
        {
            // 只有在控件可见时才处理输入
            if (!IsVisibleInTree())
            {
                return;
            }

            // 检查是否为ESC键（同时检查action和keycode，确保可靠捕获）
            bool isEscKey = false;
            if (@event.IsActionPressed("ui_cancel"))
            {
                isEscKey = true;
            }
            else if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
            {
                isEscKey = true;
            }

            if (isEscKey)
            {
                GD.Print("ModeSelectionMenu._Input: ESC键关闭模式选择菜单");
                OnBackPressed();
                GetViewport().SetInputAsHandled();
            }
        }
    }
}

