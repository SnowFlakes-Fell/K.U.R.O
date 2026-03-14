using Godot;
using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.States
{
    /// <summary>
    /// 冻结状态：玩家被控制时无法移动或执行输入，直到持续时间结束。
    /// </summary>
    public partial class PlayerFrozenState : PlayerState
    {
        public float FrozenDuration = 2.0f;
        public float FrozenAnimationSpeed = 1.0f;
        [Export] public string SpineFrozenAnimationName = "stun";

        private float _timer;
        private bool _externallyHeld;
        private float _originalSpeedScale = 1.0f;
        private MainCharacter? _mainCharacter;
        private bool _spineAnimationApplied;

        public bool IsExternallyHeld => _externallyHeld;
        public float RemainingHoldRatio
        {
            get
            {
                if (FrozenDuration <= Mathf.Epsilon)
                {
                    return 0f;
                }
                return Mathf.Clamp(_timer / FrozenDuration, 0f, 1f);
            }
        }

        public override void Enter()
        {
            _timer = FrozenDuration;
            _externallyHeld = false;
            Actor.Velocity = Vector2.Zero;
            _mainCharacter = Actor as MainCharacter;
            _spineAnimationApplied = false;

            if (_mainCharacter != null)
            {
                var animName = string.IsNullOrEmpty(SpineFrozenAnimationName)
                    ? _mainCharacter.IdleAnimationName
                    : SpineFrozenAnimationName;
                _mainCharacter.PlaySpineAnimation(animName, true, FrozenAnimationSpeed);
                _spineAnimationApplied = true;
            }
            else if (Actor.AnimPlayer != null)
            {
                // Save original speed scale before modifying
                _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                
                if (Actor.AnimPlayer.HasAnimation("animations/hit"))
                {
                    Actor.AnimPlayer.Play("animations/hit");
                }
                else
                {
                    Actor.AnimPlayer.Play("animations/Idle");
                }
                // Set animation playback speed only for frozen animation
                Actor.AnimPlayer.SpeedScale = FrozenAnimationSpeed;
            }
        }
        
        public override void Exit()
        {
            // Restore original animation speed when leaving frozen state
            if (_spineAnimationApplied)
            {
                // State 离开时交由下一个状态控制 Spine 动画，无需额外处理
            }
            else if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            Actor.Velocity = Vector2.Zero;
            Actor.MoveAndSlide();

            if (_externallyHeld)
            {
                return;
            }

            _timer -= (float)delta;
            if (_timer <= 0)
            {
                ChangeState("Idle");
            }
        }

        public void BeginExternalHold()
        {
            _timer = FrozenDuration;
            _externallyHeld = true;
        }

        public void EndExternalHold()
        {
            _externallyHeld = false;
			_timer = 0f;
			ChangeState("Idle");
        }
    }
}

