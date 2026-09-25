#if ANDROID
using Android.Graphics;
using Android.Views;
using Android.Widget;
using AndroidX.DynamicAnimation;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Controls.Handlers.Items;

namespace Plugin.Maui.Spine.Controls;

public partial class HeroCollectionView
{
    partial void OnHandlerChangedPartial()
    {
        if (Handler is CollectionViewHandler handler &&
            handler.PlatformView is RecyclerView rv)
        {
            rv.OverScrollMode = OverScrollMode.Always;
            rv.SetEdgeEffectFactory(new BounceEdgeEffectFactory(this));
        }
    }

    private void OnTopEdgePulled(double pull)
    {
        _topPull = pull;
        ApplyStretch(pull);
    }

    private sealed class BounceEdgeEffectFactory(HeroCollectionView hero) : RecyclerView.EdgeEffectFactory
    {
        protected override EdgeEffect CreateEdgeEffect(RecyclerView view, int direction) => direction switch
        {
            DirectionTop    => new BounceEdgeEffect(view, hero, top: true),
            DirectionBottom => new BounceEdgeEffect(view, hero, top: false),
            _               => base.CreateEdgeEffect(view, direction)
        };
    }

    // Replaces Android's stretch, whose displacement cannot be read, with the list itself moving
    // like iOS's rubber band. The header can then follow the top edge by exactly the same distance,
    // and since the offset never changes the bounce is never mistaken for a scroll.
    private sealed class BounceEdgeEffect : EdgeEffect
    {
        private const float Resistance = 0.55f;
        private const float AbsorbVelocityFactor = 0.5f;

        private readonly RecyclerView _list;
        private readonly HeroCollectionView _hero;
        private readonly bool _top;
        private SpringAnimation? _spring;

        public BounceEdgeEffect(RecyclerView list, HeroCollectionView hero, bool top) : base(list.Context)
        {
            _list = list;
            _hero = hero;
            _top  = top;
        }

        private float Height => Math.Max(1, _list.Height);

        // How far the list has moved away from this edge, in pixels. The top and bottom effects share
        // the list's translation, so each sees only its own side of it.
        private float Displacement => Math.Max(0, _top ? _list.TranslationY : -_list.TranslationY);

        // The finger distance (as a fraction of the height) that produces the current displacement.
        private float PullFraction
        {
            get
            {
                float t = Math.Clamp(Displacement / Height, 0, 0.99f);
                return (1 / (1 - t) - 1) / Resistance;
            }
        }

        private void MoveTo(float displacement)
        {
            _list.TranslationY = _top ? displacement : -displacement;
            if (_top) _hero.OnTopEdgePulled(displacement / _list.Resources!.DisplayMetrics!.Density);
        }

        private void SetPullFraction(float fraction) =>
            MoveTo((1 - 1 / (fraction * Resistance + 1)) * Height);

        private void Settle(float velocity)
        {
            _spring ??= CreateSpring();
            _spring.SetStartVelocity(_top ? velocity : -velocity);
            _spring.Start();
        }

        private SpringAnimation CreateSpring()
        {
            var spring = new SpringAnimation(_list, DynamicAnimation.TranslationY, 0);
            spring.Spring!.SetDampingRatio(SpringForce.DampingRatioNoBouncy);
            spring.Spring.SetStiffness(SpringForce.StiffnessLow);
            spring.AddUpdateListener(new SpringUpdate(this));
            return spring;
        }

        private void StopSpring()
        {
            if (_spring is { IsRunning: true }) _spring.Cancel();
        }

        public override void OnPull(float deltaDistance) => OnPull(deltaDistance, 0.5f);

        public override void OnPull(float deltaDistance, float displacement) =>
            OnPullDistance(deltaDistance, displacement);

        public override float OnPullDistance(float deltaDistance, float displacement)
        {
            StopSpring();
            float current = PullFraction;
            float next    = Math.Max(0, current + deltaDistance);
            SetPullFraction(next);
            return next - current;
        }

        public override float Distance => Displacement == 0 ? 0 : PullFraction;

        public override void OnRelease()
        {
            if (Displacement != 0) Settle(0);
        }

        public override void OnAbsorb(int velocity) => Settle(velocity * AbsorbVelocityFactor);

        public override bool IsFinished => Displacement == 0 && _spring is not { IsRunning: true };

        public override void Finish()
        {
            StopSpring();
            MoveTo(0);
        }

        public override bool Draw(Canvas? canvas) => false;

        private sealed class SpringUpdate(BounceEdgeEffect effect) : Java.Lang.Object, DynamicAnimation.IOnAnimationUpdateListener
        {
            public void OnAnimationUpdate(DynamicAnimation? animation, float value, float velocity)
            {
                if (effect._top) effect._hero.OnTopEdgePulled(Math.Max(0, value) / effect._list.Resources!.DisplayMetrics!.Density);
            }
        }
    }
}
#endif
